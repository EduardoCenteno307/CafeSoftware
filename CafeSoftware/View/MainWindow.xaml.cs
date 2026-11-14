using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class MainWindow : Window
{
    private readonly LockViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    // These four are called by App.xaml.cs. They just pass the value to the ViewModel.
    public void ApplyDebug(bool show) => _vm.ShowDebug = show;
    public void ApplyConfig() => _vm.ApplyConfig();
    public void SetMessage(string text) => _vm.Message = text;
    public void SetConnection(bool connected) => _vm.SetConnection(connected);

    // Keyboard shortcuts stay here: they belong to the window itself
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrlShift = ModifierKeys.Control | ModifierKeys.Shift;

        if (e.Key == Key.A && Keyboard.Modifiers == ctrlShift)
        {
            App.OpenAdmin(this);
        }
        else if (App.Config.DebugMode)
        {
            if (e.Key == Key.Q && Keyboard.Modifiers == ctrlShift)
            {
                App.AllowExit = true;
                Application.Current.Shutdown();
            }
            else if (e.Key == Key.D1) _vm.InsertCoinCommand.Execute("1");
            else if (e.Key == Key.D5) _vm.InsertCoinCommand.Execute("5");
            else if (e.Key == Key.D0) _vm.InsertCoinCommand.Execute("10");
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!App.AllowExit) e.Cancel = true;
    }
}