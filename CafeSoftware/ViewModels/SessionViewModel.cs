using System.Windows.Media;
using System.Windows.Threading;
using CafeSoftware.Helper;
using CafeSoftware.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CafeSoftware.ViewModels;

public enum SoundKind { Coin, Warning, Beep }

public record AlertInfo(string Title, string Message, Color Color, int AutoCloseSeconds);

// Everything the session window shows, plus the decisions about alerts.
// Opening windows and playing sounds is left to the view, which listens to the events below.
public partial class SessionViewModel : ObservableObject
{
    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush Grey = MakeBrush(0x9F, 0xB3, 0xC8);
    private static readonly Brush Green = MakeBrush(0x4A, 0xDE, 0x80);

    private readonly SessionService _session;
    private readonly DispatcherTimer _msgTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _blinkOn;
    private bool _finalAlertShown;

    [ObservableProperty] private string _timeText = "00:00";
    [ObservableProperty] private Brush _timeBrush = Brushes.White;
    [ObservableProperty] private string _paidText = "Total paid: ₱0.00";
    [ObservableProperty] private string _statusText = "Session Active";
    [ObservableProperty] private Brush _statusBrush = Grey;
    [ObservableProperty] private bool _showDebug = true;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionText = "Coin slot: not connected";

    // The view listens to these
    public event Action<AlertInfo>? AlertRequested;
    public event Action<string, string>? AlertUpdateRequested;
    public event Action? AlertPulseRequested;
    public event Action? AlertCloseRequested;
    public event Action? BringToFrontRequested;
    public event Action<SoundKind>? SoundRequested;

    public SessionViewModel(SessionService session)
    {
        _session = session;
        _session.Tick += Refresh;
        _msgTimer.Tick += (_, _) => { _msgTimer.Stop(); ShowIdleStatus(); };

        _session.WarningReached += OnWarning;
        _session.CoinAdded += OnCoinAdded;
        _session.CoinInserted += OnCoinInserted;
        _session.Expired += OnExpired;
    }

    // Coin slot connection, shown at the bottom of the window
    public void SetConnection(bool connected)
    {
        IsConnected = connected;
        ConnectionText = connected ? "Coin slot: ready" : "Coin slot: not connected";
    }

    // ---------- reacting to the session ----------

    private void OnWarning()
    {
        string left = FormatLeft(_session.WarnSeconds);
        SoundRequested?.Invoke(SoundKind.Warning);
        StatusText = $"⚠ {left} remaining — insert more coins";
        StatusBrush = Brushes.Orange;

        BringToFrontRequested?.Invoke();
        AlertRequested?.Invoke(new AlertInfo(
            $"⏰ {left.ToUpper()} LEFT",
            "Insert coins to add more time",
            Color.FromRgb(0xF5, 0x9E, 0x0B), 8));
    }

    private void OnCoinAdded()
    {
        StatusText = "Session Active";
        StatusBrush = Grey;
        _finalAlertShown = false;
        AlertCloseRequested?.Invoke();
    }

    // Sound + message for each accepted coin, with the bonus shown separately
    private void OnCoinInserted(int pesos, int seconds, bool newSession)
    {
        SoundRequested?.Invoke(SoundKind.Coin);

        int normal = pesos * App.Config.SecondsPerPeso;   // what the coin gives with no bonus
        int bonus = Math.Max(0, seconds - normal);

        if (bonus > 0)
        {
            StatusText = $"₱{pesos} added: +{TimeFormat.Short(normal)}\n🎁 +{TimeFormat.Short(bonus)} bonus!";
            _msgTimer.Interval = TimeSpan.FromSeconds(6);   // a little longer so the bonus is noticed
        }
        else
        {
            StatusText = $"₱{pesos} added: +{TimeFormat.Short(seconds)}";
            _msgTimer.Interval = TimeSpan.FromSeconds(4);
        }

        StatusBrush = Green;
        _msgTimer.Stop();
        _msgTimer.Start();
    }

    private void OnExpired()
    {
        _finalAlertShown = false;
        _msgTimer.Stop();
        AlertCloseRequested?.Invoke();
    }

    private void Refresh()
    {
        int s = _session.RemainingSeconds;
        TimeText = TimeFormat.Clock(s);
        PaidText = $"Total paid: ₱{_session.TotalPaid:0.00}";

        if (s > 0 && s <= _session.FinalSeconds)
        {
            _blinkOn = !_blinkOn;
            TimeBrush = _blinkOn ? Brushes.Red : Brushes.Transparent;
            SoundRequested?.Invoke(SoundKind.Beep);

            if (!_finalAlertShown)
            {
                _finalAlertShown = true;
                BringToFrontRequested?.Invoke();
                AlertRequested?.Invoke(new AlertInfo(
                    "⚠ TIME IS ALMOST UP",
                    $"{s} seconds left — insert coin now!",
                    Color.FromRgb(0xDC, 0x26, 0x26), 0));
            }

            AlertUpdateRequested?.Invoke("⚠ TIME IS ALMOST UP",
                $"{s} second{(s == 1 ? "" : "s")} left — insert coin now!");
            AlertPulseRequested?.Invoke();
        }
        else
        {
            TimeBrush = Brushes.White;
            if (s > _session.FinalSeconds) _finalAlertShown = false;
        }
    }

    private void ShowIdleStatus()
    {
        if (_session.RemainingSeconds > 0 && _session.RemainingSeconds <= _session.WarnSeconds)
        {
            StatusText = "⚠ Time is running low — insert more coins";
            StatusBrush = Brushes.Orange;
        }
        else
        {
            StatusText = "Session Active";
            StatusBrush = Grey;
        }
    }

    // Test coin buttons: CommandParameter is the coin value
    [RelayCommand]
    private void InsertCoin(string? pesos)
    {
        if (int.TryParse(pesos, out int p)) App.InsertCoin(p);
    }

    private static string FormatLeft(int s)
    {
        if (s >= 60 && s % 60 == 0) return s == 60 ? "1 minute" : $"{s / 60} minutes";
        return $"{s} seconds";
    }
}