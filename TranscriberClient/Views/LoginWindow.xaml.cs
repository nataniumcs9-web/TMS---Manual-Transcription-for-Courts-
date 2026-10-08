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
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Username = UsernameTextBox.Text;
        _viewModel.Password = PasswordBox.Password;

        await RunLoginAsync();
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
