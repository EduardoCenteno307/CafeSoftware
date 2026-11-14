using System.IO;
using CafeSoftware.Enums;

namespace CafeSoftware.Helper;

public class ConfigReader
{
    private readonly Dictionary<string, Dictionary<string, string>> _data =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _filePath;
    public string? LastError { get; private set; }

    public ConfigReader(string filePath)
    {
        _filePath = filePath;
        try
        {
            if (File.Exists(filePath)) Load(filePath);
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    private void Load(string path)
    {
        string? currentSection = null;

        foreach (string line in File.ReadAllLines(path))
        {
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith(';') || t.StartsWith('#')) continue;

            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                currentSection = t.Substring(1, t.Length - 2).Trim();
                if (!_data.ContainsKey(currentSection))
                    _data[currentSection] = new(StringComparer.OrdinalIgnoreCase);
            }
            else if (currentSection != null)
            {
                int i = t.IndexOf('=');
                if (i > 0)
                    _data[currentSection][t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
            }
        }
    }

    public string? GetValue(Section section, ConfigKey key)
    {
        if (_data.TryGetValue(section.ToString(), out var s) && s.TryGetValue(key.ToString(), out var v))
            return v;
        return null;
    }

    public void SetValue(Section section, ConfigKey key, string value)
    {
        string name = section.ToString();
        if (!_data.ContainsKey(name))
            _data[name] = new(StringComparer.OrdinalIgnoreCase);
        _data[name][key.ToString()] = value;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        string tmp = _filePath + ".tmp";

        using (var w = new StreamWriter(tmp))
        {
            foreach (var section in _data)
            {
                w.WriteLine("[" + section.Key + "]");
                foreach (var kvp in section.Value)
                    w.WriteLine(kvp.Key + " = " + kvp.Value);
                w.WriteLine();
            }
        }
        File.Move(tmp, _filePath, true);
    }
}