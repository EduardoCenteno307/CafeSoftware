using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CafeSoftware.Services;

// Protocol:
//   ESP -> PC : READY                    (sent on boot)
//   ESP -> PC : PULSES,<seq>,<count>     (a coin sent <count> pulses; repeated until acknowledged)
//   ESP -> PC : COIN,<seq>,<pesos>       (older form with the value already decided; still accepted)
//   PC  -> ESP: ACK,<seq>
//   PC  -> ESP: PING   /  ESP -> PC: PONG
//
// What a pulse count is worth is decided in the app (the pulse table in Settings), not here.
//
// Keeping the connection: every 2 seconds the watchdog checks that the COM port still exists
// and that the board is still answering. If not, it closes the port and opens it again.
public class SerialPortService : IDisposable
{
    private const int WatchdogSeconds = 2;
    private const int DeadLinkMs = 10000;   // no data at all from the board for this long = the link is dead

    private SerialPort? _port;
    private string _portName = "";
    private int _baud = 115200;
    private readonly StringBuilder _buffer = new();
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(WatchdogSeconds) };
    private long _lastSeq;
    private long _lastRxTick;               // when the board last sent anything (Environment.TickCount64)

    public bool IsConnected => _port?.IsOpen == true;
    public string Status { get; private set; } = "Not started";

    public event Action<int>? PulsesReceived;    // pulse count of one coin, never fires twice for the same seq
    public event Action<int>? CoinReceived;      // pesos (older message form and the serial test window)
    public event Action? StatusChanged;
    public event Action<string>? LineReceived;   // every message from the ESP (PONG hidden)
    public event Action<string>? LineSent;       // every reply sent to the ESP

    public SerialPortService() => _watchdog.Tick += (_, _) => Watchdog();

    public void Start(string portName, int baud)
    {
        Close(background: false);
        _portName = portName.Trim();
        _baud = baud;
        _watchdog.Start();

        if (_portName.Length == 0)
        {
            SetStatus("No COM port selected");
            return;
        }
        TryOpen();
    }

    // Used by the admin Serial Test window: behaves as if the ESP sent this line
    public void Inject(string line)
    {
        AppLog.Write("Serial test message: " + line.Trim());
        HandleLine(line);
    }

    // ---------- connection ----------

    private bool PortIsListed() =>
        SerialPort.GetPortNames().Any(n => string.Equals(n, _portName, StringComparison.OrdinalIgnoreCase));

    private void TryOpen()
    {
        try
        {
            var port = new SerialPort(_portName, _baud)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = false,   // avoids resetting the ESP when the port opens
                RtsEnable = false,
                NewLine = "\n"
            };
            port.DataReceived += Port_DataReceived;
            port.Open();
            _port = port;
            lock (_buffer) _buffer.Clear();

            // A newly opened port starts counting again. The ESP numbers its coins from 1
            // every time it restarts, so an old number must never make new coins look like repeats.
            _lastSeq = 0;
            Volatile.Write(ref _lastRxTick, Environment.TickCount64);

            SetStatus($"Connected ({_portName})");
        }
        catch
        {
            Close(background: true);
            SetStatus($"Not connected ({_portName})");
        }
    }

    // Runs every 2 seconds
    private void Watchdog()
    {
        if (_portName.Length == 0) return;

        bool listed = PortIsListed();

        // Not open: open it as soon as Windows shows the port again
        if (_port == null || !_port.IsOpen)
        {
            if (_port != null) Close(background: true);   // stale object left over from a removed cable

            if (listed) TryOpen();
            else SetStatus($"Not connected ({_portName} not found)");
            return;
        }

        // The cable was unplugged: Windows takes the port out of the list
        if (!listed)
        {
            Close(background: true);
            SetStatus($"Not connected ({_portName} not found)");
            return;
        }

        // The board answers every PING. If nothing has arrived for a while, the link is dead
        // even though Windows still shows the port.
        if (Environment.TickCount64 - Volatile.Read(ref _lastRxTick) > DeadLinkMs)
        {
            AppLog.Write("No reply from the board for 10 seconds: reconnecting");
            Close(background: true);
            SetStatus($"Not connected ({_portName})");
            return;
        }

        try { _port.Write("PING\n"); }
        catch
        {
            Close(background: true);
            SetStatus($"Not connected ({_portName})");
        }
    }

    // ---------- receiving ----------

    private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var lines = new List<string>();
        try
        {
            string chunk = ((SerialPort)sender).ReadExisting();
            if (chunk.Length > 0) Volatile.Write(ref _lastRxTick, Environment.TickCount64);

            lock (_buffer)
            {
                _buffer.Append(chunk);
                string all = _buffer.ToString();
                int nl;
                while ((nl = all.IndexOf('\n')) >= 0)
                {
                    lines.Add(all.Substring(0, nl));
                    all = all.Substring(nl + 1);
                }
                _buffer.Clear().Append(all);
            }
        }
        catch { return; }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        foreach (var line in lines)
            dispatcher.BeginInvoke(() =>
            {
                LogBoardLine(line);
                HandleLine(line);
            });
    }

    // Writes every line the board sends to the app log (except the PONG replies),
    // so a missed coin can be traced afterwards. A "READY" line means the board restarted.
    private static void LogBoardLine(string raw)
    {
        string line = new string(raw.Where(c => c >= 32 && c < 127).ToArray()).Trim();   // drop boot-time garbage
        if (line.Length == 0 || line.StartsWith("PONG", StringComparison.OrdinalIgnoreCase)) return;
        if (line.Length > 120) line = line.Substring(0, 120);
        AppLog.Write("Board: " + line);
    }

    private void HandleLine(string raw)
    {
        string line = raw.Trim();
        if (line.Length == 0) return;

        string[] p = line.Split(',');
        string cmd = p[0].ToUpperInvariant();

        if (cmd != "PONG") LineReceived?.Invoke(line);

        switch (cmd)
        {
            case "READY":
                _lastSeq = 0;   // controller rebooted, its counter restarts
                SetStatus($"Connected ({_portName}) - controller ready");
                break;

            case "PULSES":
                if (p.Length >= 3 &&
                    long.TryParse(p[1], out long pulseSeq) &&
                    int.TryParse(p[2], out int pulses) &&
                    pulses >= 1 && pulses <= 50)
                {
                    Send($"ACK,{pulseSeq}");              // always acknowledge
                    if (pulseSeq > _lastSeq)               // but only use it once
                    {
                        _lastSeq = pulseSeq;
                        PulsesReceived?.Invoke(pulses);
                    }
                    else AppLog.Write($"Duplicate PULSES seq {pulseSeq} ignored");
                }
                else AppLog.Write("Bad PULSES message ignored: " + line);
                break;

            case "COIN":
                if (p.Length >= 3 &&
                    long.TryParse(p[1], out long coinSeq) &&
                    int.TryParse(p[2], out int pesos) &&
                    pesos > 0 && pesos <= 100)
                {
                    Send($"ACK,{coinSeq}");
                    if (coinSeq > _lastSeq) 
                    {
                        _lastSeq = coinSeq;
                        CoinReceived?.Invoke(pesos);
                    }
                    else AppLog.Write($"Duplicate COIN seq {coinSeq} ignored");
                }
                else AppLog.Write("Bad COIN message ignored: " + line);
                break;
        }
    }

    private void Send(string text)
    {
        bool delivered = false;
        try
        {
            if (_port?.IsOpen == true)
            {
                _port.Write(text + "\n");
                delivered = true;
            }
        }
        catch { }
        LineSent?.Invoke(delivered ? text : text + "   (no COM port - test only)");
    }

    private void SetStatus(string text)
    {
        if (Status == text) return;
        Status = text;
        StatusChanged?.Invoke();
    }

    // Closing a port whose cable was pulled can freeze for a long time, so the watchdog
    // does it on another thread. Settings and shutdown wait for it instead.
    private void Close(bool background)
    {
        var p = _port;
        _port = null;
        if (p == null) return;

        void CloseIt()
        {
            try
            {
                p.DataReceived -= Port_DataReceived;
                if (p.IsOpen) p.Close();
                p.Dispose();
            }
            catch { }
        }

        if (background) Task.Run(CloseIt);
        else CloseIt();
    }

    public void Dispose()
    {
        _watchdog.Stop();
        Close(background: true);
    }
}