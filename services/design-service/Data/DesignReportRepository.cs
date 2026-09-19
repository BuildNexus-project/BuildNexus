using System.Data.Common;
using BuildNexus.DesignService.Models;

namespace BuildNexus.DesignService.Data;

/// <summary>
/// ADO.NET aggregate queries for the admin reports. Direct SQL only — no ORM.
/// </summary>
public class DesignReportRepository : IDesignReportRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DesignReportRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DesignApprovalReportRow>> GetApprovalReportAsync(
        CancellationToken cancellationToken = default)
    {
        // Two passes over one join: the inner query reduces to one row per
        // document — its version count, and (if it has one) the version number
        // and time of its approval — and the outer query rolls those up per
        // project. AVG() skips NULLs, so the two approval averages are over the
        // approved documents alone, and a project with none approved gets NULL
        // for both rather than being dropped.
        const string sql = @"
            WITH document_stats AS (
                SELECT
                    d.project_id AS project_id,
                    d.id         AS document_id,
                    COUNT(v.id)  AS version_count,
                    MAX(CASE WHEN v.status = 'Approved' THEN v.version_number END) AS approved_version_number,
                    MAX(CASE WHEN v.status = 'Approved' THEN v.reviewed_at    END) AS approved_at,
                    MIN(v.uploaded_at) AS first_uploaded_at
                FROM design_documents d
                INNER JOIN design_document_versions v ON v.document_id = d.id
                GROUP BY d.project_id, d.id
            )
            SELECT
                project_id,
                COUNT(*)                              AS document_count,
                SUM(approved_version_number IS NOT NULL) AS approved_document_count,
                SUM(version_count)                    AS total_version_count,
                AVG(approved_version_number)          AS average_versions_to_approval,
                AVG(
                    CASE WHEN approved_at IS NOT NULL
                         THEN TIMESTAMPDIFF(SECOND, first_uploaded_at, approved_at) / 3600.0
                    END
                )                                    AS average_hours_to_approval
            FROM document_stats
            GROUP BY project_id
            ORDER BY project_id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<DesignApprovalReportRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DesignApprovalReportRow
            {
                // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                DocumentCount = Convert.ToInt32(reader["document_count"]),
                ApprovedDocumentCount = Convert.ToInt32(reader["approved_document_count"]),
                TotalVersionCount = Convert.ToInt32(reader["total_version_count"]),
                AverageVersionsToApproval = GetNullableDouble(reader, "average_versions_to_approval"),
                AverageHoursToApproval = GetNullableDouble(reader, "average_hours_to_approval")
            });
        }

        return rows;
    }

    private static double? GetNullableDouble(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        // AVG() comes back as DECIMAL from MySQL; Convert handles that and NULL.
        return reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal));
    }
}
