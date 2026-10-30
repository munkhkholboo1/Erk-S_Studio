using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The refresh ended on a line of four step reports, a revision id and «өөрийн бүтээсэн
/// хувилбар» - right, and unreadable: the owner asked whether something was wrong (2026-10-30,
/// via Мастер). It now ends on one line saying whether the album is ready, with counts a person
/// checks against the album; the steps stay one click away.
/// </summary>
public sealed class THEREFRESHEndsInOneLineThePersonCanReadTests
{
    private static readonly AlbumRefreshSummaryCounts Counts = new(
        SheetCount: 17, PdfPageCount: 44, CloudRevisionNumber: 61, PendingCount: 0);

    [Fact]
    public void ACompleteRunSaysTheAlbumIsReadyWithItsCounts()
    {
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(97, 0),
                AlbumRefreshReport.ContributionSent(1),
                AlbumRefreshReport.CloudFetched(true, "8d3f978e-4c1b"),
                AlbumRefreshReport.PagesRecomposed(17, 0),
            ],
            AlbumOnScreen.CloudCanonical);

        string line = report.SummaryLineMn(Counts);

        Assert.Equal("✓ Альбум бэлэн — 17 хуудас (44 PDF хуудас), Cloud R61, илгээсэн 1 хэсэг, хүлээгдэж буй 0", line);
    }

    [Fact]
    public void NoIdNorInternalWordingReachesTheLine()
    {
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [AlbumRefreshReport.CloudFetched(true, "8d3f978e-4c1b")],
            AlbumOnScreen.LocalPreview);

        string line = report.SummaryLineMn(Counts with { PendingCount = 2 });

        Assert.DoesNotContain("8d3f978e", line, StringComparison.Ordinal);
        Assert.DoesNotContain("бүтээсэн хувилбар", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIncompleteRunSaysWhatIsMissing()
    {
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(97, 1),
                AlbumRefreshReport.ContributionFailed("2 хэсэг хүлээгдсэн хэвээр."),
                AlbumRefreshReport.CloudFetched(false, ""),
                AlbumRefreshReport.PagesRecomposed(17, 0),
            ],
            AlbumOnScreen.LocalPreview);

        string line = report.SummaryLineMn(Counts with { PendingCount = 2 });

        Assert.StartsWith("⚠ Альбум бүрэн бус", line, StringComparison.Ordinal);
        Assert.Contains("хүлээгдэж буй 2", line, StringComparison.Ordinal);
        Assert.Contains("оруулгыг үүл рүү өгч чадсангүй", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalProjectNamesNoCloudRevision()
    {
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(3, 0),
                AlbumRefreshReport.SkippedForLocalProject(AlbumRefreshStep.SendOwnContribution),
                AlbumRefreshReport.SkippedForLocalProject(AlbumRefreshStep.FetchCloudUpdates),
                AlbumRefreshReport.PagesRecomposed(5, 0),
            ],
            AlbumOnScreen.LocalPreview);

        string line = report.SummaryLineMn(new AlbumRefreshSummaryCounts(5, 6, null, 0));

        Assert.DoesNotContain("Cloud R", line, StringComparison.Ordinal);
        Assert.Contains("5 хуудас (6 PDF хуудас)", line, StringComparison.Ordinal);
    }
}
