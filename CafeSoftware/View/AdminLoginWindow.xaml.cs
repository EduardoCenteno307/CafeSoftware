using System.Windows;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class AdminLoginWindow : Window
{
    private readonly AdminLoginViewModel _vm = new();

    public AdminLoginWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        Loaded += (_, _) => PwBox.Focus();

        _vm.LoginSucceeded += () => DialogResult = true;
        _vm.ClearPasswordRequested += () => PwBox.Clear();
        _vm.FocusPasswordRequested += () => PwBox.Focus();
    }

    // A PasswordBox cannot be bound, so its text is handed to the ViewModel here
    private void PwBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _vm.Password = PwBox.Password;
}