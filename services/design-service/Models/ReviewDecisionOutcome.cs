namespace BuildNexus.DesignService.Models;

/// <summary>What came of trying to record a review decision (US-11).</summary>
public enum ReviewDecisionOutcome
{
    /// <summary>Recorded. The version's status, reviewer and timestamp are set.</summary>
    Recorded,

    /// <summary>No version has this id.</summary>
    VersionNotFound,

    /// <summary>
    /// This version is not <see cref="DesignDocumentStatus.Submitted"/> — a
    /// decision was already recorded on it, concurrently or otherwise.
    /// </summary>
    AlreadyDecided,

    /// <summary>
    /// Another version of the same document is already
    /// <see cref="DesignDocumentStatus.Approved"/> — every version of a
    /// document is read-only history once one of them is.
    /// </summary>
    DocumentAlreadyApproved
}
