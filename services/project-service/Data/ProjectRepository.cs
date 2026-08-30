using System.Data.Common;
using BuildNexus.ProjectService.Models;

namespace BuildNexus.ProjectService.Data;

/// <summary>
/// ADO.NET data access for the <c>projects</c> table. Direct SQL only — no ORM.
/// Every value reaches MySQL as a bound parameter.
/// </summary>
public class ProjectRepository : IProjectRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(Project project)
    {
        const string sql = @"
            INSERT INTO projects
                (id, client_id, name, location, land_size_perches, budget, floors, bedrooms,
                 bathrooms, garage_spaces, other_requirements, status, created_at, updated_at)
            VALUES
                (@id, @clientId, @name, @location, @landSizePerches, @budget, @floors, @bedrooms,
                 @bathrooms, @garageSpaces, @otherRequirements, @status, @createdAt, @updatedAt);";

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", project.Id);
        AddParameter(command, "@clientId", project.ClientId);
        AddParameter(command, "@name", project.Name);
        AddParameter(command, "@location", project.Location);
        AddParameter(command, "@landSizePerches", project.LandSizePerches);
        AddParameter(command, "@budget", project.Budget);
        AddParameter(command, "@floors", project.Floors);
        AddParameter(command, "@bedrooms", project.Bedrooms);
        AddParameter(command, "@bathrooms", project.Bathrooms);
        AddParameter(command, "@garageSpaces", project.GarageSpaces);
        AddParameter(command, "@otherRequirements", project.OtherRequirements);
        // Stored as the enum's name, which is what ck_projects_status checks
        // against — never its underlying number.
        AddParameter(command, "@status", project.Status.ToString());
        AddParameter(command, "@createdAt", project.CreatedAt);
        AddParameter(command, "@updatedAt", project.UpdatedAt);

        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        // Omitted free-text requirements are a real NULL in the column, not an
        // empty string.
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
