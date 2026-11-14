using System.ComponentModel;
using System.Windows;
using CafeSoftware.Services;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class SetupProgressWindow : Window
{
    private readonly SetupProgressViewModel _vm = new();
    private readonly SetupMode? _mode;

    // Read by App.xaml.cs when the window closes: 0 = no problems
    public int ExitCodeValue => _vm.ExitCode;

    public SetupProgressWindow(string modeText)
    {
        InitializeComponent();
        DataContext = _vm;

        _mode = modeText switch
        {
            "activate" => SetupMode.Activate,
            "remove" => SetupMode.Remove,
            "remove-all" => SetupMode.RemoveAndDelete,
            _ => null
        };

        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        if (_mode is null)
        {
            _vm.Fail("Unknown setup request.");
            return;
        }
        await _vm.RunAsync(_mode.Value);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // The window cannot be closed while the steps are running
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_vm.IsDone) e.Cancel = true;
    }
}