using System.Text;
using BuildNexus.ProjectService.Authorization;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildNexus.ProjectService.Controllers;

/// <summary>
/// Admin reporting over the projects this service holds. Every action requires
/// a valid bearer token and is Admin-only — the Clients whose projects are
/// counted and the staff delivering them do not see the whole pipeline.
/// </summary>
/// <remarks>
/// Its own controller rather than more actions on <see cref="ProjectsController"/>,
/// the way the other services keep their reports: a report reads across every
/// project, which is the opposite of that controller's per-project access rule.
/// </remarks>
[ApiController]
[Route("api/projects/reports")]
[Authorize(Roles = PlatformRoles.Admin)]
public class ReportsController : ControllerBase
{
    private readonly IProjectReportRepository _reportRepository;

    public ReportsController(IProjectReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    /// <summary>
    /// The project status report (US-18): every project grouped by its current
    /// status, optionally narrowed by status and by the date it was submitted.
    /// Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// Every status in scope is returned as a group even when nothing is in it,
    /// in lifecycle order, so the shape of the report does not change with the
    /// data. Filters are optional and combine: <c>status</c> may be repeated or
    /// comma-separated, and <c>from</c> and <c>to</c> are whole days
    /// (<c>yyyy-MM-dd</c>), both inclusive, on the day the project was
    /// submitted.
    /// </remarks>
    /// <param name="status">Only these statuses, e.g. <c>?status=Pending&amp;status=Designing</c>.</param>
    /// <param name="from">First submitted day to include.</param>
    /// <param name="to">Last submitted day to include.</param>
    /// <response code="200">The report, grouped by status in lifecycle order.</response>
    /// <response code="400">A status is not a project status, a date is not a date, or <c>from</c> is after <c>to</c>.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Admin.</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ProjectStatusReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatusReport(
        [FromQuery] string[]? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var generatedAt = DateTime.UtcNow;
        var built = await BuildReportAsync(status, from, to, cancellationToken);

        return built.Failure ?? Ok(ProjectStatusReportResponse.From(built.Report!, generatedAt));
    }

    /// <summary>
    /// The same report as <see cref="GetStatusReport"/>, as a CSV download
    /// (US-18 AC-2). Allowed roles: Admin.
    /// </summary>
    /// <remarks>
    /// Takes the same filters, so an Admin exports what they are looking at. One
    /// row per project, grouped by status in lifecycle order. The file starts
    /// with a byte-order mark so a spreadsheet reads project names as UTF-8
    /// rather than guessing.
    /// </remarks>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">A status is not a project status, a date is not a date, or <c>from</c> is after <c>to</c>.</response>
    /// <response code="401">The token was missing, expired or otherwise invalid.</response>
    /// <response code="403">The caller is not an Admin.</response>
    [HttpGet("status/export")]
    [Produces("text/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportStatusReport(
        [FromQuery] string[]? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var generatedAt = DateTime.UtcNow;
        var built = await BuildReportAsync(status, from, to, cancellationToken);

        if (built.Failure is not null)
        {
            return built.Failure;
        }

        var csv = ProjectStatusReportCsv.Write(built.Report!);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

        return File(bytes, "text/csv; charset=utf-8", $"project-status-report-{generatedAt:yyyy-MM-dd}.csv");
    }

    /// <summary>
    /// Parses the filter, reads the rows it selects and groups them — or, when
    /// the filter is not one, the 400 to answer with.
    /// </summary>
    private async Task<(ProjectStatusReport? Report, IActionResult? Failure)> BuildReportAsync(
        string[]? status,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!ProjectReportFilterParser.TryParse(status, from, to, out var filter, out var error))
        {
            // Refused rather than quietly widened: an Admin who misspells a status
            // must not be handed the whole pipeline as if it were the slice they
            // asked for.
            return (null, ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["filter"] = [error!]
            })));
        }

        var rows = await _reportRepository.ListAsync(filter!, cancellationToken);

        return (ProjectStatusReportBuilder.Build(rows, filter!), null);
    }
}
