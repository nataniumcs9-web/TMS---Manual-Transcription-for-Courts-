using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TranscriberClient.Helpers;
using TranscriberClient.Models;

namespace TranscriberClient.Services;

public sealed class OfflineCredentialCache
{
    private static readonly TimeSpan MaximumOfflineAge = TimeSpan.FromDays(30);
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranscriberClient",
        "offline-account.dat");

    public void Save(UserAccount user)
    {
        var cached = new CachedAccount
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Gender = user.Gender,
            Role = user.Role,
            PasswordHash = user.PasswordHash,
            SavedAtUtc = DateTime.UtcNow
        };

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(cached);
        var protectedData = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        var directory = Path.GetDirectoryName(CachePath)
            ?? throw new InvalidOperationException("Could not determine the offline account cache directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(CachePath, protectedData);
    }

    public bool TryAuthenticate(string username, string password, out UserAccount? user)
    {
        user = null;
        if (!File.Exists(CachePath))
        {
            return false;
        }

        try
        {
            var protectedData = File.ReadAllBytes(CachePath);
            var plaintext = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
            var cached = JsonSerializer.Deserialize<CachedAccount>(Encoding.UTF8.GetString(plaintext));
            if (cached == null
                || DateTime.UtcNow - cached.SavedAtUtc > MaximumOfflineAge
                || !string.Equals(cached.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(cached.Role, "Transcriber", StringComparison.OrdinalIgnoreCase)
                || !PasswordHasher.Verify(password, cached.PasswordHash))
            {
                return false;
            }

            user = new UserAccount
            {
                Id = cached.Id,
                Username = cached.Username,
                FullName = cached.FullName,
                Gender = cached.Gender,
                Role = cached.Role,
                PasswordHash = cached.PasswordHash,
                Status = "Active",
                IsOfflineSignIn = true
            };
            return true;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Could not validate the Windows-user-bound offline sign-in cache");
            return false;
        }
    }

    private sealed class CachedAccount
    {
        public int Id { get; init; }
        public string Username { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Gender { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public DateTime SavedAtUtc { get; init; }
    }
}
