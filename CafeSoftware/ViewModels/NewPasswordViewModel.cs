using CafeSoftware.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CafeSoftware.ViewModels;

// Makes the owner replace the default admin password
public partial class NewPasswordViewModel : ObservableObject
{
    [ObservableProperty] private string _errorText = "";

    // Filled by the view: a PasswordBox cannot be bound
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";

    public event Action? PasswordSet;

    [RelayCommand]
    private void Save()
    {
        if (NewPassword.Length < 6)
        {
            ErrorText = "The password must be at least 6 characters.";
            return;
        }
        if (NewPassword == "admin123")
        {
            ErrorText = "Please choose a different password from the default one.";
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            ErrorText = "The two passwords do not match.";
            return;
        }

        App.Config.SetPassword(NewPassword);
        App.Config.Save();
        AppLog.Write("Admin password changed (default password replaced)");
        PasswordSet?.Invoke();
    }
}