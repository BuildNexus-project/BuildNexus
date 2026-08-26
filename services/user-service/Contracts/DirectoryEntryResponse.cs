namespace BuildNexus.UserService.Contracts;

/// <summary>
/// One entry in the project-staff directory: who someone is and what they do,
/// and nothing else.
/// </summary>
/// <remarks>
/// Narrower than <see cref="UserSummaryResponse"/> on purpose. An Architect or
/// Project Manager needs to find the colleague they are working with; they have
/// no business reading that colleague's email address, contact details or
/// account status, so those are not in the response at all rather than being
/// sent and hidden by the UI.
/// </remarks>
public class DirectoryEntryResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}
