using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CafeSoftware.Services;

public enum SetupMode { Activate, Remove, RemoveAndDelete }

public record StepResult(bool Ok, string Message);

public record SetupStep(string Title, Func<StepResult> Run);

// What is set up on this PC right now
public record SetupState(bool AccountExists, bool AutoLoginOn, bool StartupScriptInstalled, bool InstalledInFolder)
{
    public bool AllOk => AccountExists && AutoLoginOn && StartupScriptInstalled && InstalledInFolder;
    public bool AnyLockdownPart => AccountExists || AutoLoginOn || StartupScriptInstalled;
}

// The steps that create or remove the Customer account and the lockdown.
// They need administrator rights, so they run in the second, elevated copy of the app (--setup).
public static class WindowsSetup
{
    public const string CustomerName = "Customer";
    public const string InstallDir = @"C:\CafeSoftware";

    private const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

    private static readonly string CommonData =
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private static readonly string StartupFile =
        Path.Combine(CommonData, @"Microsoft\Windows\Start Menu\Programs\Startup\CafeStart.cmd");

    private static readonly string DataDir = Path.Combine(CommonData, "CafeSoftware");

    private static string ExeName => Path.GetFileName(Environment.ProcessPath ?? "CafeSoftware.exe");
    private static string InstalledExe => Path.Combine(InstallDir, ExeName);

    // The setup restarts the app's own .exe with administrator rights, so it needs to be the real .exe
    public static bool CanRun(out string reason)
    {
        if (string.Equals(ExeName, "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Start CafeSoftware from its own .exe file (for example from Visual Studio or a published folder), not through 'dotnet run'.";
            return false;
        }
        reason = "";
        return true;
    }

    // ---------- what is set up now (needs no administrator rights) ----------

    public static SetupState CheckState() => new(
        AccountExists: AccountExists(),
        AutoLoginOn: IsAutoLoginOn(),
        StartupScriptInstalled: File.Exists(StartupFile),
        InstalledInFolder: File.Exists(InstalledExe));

    private static bool AccountExists() => RunTool("net.exe", $"user {CustomerName}").ExitCode == 0;

    private static bool IsAutoLoginOn()
    {
        using var key = Registry.LocalMachine.OpenSubKey(WinlogonKey);
        if (key == null) return false;

        string? on = key.GetValue("AutoAdminLogon")?.ToString();
        string? user = key.GetValue("DefaultUserName")?.ToString();
        return on == "1" && string.Equals(user, CustomerName, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- the steps ----------

    public static List<SetupStep> GetSteps(SetupMode mode)
    {
        switch (mode)
        {
            case SetupMode.Activate:
                return new List<SetupStep>
                {
                    new SetupStep("Copy the program to " + InstallDir, CopyProgram),
                    new SetupStep("Create the Customer account", CreateAccount),
                    new SetupStep("Protect the program folder, open the settings folder", SetPermissions),
                    new SetupStep("Turn on automatic sign-in to Customer", TurnOnAutoLogin),
                    new SetupStep("Install the startup script", InstallStartupScript),
                    new SetupStep("Check the result", VerifyActivated)
                };

            case SetupMode.Remove:
                return new List<SetupStep>
                {
                    new SetupStep("Remove the startup script", RemoveStartupScript),
                    new SetupStep("Turn off automatic sign-in", TurnOffAutoLogin),
                    new SetupStep("Reset the program folder permissions", ResetPermissions),
                    new SetupStep("Clear the restrictions from the Customer profile", ClearRestrictions)
                };

            default:
                return new List<SetupStep>
                {
                    new SetupStep("Remove the startup script", RemoveStartupScript),
                    new SetupStep("Turn off automatic sign-in", TurnOffAutoLogin),
                    new SetupStep("Reset the program folder permissions", ResetPermissions),
                    new SetupStep("Delete the Customer account and its files", DeleteAccount)
                };
        }
    }

    private static StepResult CopyProgram()
    {
        string source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        if (string.Equals(source, InstallDir, StringComparison.OrdinalIgnoreCase))
            return new StepResult(true, "Already running from " + InstallDir);

        CopyDirectory(source, InstallDir);
        return File.Exists(InstalledExe)
            ? new StepResult(true, "Copied the program to " + InstallDir)
            : new StepResult(false, "The copy finished but " + InstalledExe + " was not found");
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private static StepResult CreateAccount()
    {
        if (AccountExists())
        {
            RunTool("net.exe", $"user {CustomerName} /active:yes");
            return new StepResult(true, "The Customer account already exists");
        }

        var r = RunTool("net.exe", $"user {CustomerName} /add /passwordchg:no /expires:never");
        if (r.ExitCode != 0)
        {
            return new StepResult(false, "Windows refused to create the account: " + FirstLine(r.Output) +
                " (if a password rule is the reason, run 'net accounts /minpwlen:0' as administrator and try again)");
        }
        return new StepResult(true, "Created the Customer account with no password");
    }

    // Customers can run the program but not change it. The settings and log folder must be writable.
    private static StepResult SetPermissions()
    {
        var a = RunTool("icacls.exe",
            $"\"{InstallDir}\" /inheritance:r /grant:r *S-1-5-32-544:(OI)(CI)F *S-1-5-18:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX /C /Q");
        if (a.ExitCode != 0)
            return new StepResult(false, "Could not protect " + InstallDir + ": " + FirstLine(a.Output));

        Directory.CreateDirectory(DataDir);
        var b = RunTool("icacls.exe", $"\"{DataDir}\" /grant *S-1-5-32-545:(OI)(CI)M /T /C /Q");
        if (b.ExitCode != 0)
            return new StepResult(false, "Could not open " + DataDir + ": " + FirstLine(b.Output));

        return new StepResult(true, "Program folder is read-only for customers, settings folder is writable");
    }

    private static StepResult TurnOnAutoLogin()
    {
        using var key = Registry.LocalMachine.OpenSubKey(WinlogonKey, writable: true);
        if (key == null) return new StepResult(false, "Could not open the Windows sign-in settings");

        key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
        key.SetValue("DefaultUserName", CustomerName, RegistryValueKind.String);
        key.SetValue("DefaultDomainName", Environment.MachineName, RegistryValueKind.String);
        key.SetValue("DefaultPassword", "", RegistryValueKind.String);
        return new StepResult(true, "The PC will sign in to Customer at startup");
    }

    // Runs for everyone who signs in, but does something only for the Customer account
    private static StepResult InstallStartupScript()
    {
        string[] lines =
        {
            "@echo off",
            $"if /i not \"%USERNAME%\"==\"{CustomerName}\" exit /b",
            "set \"P=HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\"",
            "reg add \"%P%\\System\" /v DisableTaskMgr /t REG_DWORD /d 1 /f >nul",
            "reg add \"%P%\\System\" /v DisableLockWorkstation /t REG_DWORD /d 1 /f >nul",
            "reg add \"%P%\\System\" /v DisableChangePassword /t REG_DWORD /d 1 /f >nul",
            "reg add \"%P%\\System\" /v DisableRegistryTools /t REG_DWORD /d 1 /f >nul",
            "reg add \"%P%\\Explorer\" /v NoRun /t REG_DWORD /d 1 /f >nul",
            "reg add \"%P%\\Explorer\" /v NoControlPanel /t REG_DWORD /d 1 /f >nul",
            "reg add \"HKCU\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableCMD /t REG_DWORD /d 2 /f >nul",
            $"start \"\" \"{InstalledExe}\"",
            "exit /b"
        };

        File.WriteAllLines(StartupFile, lines, Encoding.ASCII);
        return File.Exists(StartupFile)
            ? new StepResult(true, "The restrictions and CafeSoftware start for Customer at sign-in")
            : new StepResult(false, "Could not write " + StartupFile);
    }

    private static StepResult VerifyActivated()
    {
        var s = CheckState();
        if (s.AllOk) return new StepResult(true, "Everything is in place");

        var missing = new List<string>();
        if (!s.AccountExists) missing.Add("the Customer account");
        if (!s.AutoLoginOn) missing.Add("automatic sign-in");
        if (!s.StartupScriptInstalled) missing.Add("the startup script");
        if (!s.InstalledInFolder) missing.Add("the program in " + InstallDir);
        return new StepResult(false, "Still missing: " + string.Join(", ", missing));
    }

    private static StepResult RemoveStartupScript()
    {
        if (File.Exists(StartupFile)) File.Delete(StartupFile);
        return new StepResult(true, "The startup script is removed");
    }

    private static StepResult TurnOffAutoLogin()
    {
        using var key = Registry.LocalMachine.OpenSubKey(WinlogonKey, writable: true);
        if (key == null) return new StepResult(false, "Could not open the Windows sign-in settings");

        key.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
        key.DeleteValue("DefaultPassword", false);
        return new StepResult(true, "Automatic sign-in is off");
    }

    private static StepResult ResetPermissions()
    {
        if (!Directory.Exists(InstallDir)) return new StepResult(true, InstallDir + " does not exist, nothing to reset");

        var r = RunTool("icacls.exe", $"\"{InstallDir}\" /reset /T /C /Q");
        return r.ExitCode == 0
            ? new StepResult(true, "Permissions are back to normal")
            : new StepResult(false, "Could not reset the permissions: " + FirstLine(r.Output));
    }

    // Removes the restrictions the startup script left in the Customer profile
    private static StepResult ClearRestrictions()
    {
        string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        string hive = $@"{drive}\Users\{CustomerName}\NTUSER.DAT";
        if (!File.Exists(hive)) return new StepResult(true, "The Customer profile does not exist, nothing to clear");

        var load = RunTool("reg.exe", $"load HKU\\CafeCust \"{hive}\"");
        if (load.ExitCode != 0)
            return new StepResult(false, "Could not open the Customer profile. If Customer is signed in, restart the PC and try again.");

        try
        {
            foreach (string v in new[] { "DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword", "DisableRegistryTools" })
                RunTool("reg.exe", $"delete \"HKU\\CafeCust\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v {v} /f");
            foreach (string v in new[] { "NoRun", "NoControlPanel" })
                RunTool("reg.exe", $"delete \"HKU\\CafeCust\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v {v} /f");
            RunTool("reg.exe", "delete \"HKU\\CafeCust\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableCMD /f");
        }
        finally
        {
            GC.Collect();
            Thread.Sleep(500);
            RunTool("reg.exe", "unload HKU\\CafeCust");
        }
        return new StepResult(true, "The restrictions are cleared from the Customer profile");
    }

    private static StepResult DeleteAccount()
    {
        var r = RunTool("net.exe", $"user {CustomerName} /delete");
        if (r.ExitCode != 0 && AccountExists())
            return new StepResult(false, "Could not delete the account: " + FirstLine(r.Output) + " (make sure Customer is signed out)");

        try
        {
            string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            string profile = $@"{drive}\Users\{CustomerName}";
            if (Directory.Exists(profile)) Directory.Delete(profile, true);
        }
        catch { /* leftover files are not a problem */ }

        return new StepResult(true, "The Customer account is deleted");
    }

    // ---------- helpers ----------

    private static (int ExitCode, string Output) RunTool(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + file);
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output.Trim());
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no message";
}