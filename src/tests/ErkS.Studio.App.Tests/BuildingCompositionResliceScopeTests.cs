using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The measured defect, turned into tests.
///
/// A real project carried «source:…:e9cb…|album-slice|&lt;Орон сууц-2&gt;.sections»
/// in the cloud while its local assignments said that source belonged to
/// Орон сууц-1, and the source that really belonged to Орон сууц-2 had no cloud
/// component at all. Both sides of the composition were correct; the SLICE was
/// frozen, because reassigning a sheet marked only the building sub-covers for
/// re-upload and never the source components whose code carries the building.
/// </summary>
public sealed class BuildingCompositionResliceScopeTests
{
    private const string Owner = "munkhkholboo@gmail.com";
    private const string HouseOne = "70363657e3fb40c3b01c3913fa7a4433";
    private const string HouseTwo = "8fdda356c30d42c1a2daba11745b6d21";
    private const string SourceOne = "e9cb29d09944461c92ce2950ee0324ee";
    private const string SourceTwo = "fa18af4cea3942c6904466aa6035c073";
    private const string SiteSource = "2e7bc5f2bbdf4d54b819d2506052771f";

    [Fact]
    public void AReassignedSourceIsRequestedByItsBASECode()
    {
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        Dictionary<string, string> before = Assignments(
            (SourceOne, HouseTwo),
            (SourceTwo, HouseOne));
        project.SheetBuildingAssignments = Assignments(
            (SourceOne, HouseOne),
            (SourceTwo, HouseTwo));

        IReadOnlyList<string> codes =
            StudioBuildingCompositionResliceScope.SourceComponentCodes(
                project,
                album,
                project.BuildingGroups,
                before,
                Owner);

        // Both sources moved, so both must go up again.
        Assert.Equal(2, codes.Count);
        Assert.Contains(
            StudioAlbumComponentIdentity.SourceCode(Owner, SourceOne),
            codes,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            StudioAlbumComponentIdentity.SourceCode(Owner, SourceTwo),
            codes,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void THERequestedCodeCarriesNOSliceOfItsOwn()
    {
        // 🔴 THIS IS THE WHOLE MECHANISM AND IT IS EASY TO BREAK BY BEING
        // HELPFUL. Requesting the code the page will HAVE looks more precise and
        // does nothing: the merge matches a requested code that already names a
        // slice by equality only, so the new slice would be uploaded and the old
        // one left behind. A code with no slice matches every slice of that base,
        // which is what both selection and stale-removal are built on.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        Dictionary<string, string> before = Assignments((SourceOne, HouseTwo));
        project.SheetBuildingAssignments = Assignments((SourceOne, HouseOne));

        string code = StudioBuildingCompositionResliceScope.SourceComponentCodes(
            project,
            album,
            project.BuildingGroups,
            before,
            Owner).Single();

        Assert.DoesNotContain("album-slice", code, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            StudioAlbumComponentIdentity.TryGetSourceSlice(code, out _, out _),
            "a requested code with a slice matches by equality and cannot replace the old slice");
    }

    [Fact]
    public void REORDERINGTheGroupsAloneStillResendsTheSources()
    {
        // The slice codes are identical after a reorder - only their position
        // changes. Sending nothing leaves the cloud album in the old sequence
        // with perfectly valid codes, which is the same complaint in its
        // quietest form.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        List<ProjectBuildingGroup> before = Copy(project.BuildingGroups);
        project.BuildingGroups[0].Order = 2;
        project.BuildingGroups[1].Order = 1;

        IReadOnlyList<string> codes =
            StudioBuildingCompositionResliceScope.SourceComponentCodes(
                project,
                album,
                before,
                project.SheetBuildingAssignments,
                Owner);

        Assert.Equal(2, codes.Count);
    }

    [Fact]
    public void ARENAMEMovesNothingAndSendsNothing()
    {
        // The counterpart, and the reason the comparison is on ids: a name is
        // in no slice code and in no position, so a rename that re-sent every
        // source would be pure noise on an edit that moved nothing.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        List<ProjectBuildingGroup> before = Copy(project.BuildingGroups);
        project.BuildingGroups[0].Name = "Орон сууц A";

        Assert.Empty(StudioBuildingCompositionResliceScope.SourceComponentCodes(
            project,
            album,
            before,
            project.SheetBuildingAssignments,
            Owner));
    }

    [Fact]
    public void ASourceWithNoAlbumPageIsNEVERRequested()
    {
        // 🔴 A REQUEST THIS BUILD CANNOT ANSWER IS RESOLVED AS A REMOVAL. Asking
        // for a source whose pages this device does not hold would not refresh
        // the cloud component - it would delete it. The guard is the difference
        // between replacing pages and losing them.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        album.Pages.RemoveAll(page =>
            page.SheetKey.StartsWith(SourceTwo, StringComparison.OrdinalIgnoreCase));
        Dictionary<string, string> before = Assignments(
            (SourceOne, HouseTwo),
            (SourceTwo, HouseOne));
        project.SheetBuildingAssignments = Assignments(
            (SourceOne, HouseOne),
            (SourceTwo, HouseTwo));

        IReadOnlyList<string> codes =
            StudioBuildingCompositionResliceScope.SourceComponentCodes(
                project,
                album,
                project.BuildingGroups,
                before,
                Owner);

        Assert.Equal(
            [StudioAlbumComponentIdentity.SourceCode(Owner, SourceOne)],
            codes);
    }

    [Fact]
    public void ANUNTOUCHEDCompositionRequestsNothing()
    {
        // The positive control for every test above: the same fixture with
        // nothing changed has to stay silent, or "it asked for both sources"
        // would prove nothing at all.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();

        Assert.Empty(StudioBuildingCompositionResliceScope.SourceComponentCodes(
            project,
            album,
            project.BuildingGroups,
            project.SheetBuildingAssignments,
            Owner));
    }

    [Fact]
    public void ASourceWithNoBuildingSheetsIsLeftAlone()
    {
        // The site drawings belong to no building, so no composition edit can
        // move them and re-sending them would be waste.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        project.Sources.Add(SourceRecord(SiteSource));
        album.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = SiteSource + "|master-plan",
        });
        Dictionary<string, string> before = Assignments((SourceOne, HouseTwo));
        project.SheetBuildingAssignments = Assignments((SourceOne, HouseOne));

        IReadOnlyList<string> codes =
            StudioBuildingCompositionResliceScope.SourceComponentCodes(
                project,
                album,
                project.BuildingGroups,
                before,
                Owner);

        Assert.Equal(
            [StudioAlbumComponentIdentity.SourceCode(Owner, SourceOne)],
            codes);
    }

    [Fact]
    public void ACloudComponentFiledUnderTheWRONGBuildingIsNamed()
    {
        // The measured case, in one assertion: the cloud says Орон сууц-2, every
        // sheet of that source says Орон сууц-1, and no edit is coming to shake
        // it loose because the assignments are already correct.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        Publish(project, SourceOne, HouseTwo, "sections");
        Publish(project, SourceTwo, HouseTwo, "sections");

        IReadOnlyList<string> codes =
            StudioBuildingCompositionResliceScope.StaleCloudSourceComponentCodes(
                project,
                album,
                Owner);

        Assert.Equal(
            [StudioAlbumComponentIdentity.SourceCode(Owner, SourceOne)],
            codes);
    }

    [Fact]
    public void ASourceWithNoCloudComponentAtALLIsNamed()
    {
        // The other half of the same measurement: the source that really did
        // belong to Орон сууц-2 had no component in the cloud at all, so its
        // pages were simply absent from the album everybody else reads.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        Publish(project, SourceOne, HouseOne, "sections");

        IReadOnlyList<string> codes =
            StudioBuildingCompositionResliceScope.StaleCloudSourceComponentCodes(
                project,
                album,
                Owner);

        Assert.Equal(
            [StudioAlbumComponentIdentity.SourceCode(Owner, SourceTwo)],
            codes);
    }

    [Fact]
    public void ACloudAlbumThatAGREESNamesNothing()
    {
        // The positive control. Without it "it named a source" would prove only
        // that the method returns something.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        Publish(project, SourceOne, HouseOne, "sections");
        Publish(project, SourceOne, HouseOne, "elevations");
        Publish(project, SourceTwo, HouseTwo, "sections");

        Assert.Empty(StudioBuildingCompositionResliceScope.StaleCloudSourceComponentCodes(
            project,
            album,
            Owner));
    }

    [Fact]
    public void APROJECTThatWasNeverSharedNamesNothing()
    {
        // 🔴 «NO COMPONENT» MEANS TWO DIFFERENT THINGS. On a shared album it
        // means a source is missing; on a project that has never been synced it
        // means nothing has been sent yet, and reading the second as the first
        // would arm every source on every unshared project.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();

        Assert.Empty(StudioBuildingCompositionResliceScope.StaleCloudSourceComponentCodes(
            project,
            album,
            Owner));
    }

    [Fact]
    public void ASourceOwnedBySOMEBODYElseIsLeftToItsOwner()
    {
        // This runs by itself every time the album is shown, so a component this
        // device can never be authorised to send would sit pending forever and
        // report the project as carrying unauthorised changes on every visit.
        (ProjectWorkspace project, AlbumDefinition album) = Fixture();
        Publish(project, SourceOne, HouseOne, "sections");
        project.Sources
            .Single(source => source.Id == SourceTwo)
            .Metadata!["cloud.ownerEmail"] = "someone.else@example.com";

        Assert.Empty(StudioBuildingCompositionResliceScope.StaleCloudSourceComponentCodes(
            project,
            album,
            Owner));
    }

    private static void Publish(
        ProjectWorkspace project,
        string sourceId,
        string groupId,
        string sequenceKey)
    {
        project.Cloud.SharedAlbumComponents.Add(new ProjectCloudAlbumComponentReference
        {
            Code = StudioAlbumComponentIdentity.SourceSliceCode(
                Owner,
                sourceId,
                "studio-building:" + groupId,
                sequenceKey),
            ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
            SourceKey = sourceId,
            OwnerEmail = Owner,
        });
    }

    private static List<ProjectBuildingGroup> Copy(
        IEnumerable<ProjectBuildingGroup> groups) =>
        groups
            .Select(group => new ProjectBuildingGroup
            {
                Id = group.Id,
                Name = group.Name,
                Order = group.Order,
            })
            .ToList();

    private static Dictionary<string, string> Assignments(
        params (string SourceId, string GroupId)[] entries)
    {
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string sourceId, string groupId) in entries)
        {
            assignments[sourceId + "|sheet-a"] = groupId;
            assignments[sourceId + "|sheet-b"] = groupId;
        }
        return assignments;
    }

    private static (ProjectWorkspace Project, AlbumDefinition Album) Fixture()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup { Id = HouseOne, Name = "Орон сууц-1", Order = 1 },
                new ProjectBuildingGroup { Id = HouseTwo, Name = "Орон сууц-2", Order = 2 },
            ],
        };
        project.Sources.Add(SourceRecord(SourceOne));
        project.Sources.Add(SourceRecord(SourceTwo));
        project.SheetBuildingAssignments = Assignments(
            (SourceOne, HouseOne),
            (SourceTwo, HouseTwo));

        var album = new AlbumDefinition();
        foreach (string sourceId in new[] { SourceOne, SourceTwo })
        {
            album.Pages.Add(new AlbumPageDefinition { SheetKey = sourceId + "|sheet-a" });
            album.Pages.Add(new AlbumPageDefinition { SheetKey = sourceId + "|sheet-b" });
        }
        return (project, album);
    }

    private static ProjectDesignSource SourceRecord(string sourceId) =>
        new()
        {
            Id = sourceId,
            Kind = DesignSourceKind.Revit,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cloud.sourceKey"] = sourceId,
                ["cloud.manifestId"] = sourceId + "-manifest",
                ["cloud.contentHash"] = sourceId + "-hash",
                ["cloud.ownerEmail"] = Owner,
            },
        };
}
