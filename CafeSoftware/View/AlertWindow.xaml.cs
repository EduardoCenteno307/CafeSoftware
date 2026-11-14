using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class AlertWindow : Window
{
    private readonly AlertViewModel _vm;
    private bool _closed;

    public AlertWindow(string title, string message, Color color)
    {
        InitializeComponent();

        _vm = new AlertViewModel(title, message, color);
        DataContext = _vm;

        // Top center of the screen
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = 30;

        Closed += (_, _) => _closed = true;
    }

    // Used by the session window. They pass the values to the ViewModel.
    public void Update(string title, string message)
    {
        if (!_closed) _vm.Update(title, message);
    }

    public void Pulse()
    {
        if (!_closed) _vm.Pulse();
    }

    public void AutoClose(int seconds)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SafeClose();
        };
        timer.Start();
    }

    public void SafeClose()
    {
        if (_closed) return;
        _closed = true;
        Close();
    }
}