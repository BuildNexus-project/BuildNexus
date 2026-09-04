using System.ComponentModel.DataAnnotations;

namespace BuildNexus.DesignService.Contracts;

/// <summary>
/// The multipart form behind <c>POST /api/designs/projects/{projectId}/documents</c>:
/// the name to file the upload under, the file itself, and an optional note on
/// what changed in this revision.
/// </summary>
/// <remarks>
/// The project is named by the route, and the uploader comes from the caller's
/// token — neither is a field here, so an upload cannot claim to be for another
/// project or by another person.
/// <para>
/// The file's <em>type</em> is not validated here: a data annotation can only
/// see the client-supplied content type, which is exactly what must not be
/// trusted. <see cref="Validation.DesignFileValidator"/> checks the bytes
/// themselves once the request is in.
/// </para>
/// </remarks>
public class UploadDesignDocumentRequest
{
    /// <summary>
    /// The document this upload is a version of — "GroundFloorPlan". A name not
    /// seen before in this project starts a new document at version 1; a name
    /// already there adds the next version to it.
    /// </summary>
    [Required(ErrorMessage = "A document name is required.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Document name must be between 1 and 150 characters.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>What changed in this revision. Optional — a first upload often has nothing to say.</summary>
    [StringLength(2000, ErrorMessage = "Revision comment must not exceed 2000 characters.")]
    public string? RevisionComment { get; set; }

    /// <summary>The design file. PDF, JPG or PNG, up to 10 MB — enforced on the bytes, not on this.</summary>
    [Required(ErrorMessage = "A file is required.")]
    public IFormFile File { get; set; } = default!;
}
