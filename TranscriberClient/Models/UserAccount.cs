namespace TranscriberClient.Models;

public class UserAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ProfPic { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime? ReqDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOfflineSignIn { get; set; }
}
