using System.Data.Common;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// ADO.NET reads for the role dashboards. Direct SQL only — no ORM. Every value
/// reaches MySQL as a bound parameter, including the status list.
/// </summary>
public class ProjectDashboardRepository : IProjectDashboardRepository
{
    /// <summary>
    /// The statuses that still have work left in them. Listed here rather than
    /// written as <c>NOT IN ('Completed', 'Cancelled')</c> so a status added to
    /// the enum later is active by default and has to be excluded on purpose,
    /// instead of quietly vanishing from every dashboard.
    /// </summary>
    private static readonly ProjectStatus[] ActiveStatuses = Enum.GetValues<ProjectStatus>()
        .Where(status => status is not (ProjectStatus.Completed or ProjectStatus.Cancelled))
        .ToArray();

    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectDashboardRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<DashboardProject>> ListActiveForClientAsync(
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        ListActiveAsync("client_id", clientId, cancellationToken);

    public Task<IReadOnlyList<DashboardProject>> ListActiveForArchitectAsync(
        Guid architectId,
        CancellationToken cancellationToken = default) =>
        ListActiveAsync("assigned_architect_id", architectId, cancellationToken);

    public async Task<IReadOnlyList<ProjectStatusCount>> CountByStatusAsync(
        CancellationToken cancellationToken = default)
    {
        // The database does the counting: an Admin's dashboard is a handful of
        // numbers, and reading every project to add them up in memory would grow
        // with the pipeline for no reason.
        const string sql = @"
            SELECT status, COUNT(*) AS project_count
            FROM projects
            GROUP BY status;";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var counts = new List<ProjectStatusCount>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add(new ProjectStatusCount
            {
                Status = Enum.Parse<ProjectStatus>(reader.GetString(reader.GetOrdinal("status"))),
                // COUNT(*) is a BIGINT; the value is a tally of projects, not a key.
                Count = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("project_count")))
            });
        }

        return counts;
    }

    /// <summary>
    /// The active projects where <paramref name="personColumn"/> is
    /// <paramref name="personId"/>. The column name is one of two fixed strings
    /// chosen by the callers above and never a caller-supplied value, so it is
    /// safe to place in the statement; the id itself is a parameter.
    /// </summary>
    private async Task<IReadOnlyList<DashboardProject>> ListActiveAsync(
        string personColumn,
        Guid personId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        var statusNames = new List<string>();

        for (var index = 0; index < ActiveStatuses.Length; index++)
        {
            var name = $"@status{index}";
            statusNames.Add(name);
            AddParameter(command, name, ActiveStatuses[index].ToString());
        }

        AddParameter(command, "@personId", personId);

        // Most recently moved first: a dashboard is asking "what needs me", and
        // the project that changed this morning outranks the one that has sat
        // untouched for a month. `id` breaks the tie between projects that moved
        // in the same second, so the order is the same on every read.
        command.CommandText = $@"
            SELECT id, name, location, status, updated_at
            FROM projects
            WHERE {personColumn} = @personId
              AND status IN ({string.Join(", ", statusNames)})
            ORDER BY updated_at DESC, id;";

        var projects = new List<DashboardProject>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(new DashboardProject
            {
                // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Location = reader.GetString(reader.GetOrdinal("location")),
                Status = Enum.Parse<ProjectStatus>(reader.GetString(reader.GetOrdinal("status"))),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            });
        }

        return projects;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
