namespace BuildNexus.DesignService.Validation;

/// <summary>
/// The first US-09 acceptance bullet: an upload is a PDF, JPG or PNG within a
/// size limit, and anything else is refused with a message rather than a stack
/// trace.
/// </summary>
/// <remarks>
/// The type is decided by the file's own leading bytes, not by the
/// <c>Content-Type</c> the client put on the multipart part — that header is
/// caller-supplied and trivially wrong, by accident or on purpose. A file
/// renamed <c>plan.pdf</c> that is really an executable does not get past this.
/// <para>
/// Kept free of <c>IFormFile</c> and anything else from ASP.NET so the rule can
/// be exercised on a plain byte array.
/// </para>
/// </remarks>
public static class DesignFileValidator
{
    /// <summary>The largest upload accepted, in bytes. 10 MB.</summary>
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public const string EmptyFileMessage = "The file is empty.";

    public const string TooLargeMessage = "The file is larger than the 10 MB limit.";

    public const string UnsupportedFormatMessage =
        "Only PDF, JPG and PNG files are accepted.";

    private const string Pdf = "application/pdf";
    private const string Jpeg = "image/jpeg";
    private const string Png = "image/png";

    // PDF: "%PDF-"
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46, 0x2D];

    // JPEG: FF D8 FF (SOI marker, then a marker byte).
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    // PNG: the 8-byte signature every PNG opens with.
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Checks <paramref name="content"/> and returns the type it really is, or
    /// the reason it cannot be accepted.
    /// </summary>
    public static DesignFileValidationResult Validate(byte[] content)
    {
        if (content.Length == 0)
        {
            return DesignFileValidationResult.Invalid(EmptyFileMessage);
        }

        if (content.LongLength > MaxFileSizeBytes)
        {
            return DesignFileValidationResult.Invalid(TooLargeMessage);
        }

        var contentType = DetectContentType(content);

        return contentType is null
            ? DesignFileValidationResult.Invalid(UnsupportedFormatMessage)
            : DesignFileValidationResult.Valid(contentType);
    }

    private static string? DetectContentType(byte[] content)
    {
        if (StartsWith(content, PdfSignature))
        {
            return Pdf;
        }

        if (StartsWith(content, JpegSignature))
        {
            return Jpeg;
        }

        if (StartsWith(content, PngSignature))
        {
            return Png;
        }

        return null;
    }

    private static bool StartsWith(byte[] content, byte[] signature)
    {
        if (content.Length < signature.Length)
        {
            return false;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            if (content[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }
}
