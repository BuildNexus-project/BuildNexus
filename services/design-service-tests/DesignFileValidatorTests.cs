using BuildNexus.DesignService.Validation;

namespace BuildNexus.DesignService.Tests;

/// <summary>
/// The first US-09 acceptance bullet: PDF / JPG / PNG only, within a size limit,
/// and a clear refusal for anything else. Pure logic — no database, no host.
/// </summary>
public class DesignFileValidatorTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    [Fact]
    public void Accepts_a_pdf_and_reports_its_type()
    {
        var result = DesignFileValidator.Validate(PdfBytes);

        Assert.True(result.IsValid);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Accepts_a_jpeg_and_reports_its_type()
    {
        var result = DesignFileValidator.Validate(JpegBytes);

        Assert.True(result.IsValid);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public void Accepts_a_png_and_reports_its_type()
    {
        var result = DesignFileValidator.Validate(PngBytes);

        Assert.True(result.IsValid);
        Assert.Equal("image/png", result.ContentType);
    }

    [Fact]
    public void Rejects_a_file_whose_bytes_match_no_allowed_type()
    {
        // Plain text — the shape of a .txt renamed .pdf, or a document saved in
        // the wrong format.
        var result = DesignFileValidator.Validate("This is just text, not a design."u8.ToArray());

        Assert.False(result.IsValid);
        Assert.Equal(DesignFileValidator.UnsupportedFormatMessage, result.Error);
        Assert.Null(result.ContentType);
    }

    [Fact]
    public void Ignores_a_convincing_name_and_looks_at_the_bytes()
    {
        // The client can call the part whatever it likes; only the signature
        // decides. A ZIP's leading bytes ("PK") are not on the list.
        var result = DesignFileValidator.Validate([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00]);

        Assert.False(result.IsValid);
        Assert.Equal(DesignFileValidator.UnsupportedFormatMessage, result.Error);
    }

    [Fact]
    public void Rejects_an_empty_file()
    {
        var result = DesignFileValidator.Validate([]);

        Assert.False(result.IsValid);
        Assert.Equal(DesignFileValidator.EmptyFileMessage, result.Error);
    }

    [Fact]
    public void Rejects_a_file_over_the_ten_megabyte_limit()
    {
        var oversize = new byte[DesignFileValidator.MaxFileSizeBytes + 1];
        // A real PDF signature at the front, so the only thing wrong is the size.
        Array.Copy(PdfBytes, oversize, PdfBytes.Length);

        var result = DesignFileValidator.Validate(oversize);

        Assert.False(result.IsValid);
        Assert.Equal(DesignFileValidator.TooLargeMessage, result.Error);
    }

    [Fact]
    public void Accepts_a_file_exactly_on_the_limit()
    {
        var onLimit = new byte[DesignFileValidator.MaxFileSizeBytes];
        Array.Copy(PngBytes, onLimit, PngBytes.Length);

        var result = DesignFileValidator.Validate(onLimit);

        Assert.True(result.IsValid);
        Assert.Equal("image/png", result.ContentType);
    }

    [Fact]
    public void Rejects_a_file_shorter_than_the_signature_it_might_have_had()
    {
        // Two bytes of what could be a JPEG. Not enough to be sure, so not
        // accepted.
        var result = DesignFileValidator.Validate([0xFF, 0xD8]);

        Assert.False(result.IsValid);
        Assert.Equal(DesignFileValidator.UnsupportedFormatMessage, result.Error);
    }

    [Fact]
    public void The_limit_is_ten_megabytes()
    {
        Assert.Equal(10 * 1024 * 1024, DesignFileValidator.MaxFileSizeBytes);
    }
}
