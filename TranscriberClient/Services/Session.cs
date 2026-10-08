using TranscriberClient.Models;

namespace TranscriberClient.Services;

public static class Session
{
    public static UserAccount? CurrentUser { get; set; }
}
