using System.Windows;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm = new();

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // The ViewModel decides WHEN; the window shows dialogs and closes itself
        _vm.CloseRequested += () => DialogResult = true;
        _vm.InfoRequested += message => MessageBox.Show(this, message, "Settings");
        _vm.ConfirmRequested += message =>
            MessageBox.Show(this, message, "Exit", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        _vm.SerialTestRequested += () => new SerialTestWindow { Owner = this }.ShowDialog();

        // Empty the password box after a successful save
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.NewPassword) && _vm.NewPassword.Length == 0)
                NewPwBox.Clear();
        };

        // Stop listening to the coin slot when the window closes
        Closed += (_, _) => _vm.Dispose();
    }

    // A PasswordBox cannot be bound, so its text is handed to the ViewModel here
    private void NewPwBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _vm.NewPassword = NewPwBox.Password;
}