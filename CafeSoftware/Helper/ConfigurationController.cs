using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CafeSoftware.Enums;

namespace CafeSoftware.Helper;

// What one coin is worth: its pesos, plus its own bonus seconds (0 = no bonus)
public record CoinRule(int Pesos, int BonusSeconds);

// Loads and saves the owner's settings (settings.json) and checks the admin password.
// Values that are missing or out of range fall back to safe defaults.
public class ConfigurationController
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private AppSettings _s = new();

    public string? LastError { get; private set; }

    // Keeps older code like App.Config.Data.X working
    public ConfigurationController Data => this;

    public ConfigurationController(string filePath)
    {
        _path = filePath;
        Load();

        // First run: create the default password
        if (string.IsNullOrEmpty(_s.PasswordHash))
        {
            SetPassword("admin123");
            Save();
        }
    }

    // ---------- loading and saving ----------

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
            if (loaded != null) _s = loaded;
        }
        catch (Exception ex)
        {
            // The file is unreadable: keep a copy for inspection and start from the defaults
            LastError = ex.Message;
            try { File.Move(_path, _path + ".bad", true); } catch { }
            _s = new AppSettings();
        }
    }

    // Written to a temporary file first, so a power cut cannot leave half a settings file
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_s, JsonOptions));
        File.Move(temp, _path, true);
    }

    // ---------- time and pricing ----------

    // Seconds of computer time for each peso
    public int SecondsPerPeso
    {
        get => _s.SecondsPerPeso > 0 ? _s.SecondsPerPeso : 360;
        set => _s.SecondsPerPeso = value;
    }

    public int WarnSeconds
    {
        get => _s.WarnSeconds > 0 ? _s.WarnSeconds : 60;
        set => _s.WarnSeconds = value;
    }

    public int FinalSeconds
    {
        get => _s.FinalSeconds > 0 ? _s.FinalSeconds : 10;
        set => _s.FinalSeconds = value;
    }

    // ---------- coin table: pulses sent by the coin acceptor -> pesos and bonus ----------

    public Dictionary<int, CoinRule> PulseTable
    {
        get
        {
            var table = new Dictionary<int, CoinRule>();
            foreach (var c in _s.Coins ?? new List<CoinSetting>())
            {
                if (c.Pulses is >= 1 and <= 50 && c.Pesos is >= 1 and <= 100 && c.BonusSeconds is >= 0 and <= 86400)
                    table[c.Pulses] = new CoinRule(c.Pesos, c.BonusSeconds);
            }

            return table.Count > 0
                ? table
                : AppSettings.DefaultCoins().ToDictionary(c => c.Pulses, c => new CoinRule(c.Pesos, c.BonusSeconds));
        }
    }

    public void SetPulseTable(IEnumerable<KeyValuePair<int, CoinRule>> rows)
    {
        _s.Coins = rows.OrderBy(r => r.Key)
            .Select(r => new CoinSetting { Pulses = r.Key, Pesos = r.Value.Pesos, BonusSeconds = r.Value.BonusSeconds })
            .ToList();
    }

    // The coin that sends this many pulses, or null if the count is not in the table
    public CoinRule? FindCoin(int pulses) =>
        PulseTable.TryGetValue(pulses, out var rule) ? rule : null;

    // Bonus for a coin pressed on a test button: the first coin in the table worth that many pesos
    public int BonusForPesos(int pesos) =>
        PulseTable.OrderBy(p => p.Key).Select(p => p.Value)
                  .FirstOrDefault(r => r.Pesos == pesos)?.BonusSeconds ?? 0;

    // For the price list on the lock screen: one entry per coin value, with its time
    public List<(int Pesos, int Seconds)> CoinPrices()
    {
        var rules = PulseTable.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        return rules.GroupBy(r => r.Pesos)
                    .OrderBy(g => g.Key)
                    .Select(g => (Pesos: g.Key, Seconds: g.Key * SecondsPerPeso + g.First().BonusSeconds))
                    .ToList();
    }

    // ---------- other settings ----------

    // Minutes on the lock screen with no coin before the PC shuts down (0 = never)
    public int IdleShutdownMinutes
    {
        get => Math.Max(0, _s.IdleShutdownMinutes);
        set => _s.IdleShutdownMinutes = value;
    }

    // What happens when a customer's time runs out
    public EndAction OnSessionEnd
    {
        get => Enum.IsDefined(_s.OnSessionEnd) ? _s.OnSessionEnd : EndAction.Lock;
        set => _s.OnSessionEnd = value;
    }

    public bool DebugMode
    {
        get => _s.DebugMode;
        set => _s.DebugMode = value;
    }

    public string ShopName
    {
        get => string.IsNullOrWhiteSpace(_s.ShopName) ? "PISONET COMPUTER" : _s.ShopName;
        set => _s.ShopName = value;
    }

    public string ComPort
    {
        get => _s.ComPort ?? "";
        set => _s.ComPort = value;
    }

    public int BaudRate
    {
        get => _s.BaudRate >= 300 ? _s.BaudRate : 115200;
        set => _s.BaudRate = value;
    }

    // ---------- admin password (stored as a salted hash, never as text) ----------

    public void SetPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        _s.PasswordSalt = Convert.ToBase64String(salt);
        _s.PasswordHash = Convert.ToBase64String(Hash(password, salt));
    }

    public bool VerifyPassword(string password)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(_s.PasswordSalt);
            byte[] expected = Convert.FromBase64String(_s.PasswordHash);
            return CryptographicOperations.FixedTimeEquals(Hash(password, salt), expected);
        }
        catch { return false; }
    }

    // True while the published default password still works
    public bool UsingDefaultPassword => VerifyPassword("admin123");

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
}