namespace BuildNexus.DesignService.Configuration;

/// <summary>
/// Outbound email settings, bound from the <c>Email</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="SmtpHost"/> is what decides how mail is delivered: left blank, the
/// service writes each message to the log instead of sending it. Set it, and
/// real SMTP delivery is used — see the sender registration in
/// <c>Program.cs</c>, and <c>user-service</c>'s identically-shaped options.
/// </remarks>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Address a notification is sent from.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Display name shown beside <see cref="FromAddress"/>.</summary>
    public string FromName { get; set; } = "BuildNexus";

    /// <summary>SMTP server host. Blank means "log the message instead of sending it".</summary>
    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    /// <summary>Whether to upgrade the connection with STARTTLS.</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// SMTP credentials. Blank means the server is used without authenticating,
    /// which is normal for a mail relay on a private network.
    /// </summary>
    /// <remarks>Supply the password out of band — <c>Email__Password</c> — never in a committed file.</remarks>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
