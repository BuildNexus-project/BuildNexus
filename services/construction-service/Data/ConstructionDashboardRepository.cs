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

        // Led by construction_phases: a row there is the record that a Project
        // Manager pressed start, so an INNER JOIN is the whole "under way" gate,
        // and the WHERE removes only the finished-and-delivered. The milestones are
        // a LEFT JOIN so a started build that somehow has none is still a build
        // under way, reported at zero rather than vanishing. The projects are the
        // ones the caller was told are theirs; nothing outside them is read.
        command.CommandText = $@"
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
              AND p.project_id IN ({projectList})
            GROUP BY p.project_id, p.status
            ORDER BY p.started_at DESC, p.project_id;";

        return await ReadProgressRowsAsync(command, cancellationToken);
    }

    public async Task<OutstandingMilestones> GetOutstandingMilestonesAsync(
        IReadOnlyCollection<Guid> projectIds,
        int limit,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            return new OutstandingMilestones { TotalCount = 0, OverdueCount = 0, Items = [] };
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync();

        int total;
        int overdue;

        await using (var count = connection.CreateCommand())
        {
            // Outstanding only counts on a build that has started: a milestone on a
            // planned build nobody has begun is not yet due, and once a build is
            // Completed every milestone is Completed by definition.
            //
            // Overdue is a due date strictly before the day asked about. A milestone
            // with no date compares as NULL, which SUM ignores — so it is outstanding
            // and never late, which is what having no date means.
            count.CommandText = $@"
                SELECT COUNT(*) AS total, COALESCE(SUM(m.due_date < @today), 0) AS overdue
                {OutstandingFrom(AddProjectIds(count, projectIds))};";
            AddParameter(count, "@today", today.ToDateTime(TimeOnly.MinValue));

            await using var reader = await count.ExecuteReaderAsync(cancellationToken);

            // An aggregate query always returns exactly one row.
            await reader.ReadAsync(cancellationToken);

            total = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("total")));
            overdue = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("overdue")));
        }

        var items = new List<OutstandingMilestone>();

        await using var list = connection.CreateCommand();

        // What is dated comes first, soonest first — so the most overdue is at the top and
        // what is next after that follows — and what has no date after it. Within a date,
        // and among the undated, already under way first (that is what is happening now),
        // then what is next, each in the order the Project Manager planned them
        // (created_at, then id so two created in the same instant keep one order).
        list.CommandText = $@"
            SELECT m.id, m.project_id, m.name, m.status, m.due_date, m.created_at
            {OutstandingFrom(AddProjectIds(list, projectIds))}
            ORDER BY (m.due_date IS NULL), m.due_date, (m.status = 'InProgress') DESC, m.created_at, m.id
            LIMIT @limit;";
        AddParameter(list, "@limit", limit);

        await using var itemReader = await list.ExecuteReaderAsync(cancellationToken);

        while (await itemReader.ReadAsync(cancellationToken))
        {
            var dueDate = itemReader.GetOrdinal("due_date");

            items.Add(new OutstandingMilestone
            {
                // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
                Id = itemReader.GetGuid(itemReader.GetOrdinal("id")),
                ProjectId = itemReader.GetGuid(itemReader.GetOrdinal("project_id")),
                Name = itemReader.GetString(itemReader.GetOrdinal("name")),
                Status = Enum.Parse<MilestoneStatus>(itemReader.GetString(itemReader.GetOrdinal("status"))),
                DueDate = itemReader.IsDBNull(dueDate)
                    ? null
                    : DateOnly.FromDateTime(itemReader.GetDateTime(dueDate)),
                CreatedAtUtc = itemReader.GetDateTime(itemReader.GetOrdinal("created_at"))
            });
        }

        return new OutstandingMilestones { TotalCount = total, OverdueCount = overdue, Items = items };
    }

    /// <summary>
    /// The shared tail of both outstanding-milestone statements: a milestone that is not
    /// finished, on a build that has started, in one of the given projects.
    /// </summary>
    private static string OutstandingFrom(string projectList) => $@"
                FROM construction_milestones m
                INNER JOIN construction_phases p ON p.project_id = m.project_id
                WHERE p.status = 'Started'
                  AND m.status <> 'Completed'
                  AND m.project_id IN ({projectList})";

    /// <summary>
    /// Adds one bound parameter per project id and returns the comma-separated placeholder
    /// list to put inside <c>IN (...)</c>. Only the placeholders are text in the statement; the
    /// ids themselves never are.
    /// </summary>
    private static string AddProjectIds(DbCommand command, IReadOnlyCollection<Guid> projectIds)
    {
        var placeholders = new List<string>(projectIds.Count);
        var index = 0;

        foreach (var projectId in projectIds.Distinct())
        {
            var name = $"@project{index++}";
            placeholders.Add(name);
            AddParameter(command, name, projectId);
        }

        return string.Join(", ", placeholders);
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
                ProgressPercent = ProgressPercentage.Of(completed, total)
            });
        }

        return rows;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
