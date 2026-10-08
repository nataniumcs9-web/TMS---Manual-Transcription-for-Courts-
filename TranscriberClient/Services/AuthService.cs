using System.IO;
using System.Net.Sockets;
using TranscriberClient.Helpers;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.Services;

public class AuthService
{
    private readonly DatabaseService _databaseService;
    private readonly OfflineCredentialCache _offlineCredentialCache = new();

    public AuthService(DatabaseService? databaseService = null)
    {
        _databaseService = databaseService ?? new DatabaseService();
    }

    public async Task<UserAccount?> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = await _databaseService.GetUserByUsernameAsync(username.Trim());
        if (user == null)
        {
            return null;
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return user;
    }

    public async Task<bool> IsDatabaseEndpointReachableAsync()
    {
        var database = AppSettings.Database;
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        try
        {
            await client.ConnectAsync(database.Server, (int)database.Port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            return false;
        }
    }

    public bool TryOfflineLogin(string username, string password, out UserAccount? user)
    {
        return _offlineCredentialCache.TryAuthenticate(username, password, out user);
    }

    public void CacheForOfflineLogin(UserAccount user)
    {
        try
        {
            _offlineCredentialCache.Save(user);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not save the per-Windows-user offline sign-in cache for {Username}", user.Username);
        }
    }
}
