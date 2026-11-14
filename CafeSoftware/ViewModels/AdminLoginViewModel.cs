using CafeSoftware.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CafeSoftware.ViewModels;

// Checks the admin password. Wrong tries lock the login for longer each time (up to 30 seconds).
public partial class AdminLoginViewModel : ObservableObject
{
    private static int _failures;   // shared, so closing and reopening the window does not reset it

    [ObservableProperty] private string _errorText = "";

    // Filled by the view: a PasswordBox cannot be bound
    public string Password { get; set; } = "";

    public event Action? LoginSucceeded;
    public event Action? ClearPasswordRequested;
    public event Action? FocusPasswordRequested;

    // While this runs (the wait after a wrong password) the Login button disables itself
    [RelayCommand]
    private async Task LoginAsync()
    {
        if (App.Config.VerifyPassword(Password))
        {
            _failures = 0;
            AppLog.Write("Admin login OK");
            LoginSucceeded?.Invoke();
            return;
        }

        _failures++;
        AppLog.Write($"Admin login FAILED (attempt {_failures})");
        ClearPasswordRequested?.Invoke();

        int wait = Math.Min(_failures * 2, 30);
        for (int i = wait; i > 0; i--)
        {
            ErrorText = $"Wrong password. Try again in {i}s";
            await Task.Delay(1000);
        }

        ErrorText = "";
        FocusPasswordRequested?.Invoke();
    }
}