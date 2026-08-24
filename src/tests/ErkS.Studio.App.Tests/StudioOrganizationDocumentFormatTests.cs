using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Which of the organisation's scans an album page can draw, and what to call
/// the file once it is fetched.
///
/// The client uploaded their certificates as images, and the album's renderer
/// draws images perfectly well - it branches on the file extension and hands
/// anything that is not a PDF to the image loader. So the question was never
/// "is this a PDF"; it is "can the renderer open it".
///
/// The extension is written from the content type the server reported, not
/// from the original file name, because the renderer decides how to open a
/// file by looking at its extension. A PNG saved as .pdf draws nothing and
/// reports "Файл олдсонгүй" - wrong, and unhelpful about why.
/// </summary>
public sealed class StudioOrganizationDocumentFormatTests
{
    [Theory]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    public void TheExtensionFollowsWhatTheServerSaidTheFileIs(string contentType, string expected)
    {
        Assert.True(StudioOrganizationDocumentFormats.CanDraw(contentType));
        Assert.Equal(expected, StudioOrganizationDocumentFormats.Extension(contentType));
    }

    [Theory]
    [InlineData("APPLICATION/PDF")]
    [InlineData("  image/png  ")]
    public void CasingAndSpacingDoNotDecideWhetherAScanCanBeDrawn(string contentType)
    {
        Assert.True(StudioOrganizationDocumentFormats.CanDraw(contentType));
    }

    [Theory]
    [InlineData("image/tiff")]
    [InlineData("application/msword")]
    [InlineData("")]
    [InlineData(null)]
    public void AFormatTheRendererCannotOpenIsRefusedRatherThanSaved(string? contentType)
    {
        // Saving it would produce a page that says "Файл олдсонгүй" over a file
        // that is present - the album would be wrong and the message would send
        // the user looking in the wrong place.
        Assert.False(StudioOrganizationDocumentFormats.CanDraw(contentType));
        Assert.Equal("", StudioOrganizationDocumentFormats.Extension(contentType));
    }
}
