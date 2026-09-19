using System.ComponentModel.DataAnnotations;

namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// The body behind <c>POST /api/designs/versions/{versionId}/request-revision</c>:
/// the Client's note on what needs to change before the design is approved.
/// </summary>
public class RequestRevisionRequest
{
    [Required(ErrorMessage = "A comment is required.")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Comment must be between 1 and 2000 characters.")]
    public string Comment { get; set; } = string.Empty;
}
