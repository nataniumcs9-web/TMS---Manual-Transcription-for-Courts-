using System.Windows;
using TranscriberClient.Services;
using TranscriberClient.ViewModels;

namespace TranscriberClient.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginViewModel();
        DataContext = _viewModel;
        PasswordBox.Password = _viewModel.Password;
        VisiblePasswordTextBox.Text = _viewModel.Password;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Username = UsernameTextBox.Text;
        _viewModel.Password = PasswordBox.Visibility == Visibility.Visible
            ? PasswordBox.Password
            : VisiblePasswordTextBox.Text;

        await RunLoginAsync();
    }

    private void PasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Visibility == Visibility.Visible)
        {
            VisiblePasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            VisiblePasswordTextBox.Visibility = Visibility.Visible;
            VisiblePasswordTextBox.Focus();
            VisiblePasswordTextBox.CaretIndex = VisiblePasswordTextBox.Text.Length;
            PasswordVisibilityButton.Content = "⊙";
            PasswordVisibilityButton.ToolTip = "Hide password";
        }
        else
        {
            PasswordBox.Password = VisiblePasswordTextBox.Text;
            VisiblePasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
            PasswordVisibilityButton.Content = "◉";
            PasswordVisibilityButton.ToolTip = "Show password";
        }
    }

    private async Task RunLoginAsync()
    {
        await _viewModel.LoginCommand.ExecuteAsync(null);

        if (Session.CurrentUser != null)
        {
            var dashboard = new DashboardWindow(Session.CurrentUser);
            dashboard.Show();
            Close();
        }
    }

    private async void DatabaseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new DatabaseSettingsWindow
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            _viewModel.ErrorMessage = "Database settings saved. You can now sign in.";
        }

        await Task.CompletedTask;
    }
}
