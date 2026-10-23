using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A source that delivers only presentation pages owes the album nothing.
///
/// 🔴 MAD-2026-ЕТ-05, 2026-10-22: an Erk-S CAD diagram package of 17 Portfolio views
/// was counted as 17 sheets, so Cloud sync demanded an album component for it, found
/// none (Portfolio entries never enter the album) and stopped every other component -
/// the cover and the drawing list waited behind a source that had nothing to give.
/// The count the album check reads is the count of ALBUM entries.
///
/// ⚠ A record written before the album count existed stays conservative: it is still
/// treated as carrying sheets until its package is reconciled again, because guessing
/// «portfolio» for a real building source would publish an album without its sheets.
/// </summary>
public sealed class APORTFOLIOOnlySourceNEVERHoldsTheAlbumSyncTests
{
    [Fact]
    public void APackageOfONLYPortfolioViewsContributesNOAlbumSheets()
    {
        (ProjectWorkspace project, ProjectDesignSource source) = Project();

        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            Manifest(source, SheetDestinations.Portfolio, SheetDestinations.Portfolio),
            "hash-portfolio");

        ProjectSourceSyncCandidate candidate = Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));
        Assert.Equal(2, candidate.SheetCount);
        Assert.False(candidate.ContributesAlbumSheets);
    }

    [Fact]
    public void AMixedPackageStillOwesTheAlbumItsAlbumSheets()
    {
        (ProjectWorkspace project, ProjectDesignSource source) = Project();

        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            Manifest(source, SheetDestinations.Portfolio, SheetDestinations.Album),
            "hash-mixed");

        Assert.True(Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project)).ContributesAlbumSheets);
    }

    [Fact]
    public void ARecordWrittenBEFORETheAlbumCountStaysConservative()
    {
        (ProjectWorkspace project, ProjectDesignSource source) = Project();
        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            Manifest(source, SheetDestinations.Portfolio),
            "hash-old");
        source.Metadata.Remove("cloud.albumSheetCount");

        Assert.True(Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project)).ContributesAlbumSheets);
    }

    private static (ProjectWorkspace Project, ProjectDesignSource Source) Project()
    {
        var source = new ProjectDesignSource { Id = "source-1", Name = "Erk-S CAD диаграм" };
        var project = new ProjectWorkspace();
        project.Sources.Add(source);
        return (project, source);
    }

    private static SheetPackageManifest Manifest(ProjectDesignSource source, params string[] destinations) => new()
    {
        SchemaVersion = 6,
        PackageId = Guid.NewGuid(),
        ExportedAtUtc = new DateTimeOffset(2026, 9, 28, 5, 50, 0, TimeSpan.Zero),
        Source = new SheetPackageSource
        {
            SourceId = source.Id,
            Application = SheetSourceApplication.ErkSCad,
            DocumentTitle = "ZuunMod-XET-diagram",
        },
        Sheets = destinations
            .Select((destination, index) => new SheetPackageEntry
            {
                SheetId = $"view-{index}",
                Sha256 = $"view-hash-{index}",
                Destination = destination,
            })
            .ToList(),
    };
}
