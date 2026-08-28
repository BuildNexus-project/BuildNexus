namespace BuildNexus.UserService.Contracts;

/// <summary>
/// A successful response whose only content is a sentence for the user.
/// </summary>
/// <remarks>
/// Used by the password reset endpoints, where there is nothing to return: the
/// first deliberately says nothing about whether an account exists, and the
/// second has just retired the caller's only credential.
/// </remarks>
public class MessageResponse
{
    public string Message { get; set; } = string.Empty;
}
