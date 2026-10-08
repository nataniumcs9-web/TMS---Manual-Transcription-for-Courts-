using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService = new();

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter both username and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var user = await _authService.LoginAsync(Username, Password);
            if (user == null)
            {
                ErrorMessage = "Invalid username, password, or account status.";
                IsBusy = false;
                return;
            }

            Session.CurrentUser = user;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unable to sign in. Please check the database connection.";
            Serilog.Log.Error(ex, "Login failed");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
