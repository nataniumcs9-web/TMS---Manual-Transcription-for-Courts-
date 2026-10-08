using TranscriberClient.Helpers;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.Services;

public class AuthService
{
    private readonly DatabaseService _databaseService;

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
}
