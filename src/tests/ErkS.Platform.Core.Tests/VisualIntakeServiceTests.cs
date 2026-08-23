using System.Text.Json;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Noticing that a visual package has arrived.
///
/// Without this the reader, the import and the pool were all in place and a
/// package could still land in the inbox and do nothing at all - no error, no
/// notice, nothing. That is the failure this codebase keeps finding, and it is
/// the one worth closing even when every other piece already works.
///
/// It is a second watcher rather than a wider filter on the sheet one. The two
/// channels answer to different contracts, and the sheet intake is the path a
/// user's day runs through.
/// </summary>
public sealed class VisualIntakeServiceTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public VisualIntakeServiceTests() => Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void APackageAlreadyInTheFolderIsFoundOnWatching()
    {
        // A user may export while Studio is closed. Whatever is already there
        // has to be found when it opens, or the delivery is lost to silence.
        string folder = WritePackage("delivery-1", Raster("view-1"));
        var arrivals = new List<VisualPackageArrival>();
        using var intake = new VisualIntakeService();
        intake.PackageProcessed += arrivals.Add;

        intake.WatchFolder(workDirectory, sourceId: "revit-1");

        VisualPackageArrival arrival = Assert.Single(arrivals);
        Assert.True(arrival.Result.IsLoaded);
        Assert.Equal("revit-1", arrival.SourceId);
        Assert.Equal(folder, arrival.PackageFolder);
        Assert.Single(arrival.Result.Accepted);
    }

    [Fact]
    public void APackageThatCannotBeUsedIsKeptRatherThanForgotten()
    {
        // A refusal the user can be shown, instead of leaving them to wonder
        // why the export they just ran did nothing.
        string folder = Path.Combine(workDirectory, "broken");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "p" + VisualPackageContract.ManifestSuffix),
            """{ "schemaVersion": 99 }""");
        using var intake = new VisualIntakeService();

        intake.WatchFolder(workDirectory);

        RefusedVisualPackage refusal = Assert.Single(intake.RefusedPackages);
        Assert.NotEmpty(refusal.Issues);
    }

    [Fact]
    public void TheSameManifestIsNotRefusedTwiceOver()
    {
        string folder = Path.Combine(workDirectory, "broken");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "p" + VisualPackageContract.ManifestSuffix);
        File.WriteAllText(path, """{ "schemaVersion": 99 }""");
        using var intake = new VisualIntakeService();

        intake.Process(path);
        intake.Process(path);

        Assert.Single(intake.RefusedPackages);
    }

    [Fact]
    public void SheetPackagesAreNoneOfItsBusiness()
    {
        // The whole reason for a second watcher: the two channels do not read
        // each other's deliveries.
        string folder = Path.Combine(workDirectory, "delivery");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "p.erks-sheets.json"), "{}");
        var arrivals = new List<VisualPackageArrival>();
        using var intake = new VisualIntakeService();
        intake.PackageProcessed += arrivals.Add;

        intake.WatchFolder(workDirectory);

        Assert.Empty(arrivals);
        Assert.Empty(intake.RefusedPackages);
    }

    [Fact]
    public void AFolderWithNothingInItIsNotAProblem()
    {
        using var intake = new VisualIntakeService();
        var errors = new List<string>();
        intake.IntakeError += errors.Add;

        intake.WatchFolder(Path.Combine(workDirectory, "not-there-yet"));

        Assert.Empty(errors);
        Assert.Single(intake.WatchedFolders);
    }

    [Fact]
    public void APackageNobodyHasTakenInIsReportedAsPending()
    {
        WritePackage("delivery-1", Raster("view-1"));

        PendingVisualPackageSurvey survey = VisualInboxScanner.Survey(workDirectory, null);

        Assert.True(survey.HasPending);
        Assert.Equal(1, survey.Count);
        Assert.NotNull(survey.NewestExportedAtUtc);
    }

    [Fact]
    public void APackageAlreadyTakenInIsNotStillPending()
    {
        WritePackage(
            "delivery-1",
            Raster("view-1"),
            exportedAt: new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero));

        PendingVisualPackageSurvey survey = VisualInboxScanner.Survey(
            workDirectory,
            new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero));

        Assert.False(survey.HasPending);
    }

    [Fact]
    public void ANewerPackageIsPendingEvenWhenAnOlderOneWasTakenIn()
    {
        WritePackage(
            "delivery-1",
            Raster("view-1"),
            exportedAt: new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));
        WritePackage(
            "delivery-2",
            Raster("view-2"),
            exportedAt: new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero));

        PendingVisualPackageSurvey survey = VisualInboxScanner.Survey(
            workDirectory,
            new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, survey.Count);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
            survey.NewestExportedAtUtc);
    }

    [Fact]
    public void HowFarASourceHasBeenTakenInIsReadFromTheMaterialItself()
    {
        // No second record to keep in step with the pool: the items say it.
        var portfolio = new ProjectPortfolio
        {
            Items =
            [
                new ProjectPortfolioItem
                {
                    Kind = ProjectPortfolioItemKinds.Visual,
                    SourceSheetKey = VisualPackageImportService.MakeKey("revit-1", "view-1"),
                    SourceExportedAtUtc = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
                },
                new ProjectPortfolioItem
                {
                    Kind = ProjectPortfolioItemKinds.Visual,
                    SourceSheetKey = VisualPackageImportService.MakeKey("revit-1", "view-2"),
                    SourceExportedAtUtc = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
                },
                new ProjectPortfolioItem
                {
                    // Another source entirely, and none of this one's business.
                    Kind = ProjectPortfolioItemKinds.Visual,
                    SourceSheetKey = VisualPackageImportService.MakeKey("revit-2", "view-1"),
                    SourceExportedAtUtc = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero),
                },
            ],
        };

        Assert.Equal(
            new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
            VisualInboxScanner.AbsorbedUpTo(portfolio, "revit-1"));
    }

    [Fact]
    public void ASourceThatHasDeliveredNothingHasTakenInNothing()
    {
        Assert.Null(VisualInboxScanner.AbsorbedUpTo(new ProjectPortfolio(), "revit-1"));
        Assert.Null(VisualInboxScanner.AbsorbedUpTo(null, "revit-1"));
    }

    private string WritePackage(
        string name,
        VisualAsset asset,
        DateTimeOffset? exportedAt = null)
    {
        string folder = Path.Combine(workDirectory, name);
        Directory.CreateDirectory(folder);
        string filePath = Path.Combine(folder, asset.FileName);
        File.WriteAllText(filePath, "bytes for " + asset.AssetId);
        asset.Sha256 = ProjectDocumentFileStore.ComputeSha256(filePath);

        var manifest = new VisualPackageManifest
        {
            SchemaVersion = VisualPackageContract.CurrentSchemaVersion,
            PackageId = Guid.NewGuid().ToString("N"),
            ProjectId = "P-001",
            ExportedAtUtc = exportedAt ?? new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
            Source = new VisualPackageSource
            {
                SourceId = "revit-1",
                Application = VisualPackageContract.ApplicationRevit,
            },
            Assets = [asset],
        };
        File.WriteAllText(
            Path.Combine(folder, name + VisualPackageContract.ManifestSuffix),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return folder;
    }

    private static VisualAsset Raster(string assetId) => new()
    {
        AssetId = assetId,
        ViewName = assetId,
        ViewType = "ThreeD",
        Kind = VisualAssetKinds.Render,
        MediaType = VisualMediaTypes.Png,
        FileName = assetId + ".png",
        WidthPx = 4489,
        HeightPx = 2835,
        Dpi = 300,
    };
}
