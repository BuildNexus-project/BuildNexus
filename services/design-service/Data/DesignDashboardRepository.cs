using System.Data.Common;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Data;

/// <summary>
/// ADO.NET reads for the role dashboards. Direct SQL only — no ORM. Every value
/// reaches MySQL as a bound parameter, including the project id list.
/// </summary>
public class DesignDashboardRepository : IDesignDashboardRepository
{
    /// <summary>
    /// Restricts a query to each document's latest version. Version numbers are
    /// unique within a document (<c>uq_design_document_versions_number</c>) and
    /// only ever grow, so the highest one is the latest and matches exactly one
    /// row per document.
    /// </summary>
    private const string LatestVersionOnly = @"
              AND v.version_number = (
                  SELECT MAX(latest.version_number)
                  FROM design_document_versions latest
                  WHERE latest.document_id = d.id)";

    private readonly IDbConnectionFactory _connectionFactory;

    public DesignDashboardRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProjectDesignTally>> GetDesignTalliesAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            // `IN ()` is a syntax error, and "none of no projects" needs no query.
            return [];
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        var projectList = AddProjectIds(command, projectIds);

        // One row per project, counted over each document's latest version alone.
        // SUM over a boolean is MySQL's idiom for a conditional count; every group
        // has at least one row (the INNER JOIN drops a document with no versions,
        // which the upload path cannot produce), so none is NULL. The three counts
        // partition the latest versions, so they add up to COUNT(*) by construction.
        command.CommandText = $@"
            SELECT
                d.project_id                                          AS project_id,
                COUNT(*)                                              AS document_count,
                SUM(v.status IN ('Submitted', 'UnderReview'))         AS awaiting_review_count,
                SUM(v.status = 'RevisionRequested')                   AS revision_requested_count,
                SUM(v.status = 'Approved')                            AS approved_count
            FROM design_documents d
            INNER JOIN design_document_versions v ON v.document_id = d.id
            WHERE d.project_id IN ({projectList})
            {LatestVersionOnly}
            GROUP BY d.project_id;";

        var tallies = new List<ProjectDesignTally>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            tallies.Add(new ProjectDesignTally
            {
                // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                DocumentCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("document_count"))),
                AwaitingReviewCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("awaiting_review_count"))),
                RevisionRequestedCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("revision_requested_count"))),
                ApprovedCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("approved_count")))
            });
        }

        return tallies;
    }

    public async Task<IReadOnlyList<PendingRevision>> ListPendingRevisionsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        var projectList = AddProjectIds(command, projectIds);

        // Longest-waiting first: the revision the Client asked for last week is the
        // one an Architect should be worried about, not the one from this morning.
        // reviewed_at is whole seconds, so `d.id` breaks the tie between two
        // revisions requested in the same second and the order is the same on
        // every read.
        command.CommandText = $@"
            SELECT
                d.project_id     AS project_id,
                d.id             AS document_id,
                d.name           AS document_name,
                v.version_number AS version_number,
                v.review_comment AS review_comment,
                v.reviewed_at    AS reviewed_at
            FROM design_documents d
            INNER JOIN design_document_versions v ON v.document_id = d.id
            WHERE d.project_id IN ({projectList})
              AND v.status = 'RevisionRequested'
            {LatestVersionOnly}
            ORDER BY v.reviewed_at, d.id;";

        var revisions = new List<PendingRevision>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var comment = reader.GetOrdinal("review_comment");

            revisions.Add(new PendingRevision
            {
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                DocumentId = reader.GetGuid(reader.GetOrdinal("document_id")),
                DocumentName = reader.GetString(reader.GetOrdinal("document_name")),
                VersionNumber = reader.GetInt32(reader.GetOrdinal("version_number")),
                ReviewComment = reader.IsDBNull(comment) ? null : reader.GetString(comment),
                // reviewed_at is set by every review decision, so a RevisionRequested
                // version always has one.
                RequestedAtUtc = reader.GetDateTime(reader.GetOrdinal("reviewed_at"))
            });
        }

        return revisions;
    }

    /// <summary>
    /// Adds one bound parameter per project id and returns the comma-separated
    /// placeholder list to put inside <c>IN (...)</c>. Only the placeholders are
    /// text in the statement; the ids themselves never are.
    /// </summary>
    private static string AddProjectIds(DbCommand command, IReadOnlyCollection<Guid> projectIds)
    {
        var placeholders = new List<string>(projectIds.Count);
        var index = 0;

        foreach (var projectId in projectIds.Distinct())
        {
            var name = $"@project{index++}";
            placeholders.Add(name);

            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = projectId;
            command.Parameters.Add(parameter);
        }

        return string.Join(", ", placeholders);
    }
}
