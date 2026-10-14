namespace BuildNexus.DesignService.Models;

/// <summary>
/// Where a project's design stands as a whole, for the Client's dashboard
/// (US-21 AC-1) — one word for a project that may have several documents, each
/// at a different point in review.
/// </summary>
/// <remarks>
/// Derived from each document's <em>latest</em> version, not from every version
/// ever uploaded: a revision the Architect has since answered with a newer upload
/// is no longer outstanding. Persisted nowhere — it is computed on every read,
/// so it cannot disagree with the versions behind it.
/// <para>
/// Declared in the order they are tested, most in need of the Client first:
/// something awaiting their review outranks a revision they are waiting on,
/// which outranks everything being signed off.
/// </para>
/// </remarks>
public enum ProjectDesignState
{
    /// <summary>Nothing has been uploaded for the project yet.</summary>
    NoDesign,

    /// <summary>At least one document's latest version is waiting on the Client's review.</summary>
    AwaitingReview,

    /// <summary>
    /// Nothing is waiting on the Client, but at least one document's latest
    /// version is a revision the Architect has yet to answer.
    /// </summary>
    RevisionRequested,

    /// <summary>Every document's latest version has been approved.</summary>
    Approved
}
