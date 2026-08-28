namespace BuildNexus.UserService.Configuration;

/// <summary>
/// How password reset links behave, bound from the <c>PasswordReset</c>
/// configuration section.
/// </summary>
public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Placeholder in <see cref="ResetUrlTemplate"/> the token replaces.</summary>
    public const string TokenPlaceholder = "{token}";

    /// <summary>
    /// How long an emailed link stays usable. US-04 sets this at 30 minutes: long
    /// enough to walk to another device for the email, short enough that a link
    /// left sitting in an inbox stops being a way in.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 30;

    /// <summary>
    /// The frontend URL the emailed link points at, with
    /// <see cref="TokenPlaceholder"/> where the token goes.
    /// </summary>
    /// <remarks>
    /// The page is served by the React app, not by this service, so the address
    /// is configuration rather than something the service can work out — it
    /// differs between a local run, the Docker stack and a deployment.
    /// </remarks>
    public string ResetUrlTemplate { get; set; } = string.Empty;
}
