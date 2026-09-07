using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The user's own description, as an experiment: «Автокадаас орж ирсэн
/// байгуулалт болон Рэвитээс орж ирсэн огтлол, нүүр тал нэг төрлийн барилга.
/// 2 өөр эх үүсвэртэй нэг барилга.»
///
/// One building. Its floor plans arrive from AutoCAD, its sections and
/// elevations from Revit. They must interleave BY KIND - plans, sections,
/// elevations - and not split into two blocks by which product sent them.
///
/// And the half no earlier test covered, which is the half they say is broken:
/// «анх удаа тохируулахад зөв байрандаа ордог байх. Харин тэдний барилгын
/// төрлийг ДАРАА НЬ ӨӨРЧЛӨХӨД байрлалаа зөв олдоггүй.»
/// </summary>
public sealed class OneBuildingTwoSourcesTests : IDisposable
{
    private string acadManifestPath = "";

    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(), "erks-one-building-" + Guid.NewGuid().ToString("N"));

    public OneBuildingTwoSourcesTests() => Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ONEBuildingFromTWOSourcesInterleavesByKIND()
    {
        (AlbumProject project, SheetLibrary library) = OneBuildingTwoSources();

        Assert.Equal(
            ["Давхрын байгуулалт", "Огтлол", "Нүүр тал"],
            KindOrder(project, library));
    }

    [Fact]
    public void CHANGINGTheKindLATERStillPlacesThePageCorrectly()
    {
        // 🔴 THE HALF THE USER SAYS IS BROKEN. Setting it up correctly the first
        // time works; changing a sheet's kind afterwards is where they see it go
        // wrong.
        (AlbumProject project, SheetLibrary library) = OneBuildingTwoSources();
        Assert.Equal(
            ["Давхрын байгуулалт", "Огтлол", "Нүүр тал"],
            KindOrder(project, library));

        AlbumPageDefinition plan = PlanPage(project);
        plan.ContentKindOverride = "Нүүр тал";

        // It must now sort WITH the elevations, at the end of the building.
        Assert.Equal(
            ["Огтлол", "Нүүр тал", "Нүүр тал"],
            KindOrder(project, library));
    }

    [Fact]
    public void CHANGINGTheKindBACKRestoresTheOriginalOrder()
    {
        // The round trip. A rule that reads the kind fresh returns to where it
        // started; one that remembers a position does not.
        (AlbumProject project, SheetLibrary library) = OneBuildingTwoSources();
        AlbumPageDefinition plan = PlanPage(project);

        plan.ContentKindOverride = "Нүүр тал";
        _ = KindOrder(project, library);

        plan.ContentKindOverride = "Давхрын байгуулалт";
        Assert.Equal(
            ["Давхрын байгуулалт", "Огтлол", "Нүүр тал"],
            KindOrder(project, library));
    }

    [Fact]
    public void ASTOREDOrderDoesNotFREEZEThePagesInPlace()
    {
        // 🔴 THE SHAPE OF «түр хугацаанд хуурамчаар засагдчихаад байна». Studio
        // reorders the stored page list and SAVES it on a composition edit. If
        // that saved sequence then outranks the kinds, a later change of kind
        // cannot move anything - the page stays where the OLD kind put it, and
        // the album looks corrected exactly once.
        (AlbumProject project, SheetLibrary library) = OneBuildingTwoSources();

        IReadOnlyList<AlbumPageDefinition> stored =
            BuildingArchitectureConceptAlbumSequencer.OrderPages(
                project.Album,
                project.Album.Pages,
                library,
                project.DesignSources,
                project.BuildingGroups,
                project.SheetBuildingAssignments);
        project.Album.Pages.Clear();
        project.Album.Pages.AddRange(stored);

        PlanPage(project).ContentKindOverride = "Нүүр тал";

        Assert.Equal(
            ["Огтлол", "Нүүр тал", "Нүүр тал"],
            KindOrder(project, library));
    }

    [Fact]
    public void MOVINGASheetToANOTHERBuildingTakesItWithIt()
    {
        // The other way a person changes "the building type" of a sheet: they
        // reassign it. Two buildings, and the AutoCAD plan moves to the second.
        (AlbumProject project, SheetLibrary library) = OneBuildingTwoSources();
        project.BuildingGroups.Add(new ProjectBuildingGroup { Name = "Барилга Б", Order = 2 });

        AlbumPageDefinition plan = PlanPage(project);
        project.SheetBuildingAssignments[plan.SheetKey] = project.BuildingGroups[1].Id;

        // The plan now belongs to the SECOND building, so it must come after
        // the first building's pages rather than leading them.
        Assert.Equal(
            ["Огтлол", "Нүүр тал", "Давхрын байгуулалт"],
            KindOrder(project, library));
    }

    private static AlbumPageDefinition PlanPage(AlbumProject project) =>
        project.Album.Pages.Single(page =>
            page.SheetKey.Contains("acad", StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<string> KindOrder(AlbumProject project, SheetLibrary library) =>
        BuildingArchitectureConceptAlbumSequencer.Create(
                project.Album,
                project.Album.Pages,
                library,
                project.DesignSources,
                generatedPageCount: -1,
                project.BuildingGroups,
                project.SheetBuildingAssignments)
            .Select(page => AlbumPageSourceMetadata.ResolveContentKind(
                page.Page,
                page.Sheet?.Entry ?? new SheetPackageEntry()))
            .ToList();

    private (AlbumProject Project, SheetLibrary Library) OneBuildingTwoSources()
    {
        var library = new SheetLibrary();
        acadManifestPath = CreatePackage(
            "acad",
            SheetSourceApplication.AutoCad,
            [("A-01", "Давхрын байгуулалт")]);
        library.Absorb(SheetPackageReader.Load(acadManifestPath));
        library.Absorb(SheetPackageReader.Load(CreatePackage(
            "revit",
            SheetSourceApplication.Revit,
            [("R-01", "Огтлол"), ("R-02", "Нүүр тал")])));

        var project = new AlbumProject
        {
            Name = "Нэг барилга",
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар"),
        };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;
        project.BuildingGroups.Add(new ProjectBuildingGroup { Name = "Барилга А", Order = 1 });

        // Added with the REVIT pages first, so a sequencer that merely kept
        // arrival order would be visibly wrong rather than accidentally right.
        foreach (SheetRecord record in library.Snapshot()
                     .OrderByDescending(item => item.SourceId, StringComparer.Ordinal))
        {
            if (project.DesignSources.All(source => source.Id != record.SourceId))
            {
                project.DesignSources.Add(new ProjectDesignSource
                {
                    Id = record.SourceId,
                    Name = record.SourceId,
                    Kind = record.Source.Application == SheetSourceApplication.Revit
                        ? DesignSourceKind.Revit
                        : DesignSourceKind.AutoCad,
                });
            }

            project.Album.Pages.Add(new AlbumPageDefinition
            {
                SheetKey = record.Key,
                PageFormatId = PageFormatCatalog.SourceAsIsId,
                PlacementMode = PagePlacementMode.FullPage,
            });
            project.SheetBuildingAssignments[record.Key] = project.BuildingGroups[0].Id;
        }

        return (project, library);
    }

    private string CreatePackage(
        string name,
        SheetSourceApplication application,
        (string Number, string Kind)[] sheets)
    {
        var entries = new List<SheetPackageEntry>();
        foreach ((string number, string kind) in sheets)
        {
            string pdfPath = Path.Combine(workDirectory, name + "-" + number + ".pdf");
            using (var document = new PdfDocument())
            {
                PdfPage page = document.AddPage();
                page.Width = XUnit.FromMillimeter(420);
                page.Height = XUnit.FromMillimeter(297);
                using (XGraphics graphics = XGraphics.FromPdfPage(page))
                    graphics.DrawRectangle(XPens.Black, 20, 20, 100, 100);
                document.Save(pdfPath);
            }

            entries.Add(new SheetPackageEntry
            {
                SheetId = name + "-" + number,
                Number = number,
                Name = number,
                ContentKind = kind,
                WidthMm = 420,
                HeightMm = 297,
                ContentWidthMm = 420,
                ContentHeightMm = 297,
                PdfFileName = Path.GetFileName(pdfPath),
                PageCount = 1,
            });
        }

        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = name + "-source",
                Application = application,
            },
            Sheets = entries,
        };

        return SheetPackageWriter.Write(manifest, workDirectory, name);
    }

    [Fact]
    public void APageWhoseSOURCEISNOTONTHISMACHINEStillKeepsItsPlace()
    {
        // 🔴 THE CASE THE FIXTURE ABOVE CANNOT SEE. In the user's project the
        // two sources of one building do not both live here: the AutoCAD plans
        // are theirs, the Revit sheets belong to another member. A sheet whose
        // package is not on this machine is NOT in the library, so the
        // sequencer has no SheetPackageEntry to read a kind from - and if the
        // page carries no override of its own, the kind resolves to empty,
        // which ranks unclassified and sorts to the END of the building.
        //
        // That would split one building exactly the way they describe, and it
        // would look fine on the machine that owns every source.
        (AlbumProject project, SheetLibrary library) = OneBuildingTwoSources();

        // A library holding ONLY the AutoCAD package - the other member's
        // sheets never arrived here.
        var localOnly = new SheetLibrary();
        foreach (SheetRecord record in library.Snapshot()
                     .Where(item => item.SourceId.Contains("acad", StringComparison.OrdinalIgnoreCase)))
        {
            _ = record;
        }

        localOnly.Absorb(SheetPackageReader.Load(acadManifestPath));

        // Reconciliation captured each page's kind when its package was last
        // seen, which is what makes the pages survive the package being gone.
        foreach (AlbumPageDefinition page in project.Album.Pages)
        {
            page.SourceContentKindSnapshot = page.SheetKey.Contains("R-01", StringComparison.OrdinalIgnoreCase)
                ? "Огтлол"
                : page.SheetKey.Contains("R-02", StringComparison.OrdinalIgnoreCase)
                    ? "Нүүр тал"
                    : "Давхрын байгуулалт";
        }

        // MEASURED BEFORE THE FIX: [Давхрын байгуулалт | <EMPTY> | <EMPTY>] -
        // both remote pages lost their kind and ranked unclassified.
        Assert.Equal(
            ["Давхрын байгуулалт", "Огтлол", "Нүүр тал"],
            KindOrder(project, localOnly));
    }

    [Fact]
    public void RECONCILIATIONCapturesTheKindOntoThePage()
    {
        // 🔴 THE CALLER, NOT THE RULE. The test above hands the snapshot to the
        // page itself, so it proves the FALLBACK works and says nothing about
        // whether anything ever fills it in. A mutation deleting the capture
        // left that test green - the same "rule clean, caller unchecked" shape
        // this codebase keeps producing, and this time in my own test.
        string manifestPath = CreatePackage(
            "capture",
            SheetSourceApplication.Revit,
            [("C-01", "Огтлол")]);

        var library = new SheetLibrary();
        SheetPackageLoadResult loaded = SheetPackageReader.Load(manifestPath);
        library.Absorb(loaded);

        var project = new ProjectWorkspace
        {
            Sources = [new ProjectDesignSource { Id = "capture-source", Kind = DesignSourceKind.Revit }],
        };
        AlbumDefinition album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар");

        Assert.NotNull(ProjectPackageReconciliationService.Apply(project, album, library, loaded));

        AlbumPageDefinition page = Assert.Single(album.Pages);
        Assert.Equal("Огтлол", page.SourceContentKindSnapshot);
    }
}
