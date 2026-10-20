using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The components carrying a working-drawing album's front matter (Studio's
/// list page, or the Revit 01 holding the frames) go to the shared album with
/// every change, and with an edit of the front matter itself (Батнасан,
/// 2026-10-20: the corner tables said 02-05 while the Cloud's list still said
/// 01-04).
/// </summary>
public sealed class THEFRONTMatterTravelsWithEVERYChangeTests
{
    private const string Owner = "owner@example.test";

    private static AlbumDefinition WorkingDrawings() =>
        BuildingWorkingDrawingAlbumTemplate.CreateDefinition("BN");

    private static StudioCloudAlbumSection Component(string code, string sequenceKey = "", string owner = "") => new()
    {
        Code = code,
        SequenceKey = sequenceKey,
        OwnerEmail = owner,
    };

    [Fact]
    public void AnyChangeTakesTheListAndTheRevitOneAlong()
    {
        StudioCloudAlbumSection source = Component("source:et|album-slice|gp|working-drawing-sheets");
        StudioCloudAlbumSection list = Component(StudioFrontMatterComponentScope.GeneratedCode);
        StudioCloudAlbumSection revitOne = Component("source:ba|album-slice|gen|drawing-list-and-notes", "drawing-list-and-notes", Owner);
        StudioCloudAlbumSection other = Component("source:ba|album-slice|ws|working-drawing-sheets");

        List<StudioCloudAlbumSection> result = StudioFrontMatterComponentScope.IncludeCarriers(
            WorkingDrawings(), [source, list, revitOne, other], [source], Owner, canManageCanonicalMetadata: true, frontMatterRequested: false);

        // The Revit 01 takes its source's other slice along: a source's slices
        // go together (Batnasan R12 lost its 53 sheets when the 01 went alone).
        Assert.Equal([source.Code, list.Code, revitOne.Code, other.Code], result.Select(item => item.Code));
    }

    [Fact]
    public void TheRevitOneNeverGoesWithoutItsSourcesSheets()
    {
        // 🔴 BATNASAN R12, 2026-10-20: the ЕТ source changed; the Revit 01 was
        // added as a carrier; the sync read the Revit source as re-rendered by
        // that one slice and removed its 53 working sheets from the shared album.
        string et = StudioAlbumComponentIdentity.SourceSliceCode(Owner, "et-source", "", "working-drawing-sheets");
        string revitOne = StudioAlbumComponentIdentity.SourceSliceCode(Owner, "revit-source", "", "drawing-list-and-notes");
        string revitSheets = StudioAlbumComponentIdentity.SourceSliceCode(Owner, "revit-source", "", "working-drawing-sheets");
        StudioCloudAlbumSection etSlice = Component(et, "working-drawing-sheets", Owner);
        StudioCloudAlbumSection oneSlice = Component(revitOne, "drawing-list-and-notes", Owner);
        StudioCloudAlbumSection sheetsSlice = Component(revitSheets, "working-drawing-sheets", Owner);

        List<StudioCloudAlbumSection> result = StudioFrontMatterComponentScope.IncludeCarriers(
            WorkingDrawings(), [etSlice, oneSlice, sheetsSlice], [etSlice], Owner, canManageCanonicalMetadata: true, frontMatterRequested: false);

        Assert.Contains(result, item => item.Code == revitOne);
        Assert.Contains(result, item => item.Code == revitSheets);
    }

    [Fact]
    public void ASyncMayNotRemoveWhatTheLocalBuildDraws()
    {
        string revitSheets = StudioAlbumComponentIdentity.SourceSliceCode(Owner, "revit-source", "", "working-drawing-sheets");
        StudioCloudAlbumSection sheetsSlice = Component(revitSheets, "working-drawing-sheets", Owner);
        var removal = new StudioAlbumComponentUpload(revitSheets, "Ажлын зураг", 300, "", Remove: true);
        var other = new StudioAlbumComponentUpload("source:gone", "Хуучин", 400, "", Remove: true);

        Assert.Throws<InvalidDataException>(() => StudioComponentRemovalGuard.Require([removal], [sheetsSlice]));
        StudioComponentRemovalGuard.Require([other], [sheetsSlice]);
    }

    [Fact]
    public void BothSyncPathsSkipAndGuardRenderedComponents()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string components = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.AlbumComponents.cs"));

        Assert.Contains("renderedCodes.Contains(component.Code))", components, StringComparison.Ordinal);
        Assert.Contains("StudioComponentRemovalGuard.Require(uploads, rendered);", components, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEditOfTheNoteAloneStillReachesTheSharedAlbum()
    {
        StudioCloudAlbumSection revitOne = Component("source:ba|album-slice|gen|drawing-list-and-notes", "drawing-list-and-notes");

        List<StudioCloudAlbumSection> result = StudioFrontMatterComponentScope.IncludeCarriers(
            WorkingDrawings(), [revitOne], [], Owner, canManageCanonicalMetadata: false, frontMatterRequested: true);

        Assert.Equal([revitOne.Code], result.Select(item => item.Code));
    }

    [Fact]
    public void NothingChangedSendsNothing()
    {
        StudioCloudAlbumSection list = Component(StudioFrontMatterComponentScope.GeneratedCode);

        Assert.Empty(StudioFrontMatterComponentScope.IncludeCarriers(
            WorkingDrawings(), [list], [], Owner, canManageCanonicalMetadata: true, frontMatterRequested: false));
    }

    [Fact]
    public void ACarrierThisAccountMayNotWriteStaysOut()
    {
        StudioCloudAlbumSection source = Component("source:et|album-slice|gp|working-drawing-sheets");
        StudioCloudAlbumSection list = Component(StudioFrontMatterComponentScope.GeneratedCode);
        StudioCloudAlbumSection theirOne = Component("source:ba|album-slice|gen|drawing-list-and-notes", "drawing-list-and-notes", "pfr@example.test");

        List<StudioCloudAlbumSection> result = StudioFrontMatterComponentScope.IncludeCarriers(
            WorkingDrawings(), [source, list, theirOne], [source], Owner, canManageCanonicalMetadata: false, frontMatterRequested: false);

        Assert.Equal([source.Code], result.Select(item => item.Code));
    }

    [Fact]
    public void AConceptAlbumIsLeftAsItWas()
    {
        StudioCloudAlbumSection source = Component("source:et");
        StudioCloudAlbumSection list = Component(StudioFrontMatterComponentScope.GeneratedCode);
        var concept = new AlbumDefinition { TemplateId = BuildingArchitectureConceptAlbumTemplate.TemplateId };

        Assert.Equal(
            [source.Code],
            StudioFrontMatterComponentScope.IncludeCarriers(concept, [source, list], [source], Owner, true, false)
                .Select(item => item.Code));
    }

    [Fact]
    public void StudiosListGivesWayToTheRevitOne()
    {
        StudioCloudAlbumSection revitOne = Component("source:ba|album-slice||drawing-list-and-notes", "drawing-list-and-notes");
        StudioCloudAlbumSection sheets = Component("source:ba|album-slice||working-drawing-sheets", "working-drawing-sheets");

        Assert.True(StudioFrontMatterComponentScope.IsReplacedBySource(StudioFrontMatterComponentScope.GeneratedCode, [revitOne, sheets]));
        Assert.False(StudioFrontMatterComponentScope.IsReplacedBySource(StudioFrontMatterComponentScope.GeneratedCode, [sheets]));
        Assert.False(StudioFrontMatterComponentScope.IsReplacedBySource("generated:cover:Cover", [revitOne]));
    }

    [Fact]
    public void TheRendererGuardLetsTheReplacedListGo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string components = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.AlbumComponents.cs"));

        int guards = 0;
        int at = 0;
        while ((at = components.IndexOf("string[] unrenderedRendererMigrations = missing", at, StringComparison.Ordinal)) >= 0)
        {
            int end = components.IndexOf(".ToArray();", at, StringComparison.Ordinal);
            Assert.Contains("StudioFrontMatterComponentScope.IsReplacedBySource(code, rendered)", components[at..end], StringComparison.Ordinal);
            guards++;
            at = end;
        }
        Assert.Equal(2, guards);
    }

    [Fact]
    public void TheSyncAndTheEditBothUseTheScope()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string app = Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App");
        string components = File.ReadAllText(Path.Combine(app, "ShellView.AlbumComponents.cs"));
        string workspaces = File.ReadAllText(Path.Combine(app, "ShellView.Workspaces.cs"));

        int covers = components.IndexOf("selected = coverSelection.Components.ToList();", StringComparison.Ordinal);
        int carriers = components.IndexOf("StudioFrontMatterComponentScope.IncludeCarriers(", StringComparison.Ordinal);
        int missing = components.IndexOf("string[] missing = requestedCodes", StringComparison.Ordinal);
        Assert.True(covers > 0 && carriers > covers && missing > carriers);
        Assert.Contains("state.MarkAlbumComponentChanged(StudioFrontMatterComponentScope.GeneratedCode);", workspaces, StringComparison.Ordinal);
    }
}

/// <summary>
/// In the shared album the front matter stands where it prints: right after
/// the cover, before the general plan and the building's sets (Батнасан,
/// 2026-10-20: the Revit 01 arrived as a new component and was put last).
/// </summary>
public sealed class THEFRONTMatterStandsAfterTheCOVERInTheSHAREDAlbumTests
{
    private static readonly Dictionary<string, int> NoSources = new(StringComparer.OrdinalIgnoreCase);

    private static int Order(string code) =>
        StudioAlbumComponentOrderPolicy.Resolve(new ProjectWorkspace(), code, "", 0, NoSources);

    [Fact]
    public void TheRevitOneAndStudiosListComeRightAfterTheCover()
    {
        string revitOne = StudioAlbumComponentIdentity.SourceSliceCode("owner@example.test", "revit", "", "drawing-list-and-notes");
        string generalPlan = StudioAlbumComponentIdentity.SourceSliceCode("owner@example.test", "dwg", "", "working-drawing-sheets");

        int cover = Order(ProjectCloudSyncMetadata.CoverComponentCode);
        Assert.True(cover < Order(revitOne));
        Assert.Equal(Order(revitOne), Order(StudioFrontMatterComponentScope.GeneratedCode));
        Assert.True(Order(revitOne) < Order(generalPlan));
    }
}
