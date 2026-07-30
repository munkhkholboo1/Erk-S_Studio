using Xunit;

namespace ErkS.Studio.Tests;

public sealed class StudioAlbumRevisionAcknowledgementPolicyTests
{
    [Fact]
    public void CanonicalizedRevisionAcknowledgesOriginalUploadHash()
    {
        var revision = new StudioCloudAlbumRevision
        {
            PdfSha256 = new string('a', 64),
            SourceUploadSha256 = new string('b', 64),
        };

        Assert.Equal(
            new string('b', 64),
            StudioAlbumRevisionAcknowledgementPolicy
                .SourceUploadSha256(revision));
    }

    [Fact]
    public void LegacyRevisionFallsBackToCanonicalPdfHash()
    {
        var revision = new StudioCloudAlbumRevision
        {
            PdfSha256 = new string('c', 64),
        };

        Assert.Equal(
            new string('c', 64),
            StudioAlbumRevisionAcknowledgementPolicy
                .SourceUploadSha256(revision));
    }
}
