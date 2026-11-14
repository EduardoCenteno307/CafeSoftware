using System.Windows;
using System.Windows.Controls;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class WindowsSetupTab : UserControl
{
    private readonly WindowsSetupViewModel _vm = new();

    public WindowsSetupTab()
    {
        InitializeComponent();
        DataContext = _vm;

        // The ViewModel decides WHEN to ask; the tab shows the box
        _vm.ConfirmRequested += message =>
            MessageBox.Show(Window.GetWindow(this), message, "Windows setup",
                            MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }
}