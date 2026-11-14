using System.ComponentModel;
using System.Media;
using System.Windows;
using System.Windows.Input;
using CafeSoftware.Services;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class SessionPanelWindow : Window
{
    private readonly SessionViewModel _vm;
    private AlertWindow? _alert;
    private bool _serialWired;

    public SessionPanelWindow(SessionService session)
    {
        InitializeComponent();

        _vm = new SessionViewModel(session);
        DataContext = _vm;

        // The ViewModel decides WHEN; the view does the windows and sounds
        _vm.AlertRequested += ShowAlert;
        _vm.AlertUpdateRequested += (title, message) => _alert?.Update(title, message);
        _vm.AlertPulseRequested += () => _alert?.Pulse();
        _vm.AlertCloseRequested += CloseAlert;
        _vm.BringToFrontRequested += BringPanelToFront;
        _vm.SoundRequested += PlaySound;

        // The serial service does not exist yet when this window is built, so the coin slot
        // status is hooked up the first time the window is shown
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowCoinSlotStatus(); };
    }

    // Called by App.xaml.cs
    public void ApplyDebug(bool show) => _vm.ShowDebug = show;

    private void ShowCoinSlotStatus()
    {
        if (!_serialWired)
        {
            _serialWired = true;
            App.Serial.StatusChanged += () => _vm.SetConnection(App.Serial.IsConnected);
        }
        _vm.SetConnection(App.Serial.IsConnected);
    }

    private void ShowAlert(AlertInfo info)
    {
        CloseAlert();
        _alert = new AlertWindow(info.Title, info.Message, info.Color);
        _alert.Show();
        if (info.AutoCloseSeconds > 0) _alert.AutoClose(info.AutoCloseSeconds);
    }

    private void CloseAlert()
    {
        _alert?.SafeClose();
        _alert = null;
    }

    private static void PlaySound(SoundKind kind)
    {
        switch (kind)
        {
            case SoundKind.Coin: SystemSounds.Asterisk.Play(); break;
            case SoundKind.Warning: SystemSounds.Exclamation.Play(); break;
            case SoundKind.Beep: SystemSounds.Beep.Play(); break;
        }
    }

    // Restore the panel if it was minimized, without stealing keyboard focus
    private void BringPanelToFront()
    {
        if (!IsVisible) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = false;
        Topmost = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            App.OpenAdmin(this);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (App.AllowExit) return;
        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }
}