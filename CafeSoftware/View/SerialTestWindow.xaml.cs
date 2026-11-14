using System.Windows;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class SerialTestWindow : Window
{
    private readonly SerialTestViewModel _vm = new();

    public SerialTestWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // Keep the newest line in view
        _vm.LogChanged += () =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        };

        // Stop listening to the serial port and session when the window closes
        Closed += (_, _) => _vm.Dispose();
    }
}