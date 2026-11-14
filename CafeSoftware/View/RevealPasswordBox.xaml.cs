using System.Windows;
using System.Windows.Controls;

namespace CafeSoftware.View;

// A password box with an eye button. Its Password can be bound like any other property.
public partial class RevealPasswordBox : UserControl
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.Register(
        nameof(Password), typeof(string), typeof(RevealPasswordBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    private bool _updating;   // stops the two boxes from updating each other in a loop

    public RevealPasswordBox() => InitializeComponent();

    // Puts the cursor in whichever box is showing
    public void FocusInput()
    {
        if (Eye.IsChecked == true)
        {
            Shown.Focus();
            Shown.CaretIndex = Shown.Text.Length;
        }
        else Hidden.Focus();
    }

    // The password was set from outside (for example the ViewModel cleared it)
    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (RevealPasswordBox)d;
        if (box._updating) return;

        box._updating = true;
        string text = e.NewValue as string ?? "";
        if (box.Hidden.Password != text) box.Hidden.Password = text;
        if (box.Shown.Text != text) box.Shown.Text = text;
        box._updating = false;
    }

    private void Hidden_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;

        _updating = true;
        Password = Hidden.Password;
        Shown.Text = Hidden.Password;
        _updating = false;
    }

    private void Shown_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;

        _updating = true;
        Password = Shown.Text;
        Hidden.Password = Shown.Text;
        _updating = false;
    }

    private void Eye_Changed(object sender, RoutedEventArgs e)
    {
        bool show = Eye.IsChecked == true;
        Shown.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Hidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        FocusInput();
    }
}