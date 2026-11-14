using System.Windows;
using CafeSoftware.ViewModels;

namespace CafeSoftware.View;

public partial class NewPasswordWindow : Window
{
    private readonly NewPasswordViewModel _vm = new();

    public NewPasswordWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        Loaded += (_, _) => NewBox.FocusInput();
        _vm.PasswordSet += () => DialogResult = true;
    }
}