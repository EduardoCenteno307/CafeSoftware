using System.IO;

namespace CafeSoftware.Services;

// Daily text log: C:\ProgramData\CafeSoftware\logs\2026-10-15.log
public static class AppLog
{
    private static readonly object Sync = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CafeSoftware", "logs");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            }
        }
        catch { }   // logging must never crash the app
    }

    // Deletes log files older than the given number of days
    public static void CleanOld(int days = 30)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var f in Directory.GetFiles(Folder, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-days))
                    File.Delete(f);
        }
        catch { }
    }
}