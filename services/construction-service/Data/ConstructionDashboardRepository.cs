using System.Data.Common;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Data;

/// <summary>
/// ADO.NET reads for the role dashboards. Direct SQL only — no ORM. Every value
/// reaches MySQL as a bound parameter.
/// </summary>
public class ConstructionDashboardRepository : IConstructionDashboardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ConstructionDashboardRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ConstructionProgressReportRow>> ListProgressForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        // Led by project_owners so a Client is only ever shown what they own — the
        // ownership fact this service already holds, checked in the query rather
        // than on each row. The INNER JOIN onto the milestones drops a project
        // nobody has planned; construction_phases is a LEFT JOIN because its
        // absence is a real state (planned, not started), which the WHERE keeps and
        // narrows only to exclude a build already handed over.
        //
        // SUM over a boolean is MySQL's idiom for a conditional count and returns
        // NULL for an empty group; COALESCE keeps each a plain integer.
        const string sql = @"
            SELECT
                po.project_id                                 AS project_id,
                p.status                                      AS phase_status,
                COUNT(m.id)                                   AS total_milestones,
                COALESCE(SUM(m.status = 'Completed'), 0)      AS completed_milestones,
                COALESCE(SUM(m.status = 'InProgress'), 0)     AS in_progress_milestones,
                COALESCE(SUM(m.status = 'NotStarted'), 0)     AS not_started_milestones
            FROM project_owners po
            INNER JOIN construction_milestones m ON m.project_id = po.project_id
            LEFT JOIN construction_phases p ON p.project_id = po.project_id
            WHERE po.client_id = @clientId
              AND (p.status IS NULL OR p.status <> 'HandedOver')
            GROUP BY po.project_id, p.status
            ORDER BY po.project_id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@clientId", clientId);

        return await ReadProgressRowsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ConstructionProgressReportRow>> ListActiveBuildsAsync(
        CancellationToken cancellationToken = default)
    {
        // Led by construction_phases: a row there is the record that a Project
        // Manager pressed start, so an INNER JOIN is the whole "under way" gate,
        // and the WHERE removes only the finished-and-delivered. The milestones are
        // a LEFT JOIN so a started build that somehow has none is still a build
        // under way, reported at zero rather than vanishing.
        const string sql = @"
            SELECT
                p.project_id                                  AS project_id,
                p.status                                      AS phase_status,
                COUNT(m.id)                                   AS total_milestones,
                COALESCE(SUM(m.status = 'Completed'), 0)      AS completed_milestones,
                COALESCE(SUM(m.status = 'InProgress'), 0)     AS in_progress_milestones,
                COALESCE(SUM(m.status = 'NotStarted'), 0)     AS not_started_milestones
            FROM construction_phases p
            LEFT JOIN construction_milestones m ON m.project_id = p.project_id
            WHERE p.status <> 'HandedOver'
            GROUP BY p.project_id, p.status
            ORDER BY p.started_at DESC, p.project_id;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await ReadProgressRowsAsync(command, cancellationToken);
    }

    public async Task<OutstandingMilestones> GetOutstandingMilestonesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Outstanding only counts on a build that has started: a milestone on a
        // planned build nobody has begun is not yet due, and once a build is
        // Completed every milestone is Completed by definition.
        const string outstanding = @"
            FROM construction_milestones m
            INNER JOIN construction_phases p ON p.project_id = m.project_id
            WHERE p.status = 'Started'
              AND m.status <> 'Completed'";

        await using var connection = await _connectionFactory.OpenConnectionAsync();

        int total;

        await using (var count = connection.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) {outstanding};";
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        }

        var items = new List<OutstandingMilestone>();

        await using var list = connection.CreateCommand();

        // Already under way first — that is what is happening now — then what is
        // next. Within each, in the order the Project Manager planned them
        // (created_at, then id so two created in the same instant keep one order).
        list.CommandText = $@"
            SELECT m.id, m.project_id, m.name, m.status, m.created_at
            {outstanding}
            ORDER BY (m.status = 'InProgress') DESC, m.created_at, m.id
            LIMIT @limit;";
        AddParameter(list, "@limit", limit);

        await using var reader = await list.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new OutstandingMilestone
            {
                // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Status = Enum.Parse<MilestoneStatus>(reader.GetString(reader.GetOrdinal("status"))),
                CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at"))
            });
        }

        return new OutstandingMilestones { TotalCount = total, Items = items };
    }

    private static async Task<IReadOnlyList<ConstructionProgressReportRow>> ReadProgressRowsAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<ConstructionProgressReportRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var total = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("total_milestones")));
            var completed = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("completed_milestones")));
            var phase = reader.GetOrdinal("phase_status");

            rows.Add(new ConstructionProgressReportRow
            {
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                PhaseStatus = reader.IsDBNull(phase)
                    ? null
                    : Enum.Parse<ConstructionPhaseStatus>(reader.GetString(phase)),
                TotalMilestones = total,
                CompletedMilestones = completed,
                InProgressMilestones = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("in_progress_milestones"))),
                NotStartedMilestones = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("not_started_milestones"))),
                ProgressPercent = CalculatePercent(completed, total)
            });
        }

        return rows;
    }

    /// <summary>
    /// The same calculation <see cref="MilestoneRepository"/> and
    /// <see cref="ConstructionReportRepository"/> use, so a project's percentage on a
    /// dashboard and on its own progress screen cannot disagree. Computed here rather
    /// than in SQL for that reason alone. The zero guard matters for
    /// <see cref="ListActiveBuildsAsync"/>, where a started build may have no milestones.
    /// </summary>
    private static decimal CalculatePercent(int completed, int total) =>
        total == 0 ? 0m : Math.Round((decimal)completed / total * 100m, 2);

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
