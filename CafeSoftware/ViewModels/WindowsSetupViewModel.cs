using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CafeSoftware.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CafeSoftware.ViewModels;

// One line of the checklist: a tick or a cross and a label
public partial class CheckLine : ObservableObject
{
    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mark))]
    private bool _isOk;

    public string Mark => IsOk ? "✔" : "✘";

    public CheckLine(string label) => Label = label;
}

// The "Windows setup" tab: shows what is set up and starts the elevated setup
public partial class WindowsSetupViewModel : ObservableObject
{
    private readonly CheckLine _account = new("Customer account exists");
    private readonly CheckLine _autoLogin = new("Automatic sign-in to Customer is on");
    private readonly CheckLine _script = new("Startup script is installed");
    private readonly CheckLine _installed = new($"The program is in {WindowsSetup.InstallDir}");

    public ObservableCollection<CheckLine> Lines { get; } = new();

    [ObservableProperty] private string _summaryText = "Checking...";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _deleteAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUse))]
    private bool _isBusy;

    public bool CanUse => !IsBusy;

    // The view shows the confirmation box
    public event Func<string, bool>? ConfirmRequested;

    public WindowsSetupViewModel()
    {
        Lines.Add(_account);
        Lines.Add(_autoLogin);
        Lines.Add(_script);
        Lines.Add(_installed);

        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        SetupState state;
        try { state = await Task.Run(WindowsSetup.CheckState); }
        catch (Exception ex)
        {
            StatusText = "Could not check the Windows setup: " + ex.Message;
            return;
        }

        _account.IsOk = state.AccountExists;
        _autoLogin.IsOk = state.AutoLoginOn;
        _script.IsOk = state.StartupScriptInstalled;
        _installed.IsOk = state.InstalledInFolder;

        IsActive = state.AllOk;
        SummaryText = state.AllOk ? "Lockdown is ACTIVE on this PC"
                    : state.AnyLockdownPart ? "Lockdown is only partly set up"
                    : "Lockdown is not set up";
    }

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (IsBusy) return;

        string message =
            "Activate lockdown?\n\n" +
            "Windows will ask for administrator permission. Then this will:\n" +
            $"  •  copy the program to {WindowsSetup.InstallDir}\n" +
            "  •  create the Customer account (no password)\n" +
            "  •  protect the program folder\n" +
            "  •  sign in to Customer automatically at startup\n" +
            "  •  add a startup script that applies the restrictions\n\n" +
            "You will need to restart the PC afterwards.";

        if (ConfirmRequested?.Invoke(message) != true) return;
        await RunElevatedAsync("activate");
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (IsBusy) return;

        string message = DeleteAccount
            ? "Remove the lockdown AND delete the Customer account with all its files?\n\nThis cannot be undone."
            : "Remove the lockdown?\n\nThe Customer account is kept. Automatic sign-in and the restrictions are turned off.";

        if (ConfirmRequested?.Invoke(message) != true) return;
        await RunElevatedAsync(DeleteAccount ? "remove-all" : "remove");
    }

    // Starts a second copy of the app with administrator rights. It shows the progress window and does the steps.
    private async Task RunElevatedAsync(string mode)
    {
        if (!WindowsSetup.CanRun(out string reason))
        {
            StatusText = reason;
            return;
        }

        IsBusy = true;
        StatusText = "Waiting for Windows permission...";
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--setup {mode}")
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                StatusText = "Could not start the setup.";
                return;
            }

            StatusText = "The setup is running in its own window...";
            int code = await Task.Run(() =>
            {
                process.WaitForExit();
                return process.ExitCode;
            });

            StatusText = code == 0
                ? "The setup finished."
                : "The setup finished with problems. See its window or the log (General tab > Open logs folder).";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusText = "Cancelled: Windows permission was not given.";
        }
        catch (Exception ex)
        {
            StatusText = "Could not start the setup: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }
}