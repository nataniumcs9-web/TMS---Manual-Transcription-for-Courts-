using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace TranscriberClient;

public static class AppSettings
{
    private static IConfiguration? _configuration;
    private static DatabaseSettings? _databaseSettings;
    private static UiPreferences? _uiPreferences;
    private static readonly string UserSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranscriberClient",
        "database.json");
    private static readonly string UiPreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranscriberClient",
        "ui-preferences.json");
    private static readonly string LocalAudioFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranscriberClient",
        "Audio");

    public static void Initialize()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        _configuration = builder.Build();
        _databaseSettings = LoadDatabaseSettings();
    }

    public static DatabaseSettings Database => _databaseSettings ??= LoadDatabaseSettings();

    public static string DatabaseConnectionString => BuildConnectionString(Database);

    public static string AudioBaseUrl => _configuration?["Audio:BaseUrl"] ?? "http://192.168.0.14/TMS/Recorder/";

    public static string LocalAudioFolder => LocalAudioFolderPath;

    public static string DocsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GZHC_Files");

    public static UiPreferences UiPreferences => _uiPreferences ??= LoadUiPreferences();

    public static string LogFile => _configuration?["Logging:LogFile"] ?? "logs/transcriber-.log";

    public static void SaveDatabaseSettings(DatabaseSettings settings)
    {
        var directory = Path.GetDirectoryName(UserSettingsPath)
            ?? throw new InvalidOperationException("Could not determine the user settings folder.");
        Directory.CreateDirectory(directory);

        var encryptedPassword = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(settings.Password),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var persistedSettings = new PersistedDatabaseSettings
        {
            Server = settings.Server.Trim(),
            Port = settings.Port,
            Database = settings.Database.Trim(),
            Username = settings.Username.Trim(),
            ProtectedPassword = Convert.ToBase64String(encryptedPassword)
        };

        File.WriteAllText(UserSettingsPath, JsonSerializer.Serialize(persistedSettings));
        _databaseSettings = settings with
        {
            Server = settings.Server.Trim(),
            Database = settings.Database.Trim(),
            Username = settings.Username.Trim()
        };
    }

    public static void SaveUiPreferences(UiPreferences preferences)
    {
        var directory = Path.GetDirectoryName(UiPreferencesPath)
            ?? throw new InvalidOperationException("Could not determine the UI preferences folder.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(UiPreferencesPath, JsonSerializer.Serialize(preferences));
        _uiPreferences = preferences;
    }

    public static string BuildConnectionString(DatabaseSettings settings)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Server,
            Port = settings.Port,
            Database = settings.Database,
            UserID = settings.Username,
            Password = settings.Password,
            ConnectionTimeout = 8,
            ConvertZeroDateTime = true
        };

        return builder.ConnectionString;
    }

    private static DatabaseSettings LoadDatabaseSettings()
    {
        if (File.Exists(UserSettingsPath))
        {
            try
            {
                var persisted = JsonSerializer.Deserialize<PersistedDatabaseSettings>(File.ReadAllText(UserSettingsPath));
                if (persisted != null)
                {
                    var password = string.Empty;
                    if (!string.IsNullOrWhiteSpace(persisted.ProtectedPassword))
                    {
                        var protectedBytes = Convert.FromBase64String(persisted.ProtectedPassword);
                        password = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                            protectedBytes,
                            optionalEntropy: null,
                            DataProtectionScope.CurrentUser));
                    }

                    return new DatabaseSettings(
                        persisted.Server,
                        persisted.Port,
                        persisted.Database,
                        persisted.Username,
                        password);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Could not load saved database settings");
            }
        }

        var configuredConnectionString = _configuration?["Database:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            try
            {
                var builder = new MySqlConnectionStringBuilder(configuredConnectionString);
                return new DatabaseSettings(builder.Server, builder.Port, builder.Database, builder.UserID, builder.Password);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Configured database connection string is invalid");
            }
        }

        return new DatabaseSettings("192.168.1.14", 3306, "tms", "Natnael", string.Empty);
    }

    private static UiPreferences LoadUiPreferences()
    {
        if (!File.Exists(UiPreferencesPath))
        {
            return new UiPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(UiPreferencesPath))
                ?? new UiPreferences();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not load saved window preferences from {PreferencesPath}", UiPreferencesPath);
            return new UiPreferences();
        }
    }

    private sealed class PersistedDatabaseSettings
    {
        public string Server { get; set; } = string.Empty;
        public uint Port { get; set; } = 3306;
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string ProtectedPassword { get; set; } = string.Empty;
    }
}

public sealed record UiPreferences
{
    public bool DashboardAlwaysOnTop { get; init; }
    public bool MiniWindowAlwaysOnTop { get; init; } = true;
    public int AutoRefreshSeconds { get; init; } = 30;
    public bool CompactDashboard { get; init; } = true;
    public bool UseEthiopianCalendar { get; init; }
}

public sealed record DatabaseSettings(string Server, uint Port, string Database, string Username, string Password);
