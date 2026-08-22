namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Successful login response: the signed token, when it expires, and the
/// profile of the user it was issued for.
/// </summary>
public class AuthResponse
{
    public string TokenType { get; set; } = "Bearer";

    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public UserResponse User { get; set; } = new();
}
