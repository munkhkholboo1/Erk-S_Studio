using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// «Has anything changed» must be answerable without redrawing the album.
///
/// 🔴 THE OWNER'S PROJECT TOOK TEN MINUTES TO OPEN AND REACHED 9.7 GB. A timer
/// rebuilt the whole album 1.5 seconds after any activity, opening the album page
/// rebuilt it again, and the album already on disk was never consulted. They put
/// the rule plainly: «студио руу илгээхэд альбумаа шинэчлэнэ. синк хийхэд
/// шинэчлэнэ. ингээд л болоошт» - and «ингэтлээ гацаад байвал хэн ч хэрэглэхгүй»,
/// which makes this a condition of the product existing rather than a wish about
/// speed.
/// </summary>
public sealed class TheAlbumIsNotRebuiltWhenNothingChangedTests
{
    [Fact]
    public void OPENINGTheSameStoredProjectTwiceGivesTheSAMEFingerprint()
    {
        // 🔴 THE INSTRUMENT'S OWN TEST, AND THE WHOLE DESIGN RESTS ON IT. If the
        // build input carries anything that differs between two readings, the
        // fingerprint never matches, the guard never fires, and the feature is
        // dead in a way no other test would notice: every album would simply keep
        // rebuilding, exactly as it does today.
        //
        // 🔴 AND THE FIRST VERSION OF THIS TEST WAS WRONG ABOUT THE PRODUCT. It
        // built two projects with `new`, and this model is full of
        // `DateTimeOffset.UtcNow` and `Guid.NewGuid()` defaults - so it failed for
        // a reason that never happens: in the product a project is READ FROM
        // DISK, and those values are whatever was stored. Two readings of one
        // stored project is the real question.
        string stored = System.Text.Json.JsonSerializer.Serialize(
            SampleProject(), SheetPackageJson.Options);

        Assert.Equal(
            AlbumBuildFingerprint.Of(Load(stored)),
            AlbumBuildFingerprint.Of(Load(stored)));
    }

    [Fact]
    public void ARestatedProjectRoundTrippedThroughItsOwnStoreKeepsTheFingerprint()
    {
        // The album survives a restart, so the fingerprint has to survive one too.
        // Serialising and reading back is what a restart does to a project.
        string stored = System.Text.Json.JsonSerializer.Serialize(
            SampleProject(), SheetPackageJson.Options);
        AlbumProject opened = Load(stored);
        string before = AlbumBuildFingerprint.Of(opened);

        // Saved again by a session that changed nothing, then reopened.
        string resaved = System.Text.Json.JsonSerializer.Serialize(opened, SheetPackageJson.Options);

        Assert.Equal(before, AlbumBuildFingerprint.Of(Load(resaved)));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("album-title")]
    [InlineData("visualization")]
    [InlineData("building-group")]
    [InlineData("source-content-hash")]
    [InlineData("site-context")]
    public void ACHANGEAnywhereInTheBuildInputMovesTheFingerprint(string change)
    {
        // 🔴 THE HALF THAT STOPS THIS BECOMING A WORSE DEFECT THAN THE ONE IT
        // FIXES. A fingerprint that misses a change leaves the person looking at
        // an album that no longer matches their work, with no way to tell.
        //
        // Each case is a different KIND of input - project text, album settings,
        // images, composition, the package a source last received, the site map -
        // because a fingerprint built from one part of the model would pass a test
        // that only varied that part.
        string stored = System.Text.Json.JsonSerializer.Serialize(
            SampleProject(), SheetPackageJson.Options);
        AlbumProject before = Load(stored);
        AlbumProject after = Load(stored);
        Mutate(after, change);

        Assert.NotEqual(AlbumBuildFingerprint.Of(before), AlbumBuildFingerprint.Of(after));
    }

    [Fact]
    public void SHEETSThatHaveNotArrivedAreNotTheSameAsSheetsThatHave()
    {
        // A project can name a source whose sheets are not in the library yet.
        // Sharing a fingerprint with the state where they ARE present would
        // declare the album current while a page is still missing from it.
        AlbumProject project = Load(System.Text.Json.JsonSerializer.Serialize(
            SampleProject(), SheetPackageJson.Options));

        Assert.NotEqual(
            AlbumBuildFingerprint.Of(project, []),
            AlbumBuildFingerprint.Of(project, ["sheet-a"]));
        Assert.NotEqual(
            AlbumBuildFingerprint.Of(project, ["sheet-a"]),
            AlbumBuildFingerprint.Of(project, ["sheet-a", "sheet-b"]));
    }

    [Fact]
    public void THEORDERSheetsArriveInIsNotAChange()
    {
        // The same sheets in a different order draw the same pages. A fingerprint
        // that disagreed would rebuild for nothing, which is the defect this type
        // exists to end.
        AlbumProject project = Load(System.Text.Json.JsonSerializer.Serialize(
            SampleProject(), SheetPackageJson.Options));

        Assert.Equal(
            AlbumBuildFingerprint.Of(project, ["sheet-a", "sheet-b"]),
            AlbumBuildFingerprint.Of(project, ["sheet-b", "sheet-a"]));
    }

    /// <summary>How the product gets a project: read from what was stored.</summary>
    private static AlbumProject Load(string stored) =>
        System.Text.Json.JsonSerializer.Deserialize<AlbumProject>(
            stored, SheetPackageJson.Options)!;

    private static void Mutate(AlbumProject project, string change)
    {
        switch (change)
        {
            case "name":
                project.Name += " (edited)";
                break;
            case "album-title":
                project.Album.Title += " (edited)";
                break;
            case "visualization":
                project.Visualizations.Images[0].IsIncludedInAlbum =
                    !project.Visualizations.Images[0].IsIncludedInAlbum;
                break;
            case "building-group":
                project.BuildingGroups.Add(new ProjectBuildingGroup { Id = "b2", Name = "Блок Б" });
                break;
            case "source-content-hash":
                // What a new package from a plugin actually changes - the owner's
                // «Studio руу илгээх» trigger, arriving without being named.
                project.DesignSources[0].Metadata["cloud.contentHash"] = "0000deadbeef";
                break;
            case "site-context":
                project.SiteContext.Boundary.AreaSquareMeters += 1;
                break;
            default:
                throw new InvalidOperationException("unknown change: " + change);
        }
    }

    private static AlbumProject SampleProject()
    {
        var project = new AlbumProject
        {
            ProjectId = "FP-001",
            Name = "Хурууны хээ",
            Code = "FP",
        };
        project.Album.Title = "Альбом";
        project.BuildingGroups.Add(new ProjectBuildingGroup { Id = "b1", Name = "Блок А" });
        project.DesignSources.Add(new ProjectDesignSource
        {
            Id = "source-1",
            Kind = DesignSourceKind.AutoCad,
            NativeDocumentPath = "plan.dwg",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cloud.sourceKey"] = "general-plan",
                ["cloud.contentHash"] = "abc123",
            },
        });
        project.Visualizations.Images.Add(new ProjectVisualizationImage
        {
            Id = "image-1",
            IsIncludedInAlbum = true,
        });
        project.SiteContext.Boundary = new ProjectSiteBoundary
        {
            SourceId = "source-1",
            AreaSquareMeters = 10_000,
        };
        return project;
    }
}
