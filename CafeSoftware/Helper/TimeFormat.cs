namespace CafeSoftware.Helper;

// One place for showing times, so every screen reads the same way
public static class TimeFormat
{
    // 45 -> "45 sec", 90 -> "1m 30s", 1800 -> "30 min", 3600 -> "1h", 4200 -> "1h 10m"
    public static string Short(int seconds)
    {
        if (seconds < 60) return $"{seconds} sec";

        int h = seconds / 3600;
        int m = seconds % 3600 / 60;
        int s = seconds % 60;

        if (h == 0) return s == 0 ? $"{m} min" : $"{m}m {s}s";

        string text = $"{h}h";
        if (m > 0) text += $" {m}m";
        if (s > 0) text += $" {s}s";
        return text;
    }

    // Countdown display: 754 -> "12:34", 4200 -> "1:10:00"
    public static string Clock(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60:00}:{seconds % 60:00}";
    }
}