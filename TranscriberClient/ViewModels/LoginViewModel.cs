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
            if (!await _authService.IsDatabaseEndpointReachableAsync())
            {
                if (_authService.TryOfflineLogin(Username, Password, out var offlineUser))
                {
                    Session.CurrentUser = offlineUser;
                    ErrorMessage = "Signed in offline. Server data is unavailable; only this Windows user's local audio files are available until the connection returns.";
                    return;
                }

                ErrorMessage = "No database connection is available. Connect to the court network and sign in online once to enable offline sign-in on this Windows account.";
                return;
            }

            var user = await _authService.LoginAsync(Username, Password);
            if (user == null)
            {
                ErrorMessage = "Sign-in failed. Use your transcriber account, not the database connection username and password. The account must be Active, have the Transcriber role, and use a BCrypt password hash. Ask your database administrator to verify the account.";
                IsBusy = false;
                return;
            }

            _authService.CacheForOfflineLogin(user);
            Session.CurrentUser = user;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Login failed");
            if (_authService.TryOfflineLogin(Username, Password, out var offlineUser))
            {
                Session.CurrentUser = offlineUser;
                ErrorMessage = "Signed in using this Windows account's offline cache. Server data is unavailable; local audio remains available.";
            }
            else
            {
                ErrorMessage = "Sign-in could not be completed. Check the database connection and account status. Offline sign-in is unavailable or expired.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
