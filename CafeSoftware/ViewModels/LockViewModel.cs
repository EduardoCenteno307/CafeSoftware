using System.Windows.Threading;
using CafeSoftware.Helper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CafeSoftware.ViewModels;

// Everything the lock screen shows. The window only displays these values.
public partial class LockViewModel : ObservableObject
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    [ObservableProperty] private string _clockText = "";
    [ObservableProperty] private string _shopName = "PISONET COMPUTER";
    [ObservableProperty] private string _rateText = "";
    [ObservableProperty] private string _message = "No active session";
    [ObservableProperty] private string _connectionText = "Coin slot: not connected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _showDebug = true;

    public LockViewModel()
    {
        UpdateClock();
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
    }

    private void UpdateClock() =>
        ClockText = DateTime.Now.ToString("ddd, MMM d   h:mm:ss tt");

    // Reads the saved settings: shop name and the price list shown to customers
    public void ApplyConfig()
    {
        var c = App.Config;
        ShopName = c.ShopName;
        RateText = string.Join("     ",
            c.CoinPrices().Select(p => $"₱{p.Pesos} = {TimeFormat.Short(p.Seconds)}"));
    }

    public void SetConnection(bool connected)
    {
        IsConnected = connected;
        ConnectionText = connected ? "Coin slot: ready" : "Coin slot: not connected";
    }

    // Test coin buttons: CommandParameter is the coin value
    [RelayCommand]
    private void InsertCoin(string? pesos)
    {
        if (int.TryParse(pesos, out int p)) App.InsertCoin(p);
    }
}