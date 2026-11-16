using CafeSoftware.Enums;

namespace CafeSoftware.Helper;

// One coin in the coin table: how many pulses it sends and what it gives
public class CoinSetting
{
    public int Pulses { get; set; }
    public int Pesos { get; set; }
    public int BonusSeconds { get; set; }
}

// Everything the owner can change. Saved as JSON in C:\ProgramData\CafeSoftware\settings.json
public class AppSettings
{
    public string ShopName { get; set; } = "PISONET COMPUTER";
    public int SecondsPerPeso { get; set; } = 360;
    public int WarnSeconds { get; set; } = 60;
    public int FinalSeconds { get; set; } = 10;
    public int IdleShutdownMinutes { get; set; } = 0;
    public EndAction OnSessionEnd { get; set; } = EndAction.Lock;
    public bool DebugMode { get; set; } = true;

    public string ComPort { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public List<CoinSetting> Coins { get; set; } = DefaultCoins();

    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    // Used until the owner saves their own coin table
    public static List<CoinSetting> DefaultCoins() => new()
    {
        new CoinSetting { Pulses = 1, Pesos = 1,  BonusSeconds = 0 },
        new CoinSetting { Pulses = 2, Pesos = 1,  BonusSeconds = 0 },
        new CoinSetting { Pulses = 3, Pesos = 5,  BonusSeconds = 300 },
        new CoinSetting { Pulses = 4, Pesos = 5,  BonusSeconds = 300 },
        new CoinSetting { Pulses = 5, Pesos = 10, BonusSeconds = 600 }
    };
}