using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CafeSoftware.Helper;
using CafeSoftware.Services;
using CafeSoftware.View;

namespace CafeSoftware;

// Starts the app: one copy only, crash protection, then hands over to the KioskController.
// Started with "--setup <mode>" it only shows the Windows setup progress window (run with administrator rights).
public partial class App : Application
{
    public static bool AllowExit { get; set; }

    // C:\ProgramData\CafeSoftware\settings.json (read the first time it is needed)
    private static readonly Lazy<ConfigurationController> _config = new(() => new ConfigurationController(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CafeSoftware", "settings.json")));

    public static ConfigurationController Config => _config.Value;

    // Shortcuts used by the windows and view models
    public static bool AdminOpen => _kiosk?.AdminOpen ?? false;
    public static SerialPortService Serial => _kiosk.Serial;
    public static SessionService Session => _kiosk.Session;
    public static void InsertCoin(int pesos) => _kiosk.InsertCoin(pesos);
    public static void OpenAdmin(Window owner) => _kiosk.OpenAdmin(owner);

    private static KioskController _kiosk = null!;
    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private static int _crashCount;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The elevated copy started from Settings > Windows setup: only the progress window, no kiosk
        if (e.Args.Length >= 2 && e.Args[0] == "--setup")
        {
            RunSetupMode(e.Args[1]);
            return;
        }

        _crashCount = ParseCrashCount(e.Args);

        // Only one copy of the app may run. After a crash restart, wait for the old copy to finish.
        _mutex = new Mutex(false, @"Local\CafeSoftware_SingleInstance");
        try
        {
            _ownsMutex = _mutex.WaitOne(_crashCount > 0 ? TimeSpan.FromSeconds(8) : TimeSpan.Zero, false);
        }
        catch (AbandonedMutexException) { _ownsMutex = true; }

        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SetupCrashHandling();

        AppLog.CleanOld();
        AppLog.Write(_crashCount > 0 ? $"App restarted after a crash (#{_crashCount})" : "App started");

        _kiosk = new KioskController(Config);
        _kiosk.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            AppLog.Write("App closed");
            _kiosk?.Stop();
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        base.OnExit(e);
    }

    // ---------- Windows setup (elevated copy) ----------

    private void RunSetupMode(string mode)
    {
        var window = new SetupProgressWindow(mode);
        window.Closed += (_, _) => Shutdown(window.ExitCodeValue);
        window.Show();
    }

    // ---------- crash protection ----------

    private static int ParseCrashCount(string[] args)
    {
        foreach (var a in args)
            if (a.StartsWith("--crash=") && int.TryParse(a.Substring(8), out int n))
                return n;
        return 0;
    }

    private void SetupCrashHandling()
    {
        // A normal error inside the app: log it and keep running (the paid time is kept)
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write("ERROR (app kept running): " + args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write("ERROR in background task: " + args.Exception);
            args.SetObserved();
        };

        // A real crash: log it and start a fresh copy
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppLog.Write("CRASH: " + args.ExceptionObject);
            RestartAfterCrash();
        };

        // If the app has been healthy for 10 minutes, forget earlier crashes
        var healthy = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        healthy.Tick += (_, _) => { _crashCount = 0; healthy.Stop(); };
        healthy.Start();
    }

    private static void RestartAfterCrash()
    {
        try
        {
            if (_crashCount >= 5)
            {
                AppLog.Write("Too many crashes in a row, not restarting.");
            }
            else
            {
                string? exe = Environment.ProcessPath;
                if (exe != null)
                    Process.Start(new ProcessStartInfo(exe, $"--crash={_crashCount + 1}") { UseShellExecute = true });
            }
        }
        catch { }
        Environment.Exit(1);
    }
}