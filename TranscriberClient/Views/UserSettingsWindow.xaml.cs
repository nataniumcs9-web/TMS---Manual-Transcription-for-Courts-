using System.Windows;
using System.Windows.Controls;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.Views;

public partial class UserSettingsWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly DatabaseService _databaseService = new();

    public UserSettingsWindow(UserAccount currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        UserNameText.Text = $"{currentUser.FullName} · {currentUser.Username}";

        var preferences = AppSettings.UiPreferences;
        AlwaysOnTopCheckBox.IsChecked = preferences.DashboardAlwaysOnTop;
        CompactDashboardCheckBox.IsChecked = preferences.CompactDashboard;
        var interval = Math.Clamp(preferences.AutoRefreshSeconds, 15, 300);
        RefreshIntervalComboBox.SelectedItem = RefreshIntervalComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => int.TryParse(item.Tag?.ToString(), out var seconds) && seconds == interval)
            ?? RefreshIntervalComboBox.Items.OfType<ComboBoxItem>().First();
    }

    private void SaveSystemSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (RefreshIntervalComboBox.SelectedItem is not ComboBoxItem selected
            || !int.TryParse(selected.Tag?.ToString(), out var refreshSeconds))
        {
            ShowResult("Choose a valid dashboard refresh interval.", isError: true);
            return;
        }

        try
        {
            AppSettings.SaveUiPreferences(AppSettings.UiPreferences with
            {
                DashboardAlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true,
                CompactDashboard = CompactDashboardCheckBox.IsChecked == true,
                AutoRefreshSeconds = refreshSeconds
            });
            ShowResult("System settings saved for this Windows user.", isError: false);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save per-user dashboard settings");
            ShowResult($"Settings could not be saved: {ex.Message}", isError: true);
        }
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var currentPassword = CurrentPasswordBox.Password;
        var newPassword = NewPasswordBox.Password;
        if (string.IsNullOrEmpty(currentPassword))
        {
            ShowResult("Enter your current password.", isError: true);
            return;
        }

        if (newPassword.Length < 12)
        {
            ShowResult("The new password must contain at least 12 characters.", isError: true);
            return;
        }

        if (!string.Equals(newPassword, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowResult("The new password and confirmation do not match.", isError: true);
            return;
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            ShowResult("Choose a new password different from the current password.", isError: true);
            return;
        }

        try
        {
            await _databaseService.ChangeTranscriberPasswordAsync(_currentUser, currentPassword, newPassword);
            new AuthService().CacheForOfflineLogin(_currentUser);
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            ShowResult("Your transcriber password was changed successfully.", isError: false);
        }
        catch (InvalidOperationException ex)
        {
            ShowResult(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not change password for transcriber {Username}", _currentUser.Username);
            ShowResult($"Password change failed. Check your database connection and account permissions. {ex.Message}", isError: true);
        }
    }

    private void ShowResult(string message, bool isError)
    {
        ResultText.Text = message;
        ResultText.Foreground = isError
            ? System.Windows.Media.Brushes.DarkRed
            : System.Windows.Media.Brushes.DarkGreen;
    }
}
