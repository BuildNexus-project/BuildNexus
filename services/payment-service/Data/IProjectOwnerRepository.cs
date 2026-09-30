namespace BuildNexus.PaymentService.Data;

/// <summary>Data access for <c>project_owners</c>.</summary>
/// <remarks>
/// The replica of one fact the Project Service owns — which Client submitted a
/// project — kept locally so AC-1's Client-facing quotation read can be scoped
/// to the caller's own projects without querying another service's database or
/// holding a foreign key into it.
/// </remarks>
public interface IProjectOwnerRepository
{
    /// <summary>
    /// Records that a project belongs to a Client, unless it is already
    /// recorded.
    /// </summary>
    /// <remarks>
    /// Idempotent, deliberately: Kafka delivers at least once, so a redelivered
    /// <c>ProjectCreated</c> must be absorbed rather than becoming an error or a
    /// second row. The <c>project_id</c> primary key does the absorbing.
    /// Returns <c>true</c> only when a row was actually written.
    /// </remarks>
    Task<bool> RecordOwnerIfAbsentAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this project is recorded as belonging to this Client.
    /// </summary>
    /// <remarks>
    /// <c>false</c> covers both "belongs to someone else" and "this service has
    /// not learned who owns it yet" — a project whose <c>ProjectCreated</c> was
    /// published before this service started consuming <c>project-events</c>.
    /// Both answers deny the read, which is the safe direction: a caller who is
    /// refused sees a 403 and nothing of the project, whereas defaulting an
    /// unknown project to "allowed" would hand every Client every project whose
    /// ownership happens to be missing.
    /// </remarks>
    Task<bool> IsOwnedByAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken cancellationToken = default);
}
