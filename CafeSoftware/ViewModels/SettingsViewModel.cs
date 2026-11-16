using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using CafeSoftware.Enums;
using CafeSoftware.Helper;
using CafeSoftware.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CafeSoftware.ViewModels;

// All the settings fields, their validation and the Save logic.
// Text boxes are strings so half-typed input can be checked when Save is pressed.
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    // The order of the tabs in SettingsWindow.xaml
    private enum Tab { Pricing = 0, CoinSlot = 1, Session = 2, General = 3 }

    [ObservableProperty] private int _selectedTab;

    [ObservableProperty] private string _shopName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint1))]
    private string _secondsPerPeso = "";

    [ObservableProperty] private string _warnSeconds = "";
    [ObservableProperty] private string _finalSeconds = "";
    [ObservableProperty] private string _idleMinutes = "";
    [ObservableProperty] private int _endActionIndex;      // order: Lock, Restart, Shutdown
    [ObservableProperty] private string _comPort = "";
    [ObservableProperty] private string _baudRate = "";
    [ObservableProperty] private bool _debugMode;
    [ObservableProperty] private string _newPassword = ""; // filled by the view (PasswordBox cannot be bound)
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _connStatusText = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _lastCoinText = "Insert a coin to see how many pulses it sends.";
    [ObservableProperty] private bool _showDefaultPasswordWarning;

    public ObservableCollection<string> Ports { get; } = new();
    public ObservableCollection<PulseRow> PulseRows { get; } = new();
    public string[] BaudRates { get; } = { "9600", "57600", "115200" };

    private int _lastPulses;

    // The view listens to these (windows and dialogs are the view's job)
    public event Action? CloseRequested;
    public event Action<string>? InfoRequested;
    public event Func<string, bool>? ConfirmRequested;
    public event Action? SerialTestRequested;

    public SettingsViewModel()
    {
        var c = App.Config;
        ShopName = c.ShopName;
        SecondsPerPeso = c.SecondsPerPeso.ToString();
        WarnSeconds = c.WarnSeconds.ToString();
        FinalSeconds = c.FinalSeconds.ToString();
        IdleMinutes = c.IdleShutdownMinutes.ToString();
        EndActionIndex = (int)c.OnSessionEnd;
        ComPort = c.ComPort;
        BaudRate = c.BaudRate.ToString();
        DebugMode = c.DebugMode;
        ShowDefaultPasswordWarning = c.UsingDefaultPassword;

        foreach (var pair in c.PulseTable.OrderBy(p => p.Key))
        {
            AttachRow(new PulseRow
            {
                Pulses = pair.Key.ToString(),
                Pesos = pair.Value.Pesos.ToString(),
                Bonus = pair.Value.BonusSeconds.ToString()
            });
        }
        RefreshSummaries();

        LoadPorts();
        RefreshStatus();

        // While Settings is open, show how many pulses each coin sends
        App.Serial.PulsesReceived += OnPulsesReceived;
    }

    public void Dispose()
    {
        App.Serial.PulsesReceived -= OnPulsesReceived;
    }

    // Shows the message and jumps to the tab that has the mistake
    private void Fail(string message, Tab tab)
    {
        ErrorText = message;
        SelectedTab = (int)tab;
    }

    // ---------- live hint under the rate ----------

    private bool TryRate(out int rate) =>
        int.TryParse(SecondsPerPeso, out rate) && rate >= 1;

    public string Hint1 =>
        TryRate(out int rate) ? $"₱1 = {rate} s = {TimeFormat.Short(rate)}" : "Enter a whole number of seconds";

    // The coin table shows each coin's total time, so it follows the rate and the bonus
    partial void OnSecondsPerPesoChanged(string value) => RefreshSummaries();

    private void RefreshSummaries()
    {
        int? rate = TryRate(out int r) ? r : null;
        foreach (var row in PulseRows) row.Refresh(rate);
    }

    // ---------- coin table ----------

    private void AttachRow(PulseRow row)
    {
        row.PropertyChanged += OnRowChanged;
        PulseRows.Add(row);
        row.Refresh(TryRate(out int r) ? r : null);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is PulseRow row && (e.PropertyName == nameof(PulseRow.Pesos) || e.PropertyName == nameof(PulseRow.Bonus)))
            row.Refresh(TryRate(out int r) ? r : null);
    }

    private void OnPulsesReceived(int pulses)
    {
        _lastPulses = pulses;

        var row = PulseRows.FirstOrDefault(r => int.TryParse(r.Pulses, out int n) && n == pulses);
        if (row != null && int.TryParse(row.Pesos, out int value))
        {
            int.TryParse(row.Bonus, out int bonus);
            LastCoinText = bonus > 0
                ? $"Last coin: {pulses} pulses = ₱{value} (+{bonus} s bonus)"
                : $"Last coin: {pulses} pulses = ₱{value}";
        }
        else
        {
            LastCoinText = $"Last coin: {pulses} pulses (not in the table yet)";
        }
    }

    [RelayCommand]
    private void AddRow() => AttachRow(new PulseRow());

    [RelayCommand]
    private void RemoveRow(PulseRow? row)
    {
        if (row == null) return;
        row.PropertyChanged -= OnRowChanged;
        PulseRows.Remove(row);
    }

    // Adds a row for the last coin that was inserted. The owner only types its peso value.
    [RelayCommand]
    private void AddLastCoin()
    {
        if (_lastPulses <= 0)
        {
            ErrorText = "Insert a coin first.";
            return;
        }
        if (PulseRows.Any(r => int.TryParse(r.Pulses, out int n) && n == _lastPulses))
        {
            ErrorText = $"{_lastPulses} pulses is already in the table.";
            return;
        }

        AttachRow(new PulseRow { Pulses = _lastPulses.ToString() });
        ErrorText = "";
    }

    // ---------- coin slot ----------

    private void LoadPorts()
    {
        string current = ComPort;
        Ports.Clear();
        foreach (var p in SerialPort.GetPortNames().OrderBy(x => x)) Ports.Add(p);
        ComPort = current;
    }

    public void RefreshStatus()
    {
        ConnStatusText = "Status: " + App.Serial.Status;
        IsConnected = App.Serial.IsConnected;
    }

    [RelayCommand]
    private void RefreshPorts() => LoadPorts();

    [RelayCommand]
    private void SerialTest()
    {
        SerialTestRequested?.Invoke();   // the view opens the window and waits for it to close
        RefreshStatus();
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(AppLog.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppLog.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorText = "Could not open the logs folder: " + ex.Message;
        }
    }

    // ---------- save ----------

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(ShopName))
        {
            Fail("Shop name cannot be empty.", Tab.General);
            return;
        }
        if (!int.TryParse(SecondsPerPeso, out int rate) || rate < 1 || rate > 86400)
        {
            Fail("Seconds per peso must be 1 to 86400.", Tab.Pricing);
            return;
        }
        if (!int.TryParse(WarnSeconds, out int warn) || !int.TryParse(FinalSeconds, out int fin) ||
            fin < 1 || warn <= fin || warn > 3600)
        {
            Fail("Alerts: final must be at least 1 and smaller than the first alert (max 3600).", Tab.Pricing);
            return;
        }
        if (!int.TryParse(IdleMinutes, out int idle) || idle < 0 || idle > 1440)
        {
            Fail("Idle shutdown must be a whole number of minutes from 0 to 1440 (0 = never).", Tab.Session);
            return;
        }
        if (!int.TryParse(BaudRate, out int baud) || baud < 300)
        {
            Fail("Enter a valid baud rate, for example 115200.", Tab.CoinSlot);
            return;
        }
        if (NewPassword.Length > 0 && NewPassword.Length < 6)
        {
            Fail("New password must be at least 6 characters.", Tab.General);
            return;
        }

        // The coin table: skip empty rows, check the rest
        var table = new Dictionary<int, CoinRule>();
        foreach (var row in PulseRows)
        {
            if (string.IsNullOrWhiteSpace(row.Pulses) && string.IsNullOrWhiteSpace(row.Pesos)) continue;

            if (!int.TryParse(row.Pulses, out int pulses) || pulses < 1 || pulses > 50 ||
                !int.TryParse(row.Pesos, out int pesos) || pesos < 1 || pesos > 100)
            {
                Fail("Coin table: each row needs pulses (1 to 50) and pesos (1 to 100).", Tab.CoinSlot);
                return;
            }

            int bonus = 0;   // an empty bonus box means no bonus
            if (!string.IsNullOrWhiteSpace(row.Bonus) &&
                (!int.TryParse(row.Bonus, out bonus) || bonus < 0 || bonus > 86400))
            {
                Fail("Coin table: the bonus must be whole seconds from 0 to 86400.", Tab.CoinSlot);
                return;
            }

            if (!table.TryAdd(pulses, new CoinRule(pesos, bonus)))
            {
                Fail($"Coin table: {pulses} pulses appears twice.", Tab.CoinSlot);
                return;
            }
        }
        if (table.Count == 0)
        {
            Fail("Coin table: add at least one coin.", Tab.CoinSlot);
            return;
        }

        var endAction = (EndAction)Math.Clamp(EndActionIndex, 0, 2);
        var c = App.Config;

        c.ShopName = ShopName.Trim();
        c.SecondsPerPeso = rate;
        c.WarnSeconds = warn;
        c.FinalSeconds = fin;
        c.IdleShutdownMinutes = idle;
        c.OnSessionEnd = endAction;
        c.ComPort = ComPort.Trim();
        c.BaudRate = baud;
        c.DebugMode = DebugMode;
        c.SetPulseTable(table);
        if (NewPassword.Length > 0)
        {
            c.SetPassword(NewPassword);
            AppLog.Write("Admin password changed");
        }
        c.Save();
        ShowDefaultPasswordWarning = c.UsingDefaultPassword;

        string tableText = string.Join(" ", table.OrderBy(t => t.Key)
            .Select(t => $"{t.Key}p=₱{t.Value.Pesos}+{t.Value.BonusSeconds}s"));
        AppLog.Write($"Settings saved: {rate}s per peso, idle shutdown={idle}min, " +
                     $"when time ends={endAction}, test mode={DebugMode}, port='{c.ComPort}', coin table: {tableText}");

        App.Serial.Start(c.ComPort, c.BaudRate);
        RefreshStatus();

        NewPassword = "";
        ErrorText = "";
        InfoRequested?.Invoke("Settings saved.");
    }

    // ---------- leaving ----------

    [RelayCommand]
    private void ExitApp()
    {
        if (ConfirmRequested?.Invoke("Close CafeSoftware completely?") != true) return;
        App.AllowExit = true;
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}