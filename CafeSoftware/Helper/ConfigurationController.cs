using System.Security.Cryptography;
using System.Text;
using CafeSoftware.Enums;

namespace CafeSoftware.Helper;

// What one coin is worth: its pesos, plus its own bonus seconds (0 = no bonus)
public record CoinRule(int Pesos, int BonusSeconds);

public class ConfigurationController
{
    // Used until the owner saves a coin table: pulses : pesos : bonus seconds
    private const string DefaultPulseTable = "1:1:0,2:1:0,3:5:300,4:5:300,5:10:600";

    private readonly ConfigReader _reader;

    // Keeps older code like App.Config.Data.X working
    public ConfigurationController Data => this;

    public ConfigurationController(ConfigReader reader)
    {
        _reader = reader;

        if (string.IsNullOrEmpty(PasswordHash))
        {
            SetPassword("admin123");
            Save();
        }
    }

    private int GetInt(Section s, ConfigKey k, int def) =>
        int.TryParse(_reader.GetValue(s, k), out int v) && v > 0 ? v : def;

    // Same as GetInt but 0 is allowed (for "no bonus" or "off")
    private int GetIntOrZero(Section s, ConfigKey k, int def) =>
        int.TryParse(_reader.GetValue(s, k), out int v) && v >= 0 ? v : def;

    private bool GetBool(Section s, ConfigKey k, bool def) =>
        bool.TryParse(_reader.GetValue(s, k), out bool b) ? b : def;

    // Seconds of computer time for each peso
    public int SecondsPerPeso
    {
        get => GetInt(Section.General, ConfigKey.SecondsPerPeso, 360);
        set => _reader.SetValue(Section.General, ConfigKey.SecondsPerPeso, value.ToString());
    }

    // ---------- coin table: pulses sent by the coin acceptor -> pesos and bonus ----------

    public Dictionary<int, CoinRule> PulseTable
    {
        get
        {
            var table = ParsePulseTable(_reader.GetValue(Section.Serial, ConfigKey.PulseTable));
            return table.Count > 0 ? table : ParsePulseTable(DefaultPulseTable);
        }
    }

    public void SetPulseTable(IEnumerable<KeyValuePair<int, CoinRule>> rows)
    {
        string text = string.Join(",", rows.OrderBy(r => r.Key)
            .Select(r => $"{r.Key}:{r.Value.Pesos}:{r.Value.BonusSeconds}"));
        _reader.SetValue(Section.Serial, ConfigKey.PulseTable, text);
    }

    // The coin that sends this many pulses, or null if the count is not in the table
    public CoinRule? FindCoin(int pulses) =>
        PulseTable.TryGetValue(pulses, out var rule) ? rule : null;

    // Bonus for a coin pressed on a test button: the first coin in the table worth that many pesos
    public int BonusForPesos(int pesos) =>
        PulseTable.OrderBy(p => p.Key).Select(p => p.Value)
                  .FirstOrDefault(r => r.Pesos == pesos)?.BonusSeconds ?? 0;

    // For the price list on the lock screen: one line per coin value, with its time
    public List<(int Pesos, int Seconds)> CoinPrices()
    {
        var rules = PulseTable.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        return rules.GroupBy(r => r.Pesos)
                    .OrderBy(g => g.Key)
                    .Select(g => (Pesos: g.Key, Seconds: g.Key * SecondsPerPeso + g.First().BonusSeconds))
                    .ToList();
    }

    // Reads "3:5:300,4:5:300". An older row without a bonus ("3:5") takes the old fixed bonus
    // for ₱5 or ₱10, so existing settings keep working until they are saved again.
    private Dictionary<int, CoinRule> ParsePulseTable(string? text)
    {
        var table = new Dictionary<int, CoinRule>();
        if (string.IsNullOrWhiteSpace(text)) return table;

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = part.Split(':');
            if (f.Length < 2 || f.Length > 3) continue;
            if (!int.TryParse(f[0], out int pulses) || !int.TryParse(f[1], out int pesos)) continue;
            if (pulses < 1 || pulses > 50 || pesos < 1 || pesos > 100) continue;

            int bonus;
            if (f.Length == 3)
            {
                if (!int.TryParse(f[2], out bonus) || bonus < 0 || bonus > 86400) continue;
            }
            else bonus = LegacyBonus(pesos);

            table[pulses] = new CoinRule(pesos, bonus);
        }
        return table;
    }

    private int LegacyBonus(int pesos) => pesos switch
    {
        5 => GetIntOrZero(Section.General, ConfigKey.Bonus5Seconds, 300),
        10 => GetIntOrZero(Section.General, ConfigKey.Bonus10Seconds, 600),
        _ => 0
    };

    // ---------- other settings ----------

    // Minutes on the lock screen with no coin before the PC shuts down (0 = never)
    public int IdleShutdownMinutes
    {
        get => GetIntOrZero(Section.General, ConfigKey.IdleShutdownMinutes, 0);
        set => _reader.SetValue(Section.General, ConfigKey.IdleShutdownMinutes, value.ToString());
    }

    // What happens when a customer's time runs out
    public EndAction OnSessionEnd
    {
        get => Enum.TryParse<EndAction>(_reader.GetValue(Section.General, ConfigKey.EndAction), true, out var a)
               && Enum.IsDefined(a) ? a : EndAction.Lock;
        set => _reader.SetValue(Section.General, ConfigKey.EndAction, value.ToString());
    }

    public bool DebugMode
    {
        get => GetBool(Section.General, ConfigKey.DebugMode, true);
        set => _reader.SetValue(Section.General, ConfigKey.DebugMode, value.ToString());
    }

    public string ShopName
    {
        get => _reader.GetValue(Section.General, ConfigKey.ShopName) ?? "PISONET COMPUTER";
        set => _reader.SetValue(Section.General, ConfigKey.ShopName, value);
    }

    public int WarnSeconds
    {
        get => GetInt(Section.General, ConfigKey.WarnSeconds, 60);
        set => _reader.SetValue(Section.General, ConfigKey.WarnSeconds, value.ToString());
    }

    public int FinalSeconds
    {
        get => GetInt(Section.General, ConfigKey.FinalSeconds, 10);
        set => _reader.SetValue(Section.General, ConfigKey.FinalSeconds, value.ToString());
    }

    public string ComPort
    {
        get => _reader.GetValue(Section.Serial, ConfigKey.ComPort) ?? "";
        set => _reader.SetValue(Section.Serial, ConfigKey.ComPort, value);
    }

    public int BaudRate
    {
        get => GetInt(Section.Serial, ConfigKey.BaudRate, 115200);
        set => _reader.SetValue(Section.Serial, ConfigKey.BaudRate, value.ToString());
    }

    private string Salt
    {
        get => _reader.GetValue(Section.Security, ConfigKey.Salt) ?? "";
        set => _reader.SetValue(Section.Security, ConfigKey.Salt, value);
    }

    private string PasswordHash
    {
        get => _reader.GetValue(Section.Security, ConfigKey.PasswordHash) ?? "";
        set => _reader.SetValue(Section.Security, ConfigKey.PasswordHash, value);
    }

    public void SetPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        Salt = Convert.ToBase64String(salt);
        PasswordHash = Convert.ToBase64String(Hash(password, salt));
    }

    public bool VerifyPassword(string password)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(Salt);
            byte[] expected = Convert.FromBase64String(PasswordHash);
            return CryptographicOperations.FixedTimeEquals(Hash(password, salt), expected);
        }
        catch { return false; }
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

    public void Save() => _reader.Save();
}