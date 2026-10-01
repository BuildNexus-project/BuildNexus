using System.Data.Common;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// ADO.NET aggregate queries for the construction reports. Direct SQL only — no ORM.
/// </summary>
public class ConstructionReportRepository : IConstructionReportRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ConstructionReportRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ConstructionProgressReportRow>> GetProgressAcrossActiveProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        // One pass, one round trip. milestone_setups leads because it is the
        // design-approval gate — a project without a row there has no build planned, so
        // it is not a candidate at all. The INNER JOIN onto the milestones then drops
        // any approved project nobody has planned milestones for, which is what AC-1's
        // "have milestones defined" asks for.
        //
        // construction_phases is a LEFT JOIN: its absence means the build has not been
        // started, which is a real line in this report. The WHERE keeps those NULL rows
        // and excludes only a phase that has reached HandedOver.
        //
        // SUM over a boolean is MySQL's idiom for a conditional count and returns NULL
        // for an empty group; COALESCE keeps each a plain integer. The three breakdown
        // counts sum to total_milestones by construction, since the status column is
        // constrained to exactly those three values.
        const string sql = @"
            SELECT
                s.project_id                                  AS project_id,
                p.status                                      AS phase_status,
                COUNT(m.id)                                   AS total_milestones,
                COALESCE(SUM(m.status = 'Completed'), 0)      AS completed_milestones,
                COALESCE(SUM(m.status = 'InProgress'), 0)     AS in_progress_milestones,
                COALESCE(SUM(m.status = 'NotStarted'), 0)     AS not_started_milestones
            FROM milestone_setups s
            INNER JOIN construction_milestones m ON m.project_id = s.project_id
            LEFT JOIN construction_phases p ON p.project_id = s.project_id
            WHERE p.status IS NULL OR p.status <> 'HandedOver'
            GROUP BY s.project_id, p.status
            ORDER BY s.project_id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<ConstructionProgressReportRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static ConstructionProgressReportRow Map(DbDataReader reader)
    {
        var total = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("total_milestones")));
        var completed = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("completed_milestones")));

        return new ConstructionProgressReportRow
        {
            ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
            PhaseStatus = ReadPhaseStatus(reader, "phase_status"),
            TotalMilestones = total,
            CompletedMilestones = completed,
            InProgressMilestones = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("in_progress_milestones"))),
            NotStartedMilestones = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("not_started_milestones"))),
            ProgressPercent = ProgressPercentage.Of(completed, total)
        };
    }

    /// <summary>
    /// The phase status, or <c>null</c> when the project has no phase row — the build has
    /// not been started.
    /// </summary>
    private static ConstructionPhaseStatus? ReadPhaseStatus(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : Enum.Parse<ConstructionPhaseStatus>(reader.GetString(ordinal));
    }
}
