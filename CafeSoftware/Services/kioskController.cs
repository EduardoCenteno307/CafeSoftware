using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CafeSoftware.Enums;
using CafeSoftware.Helper;
using CafeSoftware.View;

namespace CafeSoftware.Services;

// Runs the kiosk: which window is showing, the restart / shut down countdown,
// the idle shutdown, and the keyboard and monitor lock. App.xaml.cs only starts and stops it.
public sealed class KioskController
{
    private const int IdleWarningSeconds = 30;

    private readonly ConfigurationController _config;
    private readonly MainWindow _lock;
    private readonly SessionPanelWindow _panel;
    private readonly LockGuard _guard;
    private readonly KeyboardBlocker _keys = new();

    // Restart / shut down countdown after a session ends
    private DispatcherTimer? _endTimer;
    private int _endLeft;

    // Idle shutdown
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _idleSeconds;
    private bool _idleMessageShown;

    // Short message on the lock screen, such as "Coin not recognised"
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public SessionService Session { get; }
    public SerialPortService Serial { get; }
    public bool AdminOpen { get; private set; }

    public KioskController(ConfigurationController config)
    {
        _config = config;

        // Order matters: the session window subscribes to the session first, as before
        Session = new SessionService();
        _lock = new MainWindow();
        _panel = new SessionPanelWindow(Session);
        _guard = new LockGuard(_lock);

        Session.CoinAdded += OnCoinAdded;
        Session.Expired += OnExpired;
        Session.CoinInserted += (pesos, secs, isNew) =>
        {
            if (isNew) AppLog.Write("Session started");
            AppLog.Write($"Coin ₱{pesos} accepted (+{secs}s), remaining {Session.RemainingSeconds}s, total paid ₱{Session.TotalPaid:0.00}");
        };

        Serial = new SerialPortService();
        Serial.PulsesReceived += OnPulses;
        Serial.CoinReceived += InsertCoin;      // older message form and the serial test window
        Serial.StatusChanged += () =>
        {
            _lock.SetConnection(Serial.IsConnected);
            AppLog.Write("Coin slot: " + Serial.Status);
        };

        _idleTimer.Tick += (_, _) => IdleTick();
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); ClearNotice(); };
    }

    public void Start()
    {
        ApplyConfig();
        Serial.Start(_config.ComPort, _config.BaudRate);
        _lock.SetConnection(Serial.IsConnected);

        _keys.Start();
        _lock.Show();
        UpdateLockdown();

        _idleTimer.Start();
    }

    public void Stop()
    {
        _idleTimer.Stop();
        _noticeTimer.Stop();
        CancelEnd();
        _guard.Dispose();
        _keys.Dispose();
        Serial.Dispose();
    }

    // Test buttons and the serial test box: the bonus comes from the first matching coin in the table
    public void InsertCoin(int pesos) => Session.AddCoin(pesos, _config.BonusForPesos(pesos));

    // The coin acceptor sent a coin as a number of pulses. The coin table in Settings says what it is worth.
    private void OnPulses(int pulses)
    {
        var coin = _config.FindCoin(pulses);
        if (coin == null)
        {
            AppLog.Write($"Coin not recognised: {pulses} pulses (not in the coin table)");
            ShowNotice($"Coin not recognised ({pulses} pulses). Please try another coin.");
            return;
        }

        AppLog.Write($"Coin received: {pulses} pulses = ₱{coin.Pesos}" +
                     (coin.BonusSeconds > 0 ? $" (+{coin.BonusSeconds}s bonus)" : ""));
        Session.AddCoin(coin.Pesos, coin.BonusSeconds);
    }

    private void ShowNotice(string text)
    {
        if (!_lock.IsVisible || _endTimer != null) return;   // only on the lock screen
        _lock.SetMessage(text);
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void ClearNotice()
    {
        if (_lock.IsVisible && _endTimer == null && !Session.Active && !_idleMessageShown)
            _lock.SetMessage("No active session");
    }

    // ---------- coins and sessions ----------

    private void OnCoinAdded()
    {
        CancelEnd();
        ResetIdle();
        if (AdminOpen) return;   // windows are switched when the admin screens close
        SyncWindows();
    }

    private void OnExpired()
    {
        AppLog.Write("Session ended (time is up)");
        ResetIdle();
        if (AdminOpen) return;

        SyncWindows();
        if (_config.OnSessionEnd == EndAction.Lock)
            _lock.SetMessage("Time is up. Insert a coin to start again.");
        else
            StartEndCountdown(_config.OnSessionEnd);
    }

    // Shows the right window for the current state: session panel while time remains, lock screen otherwise
    private void SyncWindows()
    {
        if (App.AllowExit) return;   // the app is closing: do not touch the windows

        if (Session.Active)
        {
            if (_lock.IsVisible)
            {
                _lock.SetMessage("No active session");
                _lock.Hide();
            }
            if (!_panel.IsVisible) _panel.Show();
        }
        else if (!_lock.IsVisible)
        {
            _panel.Hide();
            _lock.Show();
            _lock.Activate();
        }
        UpdateLockdown();
    }

    // ---------- restart / shut down when time ends ----------

    private void StartEndCountdown(EndAction action)
    {
        CancelEnd();
        _endLeft = 5;
        string verb = action == EndAction.Restart ? "Restarting" : "Shutting down";
        _lock.SetMessage($"Time is up! {verb} in {_endLeft}s...");

        _endTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _endTimer.Tick += (_, _) =>
        {
            _endLeft--;
            if (_endLeft > 0)
            {
                _lock.SetMessage($"Time is up! {verb} in {_endLeft}s...");
                return;
            }
            CancelEnd();
            RunEndAction(action);
        };
        _endTimer.Start();
    }

    private void CancelEnd()
    {
        _endTimer?.Stop();
        _endTimer = null;
    }

    private void RunEndAction(EndAction action)
    {
        string word = action == EndAction.Restart ? "restart" : "shut down";

        if (_config.DebugMode)
        {
            AppLog.Write($"Session ended: PC would {word} (skipped: test mode is on)");
            _lock.SetMessage($"Test mode: the PC would {word} now.");
            return;
        }

        AppLog.Write($"Session ended: PC will {word}");
        RunShutdown(action == EndAction.Restart ? "/r /f /t 0" : "/s /f /t 0", word);
    }

    private void RunShutdown(string args, string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"{what} failed: " + ex.Message);
            _lock.SetMessage($"{what} failed: " + ex.Message);
        }
    }

    // ---------- idle shutdown ----------

    // Runs every second. Counts only while the lock screen is showing with no coins.
    private void IdleTick()
    {
        int limit = _config.IdleShutdownMinutes * 60;

        bool counting = limit > 0 && !AdminOpen && !Session.Active
                        && _lock.IsVisible && _endTimer == null;

        if (!counting)
        {
            ResetIdle();
            return;
        }

        _idleSeconds++;
        int left = limit - _idleSeconds;

        if (left > 0)
        {
            if (left <= IdleWarningSeconds)
            {
                _idleMessageShown = true;
                _lock.SetMessage($"No coins inserted. Shutting down in {left}s — insert a coin to cancel.");
            }
            return;
        }

        // Time is up
        _idleSeconds = 0;
        if (_config.DebugMode)
        {
            AppLog.Write("Idle shutdown reached (skipped: test mode is on)");
            _lock.SetMessage("Test mode: the PC would shut down now.");
            _idleMessageShown = true;
            return;
        }

        AppLog.Write($"Idle shutdown: no coins for {_config.IdleShutdownMinutes} minute(s)");
        RunShutdown("/s /f /t 0", "Shutdown");
    }

    private void ResetIdle()
    {
        _idleSeconds = 0;
        if (_idleMessageShown)
        {
            _idleMessageShown = false;
            if (_lock.IsVisible && _endTimer == null && !Session.Active)
                _lock.SetMessage("No active session");
        }
    }

    // ---------- lock and settings ----------

    // Keyboard blocking, other-monitor covers and keep-on-top:
    // only on the lock screen, and only outside test mode
    private void UpdateLockdown()
    {
        bool on = !_config.DebugMode && _lock.IsVisible;
        _keys.Enabled = on;
        _guard.Enabled = on;
    }

    private void ApplyConfig()
    {
        Session.SecondsPerPeso = _config.SecondsPerPeso;
        Session.WarnSeconds = _config.WarnSeconds;
        Session.FinalSeconds = _config.FinalSeconds;
        _lock.ApplyConfig();
        _lock.ApplyDebug(_config.DebugMode);
        _panel.ApplyDebug(_config.DebugMode);
        UpdateLockdown();
        ResetIdle();
    }

    // Ctrl+Shift+A from the lock screen or session window
    public void OpenAdmin(Window owner)
    {
        if (AdminOpen) return;
        AdminOpen = true;
        ResetIdle();
        CancelEnd();   // do not restart or shut down while the owner is in the admin screens
        try
        {
            var login = new AdminLoginWindow { Owner = owner };
            if (login.ShowDialog() == true)
            {
                OfferNewPassword(owner);

                AppLog.Write("Admin settings opened");
                new SettingsWindow { Owner = owner }.ShowDialog();
                AppLog.Write("Admin settings closed");
            }
        }
        finally
        {
            AdminOpen = false;
        }

        ApplyConfig();
        SyncWindows();   // catches coins or expiry that happened while admin was open
    }

    // While the default password is still in use, offer the owner a chance to change it.
    // "Not now" is allowed: Settings opens either way.
    private void OfferNewPassword(Window owner)
    {
        if (!_config.VerifyPassword("admin123")) return;

        bool changed = new NewPasswordWindow { Owner = owner }.ShowDialog() == true;
        if (!changed) AppLog.Write("Default admin password kept (owner chose Not now)");
    }
}