using System.Windows.Threading;

namespace CafeSoftware.Services;

public class SessionService
{
    // Checks the clock 10 times a second. The time left is worked out from a fixed end time,
    // so a busy PC or a late timer tick can never make a session longer or shorter.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private long _endTick;     // Environment.TickCount64 value when the paid time runs out
    private int _remaining;    // whole seconds left, as shown on screen

    public int SecondsPerPeso { get; set; } = 360;   // kept for compatibility, not used for coins
    public int WarnSeconds { get; set; } = 60;
    public int FinalSeconds { get; set; } = 10;

    public int RemainingSeconds => _remaining;
    public decimal TotalPaid { get; private set; }
    public bool Active => _remaining > 0;

    public event Action? CoinAdded;
    public event Action<int, int, bool>? CoinInserted;   // pesos, seconds added, starts a new session
    public event Action? Tick;
    public event Action? WarningReached;
    public event Action? Expired;

    public SessionService() => _timer.Tick += (_, _) => CatchUp();

    // The ONLY entry point for payments (real coins, test buttons and the serial test box).
    // The time is the pesos at the normal rate, plus this coin's own bonus seconds.
    public void AddCoin(int pesos, int bonusSeconds = 0)
    {
        if (pesos <= 0) return;

        CatchUp();   // if the time already ran out a moment ago, finish that session first

        bool newSession = !Active;
        if (newSession) TotalPaid = 0;
        TotalPaid += pesos;

        int seconds = pesos * App.Config.SecondsPerPeso + Math.Max(0, bonusSeconds);
        long now = Environment.TickCount64;
        _endTick = newSession ? now + seconds * 1000L : _endTick + seconds * 1000L;
        _remaining = SecondsLeft(now);

        _timer.Start();
        CoinAdded?.Invoke();
        Tick?.Invoke();
        CoinInserted?.Invoke(pesos, seconds, newSession);
    }

    // Whole seconds left, rounded up, so "1" stays on screen until the last second is over
    private int SecondsLeft(long now)
    {
        long ms = _endTick - now;
        return ms <= 0 ? 0 : (int)((ms + 999) / 1000);
    }

    // Brings the countdown up to date, one second at a time, so no alert is ever skipped
    private void CatchUp()
    {
        if (_remaining <= 0) return;

        int target = SecondsLeft(Environment.TickCount64);
        while (_remaining > target)
        {
            _remaining--;
            if (_remaining == WarnSeconds) WarningReached?.Invoke();
            Tick?.Invoke();

            if (_remaining == 0)
            {
                _timer.Stop();
                Expired?.Invoke();
                return;
            }
        }
    }
}