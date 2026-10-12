using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService = new();

    public LoginViewModel()
    {
        var savedLogin = AppSettings.LoadSavedLogin();
        if (savedLogin != null)
        {
            Username = savedLogin.Username;
            Password = savedLogin.Password;
            RememberLogin = true;
        }
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _rememberLogin;

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
                    var loginInfoSaved = TrySaveLoginInfo();
                    Session.CurrentUser = offlineUser;
                    ErrorMessage = "Signed in offline. Downloaded assignments and audio are available; changes will sync when the database returns.";
                    if (!loginInfoSaved)
                    {
                        ErrorMessage += " Saved login information could not be updated.";
                    }
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
            var savedLoginInfo = TrySaveLoginInfo();
            Session.CurrentUser = user;
            if (!savedLoginInfo)
            {
                ErrorMessage = "Signed in. Saved login information could not be updated.";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Login failed");
            if (_authService.TryOfflineLogin(Username, Password, out var offlineUser))
            {
                var loginInfoSaved = TrySaveLoginInfo();
                Session.CurrentUser = offlineUser;
                ErrorMessage = "Signed in using this Windows account's offline cache. Downloaded assignments are available; changes will sync when the database returns.";
                if (!loginInfoSaved)
                {
                    ErrorMessage += " Saved login information could not be updated.";
                }
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

    private bool TrySaveLoginInfo()
    {
        try
        {
            AppSettings.SaveLogin(Username, Password, RememberLogin);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not update remembered sign-in information");
            return false;
        }
    }
}
