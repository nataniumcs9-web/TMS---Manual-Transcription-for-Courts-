using System.Windows;
using MySqlConnector;

namespace TranscriberClient.Views;

public partial class DatabaseSettingsWindow : Window
{
    public DatabaseSettingsWindow()
    {
        InitializeComponent();

        var settings = AppSettings.Database;
        ServerTextBox.Text = settings.Server;
        PortTextBox.Text = settings.Port.ToString();
        DatabaseTextBox.Text = settings.Database;
        UsernameTextBox.Text = settings.Username;
        PasswordBox.Password = settings.Password;
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out var settings, out var error))
        {
            ResultText.Text = error;
            ResultText.Foreground = System.Windows.Media.Brushes.DarkRed;
            return;
        }

        ResultText.Text = "Testing connection...";
        ResultText.Foreground = System.Windows.Media.Brushes.Black;

        try
        {
            await using var connection = new MySqlConnection(AppSettings.BuildConnectionString(settings));
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT id, username, full_name, role, password, status FROM req_acc LIMIT 0",
                connection);
            await using var reader = await command.ExecuteReaderAsync();

            ResultText.Text = "Database connection and transcriber account table are available. This test does not validate a transcriber sign-in; use the separate account in req_acc.";
            ResultText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Database connection test failed for server {Server}, database {Database}, user {Username}",
                settings.Server, settings.Database, settings.Username);
            ResultText.Text = $"Connection or transcriber-table check failed: {ex.Message}";
            ResultText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out var settings, out var error))
        {
            ResultText.Text = error;
            ResultText.Foreground = System.Windows.Media.Brushes.DarkRed;
            return;
        }

        try
        {
            AppSettings.SaveDatabaseSettings(settings);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save database settings");
            ResultText.Text = $"Could not save settings: {ex.Message}";
            ResultText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
    }

    private bool TryReadSettings(out DatabaseSettings settings, out string error)
    {
        settings = new DatabaseSettings(string.Empty, 3306, string.Empty, string.Empty, string.Empty);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(ServerTextBox.Text)
            || string.IsNullOrWhiteSpace(DatabaseTextBox.Text)
            || string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            error = "Server, database name, and username are required.";
            return false;
        }

        if (!uint.TryParse(PortTextBox.Text, out var port) || port == 0)
        {
            error = "Enter a valid TCP port (normally 3306).";
            return false;
        }

        settings = new DatabaseSettings(
            ServerTextBox.Text.Trim(),
            port,
            DatabaseTextBox.Text.Trim(),
            UsernameTextBox.Text.Trim(),
            PasswordBox.Password);
        return true;
    }
}
