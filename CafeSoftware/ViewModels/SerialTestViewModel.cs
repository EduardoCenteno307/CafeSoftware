using System.Collections.ObjectModel;
using CafeSoftware.Helper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CafeSoftware.ViewModels;

// The fake coin slot: sends messages to the serial code as if the ESP8266 sent them
public partial class SerialTestViewModel : ObservableObject, IDisposable
{
    private static int _seq;          // kept between openings, like a real ESP counter
    private string? _lastCoin;

    public ObservableCollection<string> Log { get; } = new();

    [ObservableProperty] private string _remainingText = "";
    [ObservableProperty] private string _customText = "";

    // The view scrolls the list to the newest line
    public event Action? LogChanged;

    public SerialTestViewModel()
    {
        App.Serial.LineReceived += OnReceived;
        App.Serial.LineSent += OnSent;
        App.Session.Tick += UpdateRemaining;

        UpdateRemaining();
        AddLog("Ready. Click a button to send a fake message.");
    }

    public void Dispose()
    {
        App.Serial.LineReceived -= OnReceived;
        App.Serial.LineSent -= OnSent;
        App.Session.Tick -= UpdateRemaining;
    }

    private void OnReceived(string line) => AddLog("PC received:  " + line);
    private void OnSent(string line) => AddLog("PC replied:    " + line);

    private void UpdateRemaining() =>
        RemainingText = $"Time remaining: {TimeFormat.Clock(App.Session.RemainingSeconds)}     Total paid: ₱{App.Session.TotalPaid:0.00}";

    private void AddLog(string text)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        LogChanged?.Invoke();
    }

    // CommandParameter is the coin value
    [RelayCommand]
    private void SendCoin(string? pesos)
    {
        if (!int.TryParse(pesos, out int value)) return;

        _seq++;
        _lastCoin = $"COIN,{_seq},{value}";
        App.Serial.Inject(_lastCoin);
    }

    // Same message again: the PC should ACK it but must NOT add time twice
    [RelayCommand]
    private void RepeatLast()
    {
        if (_lastCoin == null) { AddLog("Send a coin first."); return; }
        AddLog("(sending the same message again: no extra time expected)");
        App.Serial.Inject(_lastCoin);
    }

    // Simulates the ESP8266 rebooting: its counter starts again from 1
    [RelayCommand]
    private void SendReady()
    {
        _seq = 0;
        _lastCoin = null;
        App.Serial.Inject("READY");
    }

    [RelayCommand]
    private void SendCustom()
    {
        string text = CustomText.Trim();
        if (text.Length == 0) return;

        // Crash test: stops the app on purpose so you can check the automatic restart
        if (text == "CRASHTEST")
        {
            AddLog("Crashing on purpose...");
            new Thread(() => throw new InvalidOperationException("Crash test")).Start();
            return;
        }

        App.Serial.Inject(text);
        CustomText = "";
    }

    [RelayCommand]
    private void ClearLog() => Log.Clear();
}