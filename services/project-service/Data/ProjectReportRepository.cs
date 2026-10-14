using System.Data.Common;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// ADO.NET reads for the Admin project status report. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter, including the status list.
/// </summary>
public class ProjectReportRepository : IProjectReportRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectReportRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProjectReportRow>> ListAsync(
        ProjectReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        // The WHERE clause is assembled from fixed fragments only; what the
        // caller chose reaches the statement as parameters, never as text.
        var conditions = new List<string>();

        if (filter.Statuses is not null)
        {
            if (filter.Statuses.Count == 0)
            {
                // An empty selection matches nothing. `IN ()` is a syntax error,
                // so say so directly.
                conditions.Add("1 = 0");
            }
            else
            {
                var names = new List<string>();
                var index = 0;

                foreach (var status in filter.Statuses.Distinct())
                {
                    var name = $"@status{index++}";
                    names.Add(name);
                    AddParameter(command, name, status.ToString());
                }

                conditions.Add($"status IN ({string.Join(", ", names)})");
            }
        }

        if (filter.FromInclusiveUtc is { } from)
        {
            conditions.Add("created_at >= @fromUtc");
            AddParameter(command, "@fromUtc", from);
        }

        if (filter.ToExclusiveUtc is { } to)
        {
            conditions.Add("created_at < @toExclusiveUtc");
            AddParameter(command, "@toExclusiveUtc", to);
        }

        var where = conditions.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", conditions)}";

        // Newest first, the same order every other project list uses. `id` breaks
        // the tie between projects created in the same second, so an export run
        // twice over unchanged data comes out identically.
        command.CommandText = $@"
            SELECT id, name, location, status, budget, created_at, updated_at
            FROM projects
            {where}
            ORDER BY created_at DESC, id;";

        var rows = new List<ProjectReportRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ProjectReportRow
            {
                // MySqlConnector surfaces CHAR(36) as a Guid, not a string.
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Location = reader.GetString(reader.GetOrdinal("location")),
                Status = Enum.Parse<ProjectStatus>(reader.GetString(reader.GetOrdinal("status"))),
                Budget = reader.GetDecimal(reader.GetOrdinal("budget")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
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
