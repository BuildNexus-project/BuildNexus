namespace BuildNexus.DesignService.Validation;

/// <summary>
/// The outcome of checking an upload: either the canonical content type the
/// file's own bytes say it is, or the reason it was refused.
/// </summary>
public sealed class DesignFileValidationResult
{
    private DesignFileValidationResult(bool isValid, string? contentType, string? error)
    {
        IsValid = isValid;
        ContentType = contentType;
        Error = error;
    }

    public bool IsValid { get; }

    /// <summary>
    /// The type the file actually is — <c>application/pdf</c>, <c>image/jpeg</c>
    /// or <c>image/png</c> — derived from its signature, not from whatever the
    /// client labelled the part. Set only when <see cref="IsValid"/>.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>The message to show the uploader. Set only when not valid.</summary>
    public string? Error { get; }

    public static DesignFileValidationResult Valid(string contentType) =>
        new(isValid: true, contentType: contentType, error: null);

    public static DesignFileValidationResult Invalid(string error) =>
        new(isValid: false, contentType: null, error: error);
}
