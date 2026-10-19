using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// General-plan (ЕТ) sheets in a working-drawing album.
///
/// 🔴 THE OWNER'S RULE, 2026-10-20: «барилга 1 ч биш ерөнхий төлөвлөгөө бол
/// ерөнхий төлөвлөгөө гэдэг бүлэгтэй байх ёстой». Batnasan's ЕТ DWG had been
/// added as a building source in August; every ЕТ sheet was filed under
/// «Барилга 1», printed in its «Ажлын зураг» part, and demanded a building
/// sub-cover that album never draws. The sheet itself says what it is - its
/// mark is ЕТ - and that now decides, whatever the source was once called.
/// </summary>
public sealed class AGENERALPlanIsNEVERFiledUnderABuildingTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-platform-tests",
        Guid.NewGuid().ToString("N"));

    public AGENERALPlanIsNEVERFiledUnderABuildingTests()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(workDirectory);
    }

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
    public void TheWorkingDrawingAlbumHasAGeneralPlanPartBetweenTheGeneralPartAndTheSets()
    {
        AlbumDefinition album = BuildingWorkingDrawingAlbumTemplate.CreateDefinition("");

        Assert.Equal(
            [
                BuildingWorkingDrawingAlbumTemplate.GeneralPartSectionTitle,
                BuildingWorkingDrawingAlbumTemplate.GeneralPlanSectionTitle,
                BuildingWorkingDrawingAlbumTemplate.SheetsSectionTitle,
            ],
            album.Sections.Select(section => section.Title));
    }

    [Fact]
    public void AnOlderAlbumGainsThePartsInOrderAndKeepsItsOwn()
    {
        var album = BuildingWorkingDrawingAlbumTemplate.CreateDefinition("");
        AlbumSection sheets = album.Sections.Single(section =>
            section.Title == BuildingWorkingDrawingAlbumTemplate.SheetsSectionTitle);
        album.Sections.Clear();
        album.Sections.Add(sheets);
        foreach (AlbumCompositionItem item in album.Composition)
            item.SectionTitle = BuildingWorkingDrawingAlbumTemplate.SheetsSectionTitle;

        bool changed = BuildingWorkingDrawingAlbumTemplate.EnsureSections(album);

        Assert.True(changed);
        Assert.Equal(3, album.Sections.Count);
        Assert.Same(sheets, album.Sections[2]);
        Assert.Equal(
            BuildingWorkingDrawingAlbumTemplate.GeneralPartSectionTitle,
            album.Composition.Single(item => item.Id == WorkingDrawingFrontMatterSlot.SlotId).SectionTitle);
        Assert.False(BuildingWorkingDrawingAlbumTemplate.EnsureSections(album));
    }

    [Fact]
    public void AnAllETPackageCorrectsASourceOnceCalledABuilding()
    {
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library, SheetPackageLoadResult loaded) =
            Batnasan(["ЕТ", "ЕТ"]);

        Assert.NotNull(ProjectPackageReconciliationService.Apply(project, album, library, loaded));

        ProjectDesignSource source = project.Sources.Single();
        Assert.Equal(ProjectDesignSourcePurpose.GeneralPlan, ProjectDesignSourceClassification.ExplicitPurpose(source));
        Assert.Equal("", ProjectDesignSourceClassification.BuildingGroupId(source));
        Assert.True(source.Metadata!.ContainsKey(ProjectDesignSourceClassification.PurposeCorrectedKey));
    }

    [Fact]
    public void ETSheetsLoseTheirBuildingAndStandInTheGeneralPlanPart()
    {
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library, SheetPackageLoadResult loaded) =
            Batnasan(["ЕТ", "ЕТ"]);
        foreach (SheetRecord record in library.Snapshot())
            project.SheetBuildingAssignments[record.Key] = "building-1";

        ProjectPackageReconciliationService.Apply(project, album, library, loaded);

        Assert.Empty(project.SheetBuildingAssignments);
        Guid generalPlan = album.Sections.Single(section =>
            section.Title == BuildingWorkingDrawingAlbumTemplate.GeneralPlanSectionTitle).Id;
        Assert.Equal(2, album.Pages.Count);
        Assert.All(album.Pages, page => Assert.Equal(generalPlan, page.SectionId));
    }

    [Fact]
    public void AMixedPackageKeepsWhatItWasCalledButItsETSheetsStillGoToTheGeneralPlan()
    {
        // The positive control: only an unambiguous package corrects the
        // source. One ЕТ sheet among a building's sheets is filed by itself.
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library, SheetPackageLoadResult loaded) =
            Batnasan(["ЕТ", "БА"]);

        ProjectPackageReconciliationService.Apply(project, album, library, loaded);

        ProjectDesignSource source = project.Sources.Single();
        Assert.Equal(ProjectDesignSourcePurpose.Building, ProjectDesignSourceClassification.ExplicitPurpose(source));
        Guid generalPlan = album.Sections.Single(section =>
            section.Title == BuildingWorkingDrawingAlbumTemplate.GeneralPlanSectionTitle).Id;
        Guid sheets = album.Sections.Single(section =>
            section.Title == BuildingWorkingDrawingAlbumTemplate.SheetsSectionTitle).Id;
        Assert.Equal(
            [generalPlan, sheets],
            album.Pages
                .OrderBy(page => page.SheetKey, StringComparer.Ordinal)
                .Select(page => page.SectionId!.Value));
        Assert.DoesNotContain(
            project.SheetBuildingAssignments.Keys,
            key => key.Contains("-01", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGeneralPlanIsNumberedAfterTheGeneralPartNotWithItsOwnNumbers()
    {
        // 🔴 BATNASAN, 2026-10-20: the ЕТ DWG numbers its layouts 01-04, so the
        // album printed 00 cover, 01 list - and then 01, 02, 03, 04 again. The
        // general plan follows the general part: 02, 03 … (the owner's
        // reference keeps 02-10 for ЕТ, the building's sets from 11).
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library, SheetPackageLoadResult loaded) =
            Batnasan(["ЕТ", "ЕТ", "БА"]);
        ProjectPackageReconciliationService.Apply(project, album, library, loaded);
        var albumProject = new AlbumProject { Name = "BN", Album = album };

        AlbumBuildRequest request = AlbumBuilder.CreateRequest(albumProject, library);

        List<AlbumBuildPage> pages = request.Sections.SelectMany(section => section.Pages).ToList();
        Assert.Equal(
            ["02", "03"],
            pages.Where(page => page.Sheet.Entry.Discipline == "ЕТ").Select(page => page.Number));
        Assert.Equal("03", pages.Single(page => page.Sheet.Entry.Discipline == "БА").Number);
    }

    [Fact]
    public void TheOrganizerShowsTheThreePartsWithThePrintedNumbers()
    {
        // The organizer must show the working drawings as they print: the
        // general part (Studio's pages first), the general plan, the sets of
        // the building - not the concept tree with its own numbering.
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library, SheetPackageLoadResult loaded) =
            Batnasan(["ЕТ", "ЕТ", "БА"]);
        ProjectPackageReconciliationService.Apply(project, album, library, loaded);
        var albumProject = new AlbumProject { Name = "BN", Album = album };

        IReadOnlyList<WorkingDrawingOutlineSection>? outline = WorkingDrawingAlbumOutline.Create(albumProject, library);

        Assert.NotNull(outline);
        Assert.Equal(
            [
                BuildingWorkingDrawingAlbumTemplate.GeneralPartSectionTitle,
                BuildingWorkingDrawingAlbumTemplate.GeneralPlanSectionTitle,
                BuildingWorkingDrawingAlbumTemplate.SheetsSectionTitle,
            ],
            outline!.Select(section => section.Title));
        IReadOnlyList<ConceptGeneratedPagePlan> generated = BuildingArchitectureConceptGeneratedPagePlanner.Create(albumProject);
        Assert.Equal(
            generated.Select(plan => plan.Number),
            outline[0].Pages.Select(page => page.Number));
        Assert.All(outline[0].Pages, page => Assert.NotNull(page.Generated));
        Assert.Equal(["02", "03"], outline[1].Pages.Select(page => page.Number));
        AlbumBuildRequest request = AlbumBuilder.CreateRequest(albumProject, library);
        Assert.Equal(
            request.Sections.SelectMany(section => section.Pages).Select(page => page.Number),
            outline.SelectMany(section => section.Pages).Where(page => page.Page is not null).Select(page => page.Number));
    }

    [Fact]
    public void AConceptAlbumHasNoWorkingDrawingOutline()
    {
        var albumProject = new AlbumProject
        {
            Name = "concept",
            Album = new AlbumDefinition { TemplateId = BuildingArchitectureConceptAlbumTemplate.TemplateId },
        };

        Assert.Null(WorkingDrawingAlbumOutline.Create(albumProject, new SheetLibrary()));
    }

    [Fact]
    public void AnOpenedAlbumIsGivenItsPartsByTheResolver()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ErkS.Platform.Core")))
            directory = directory.Parent;
        string resolver = File.ReadAllText(Path.Combine(
            directory!.FullName, "src", "ErkS.Platform.Core", "ProjectAlbumTemplateResolver.cs"));

        Assert.Contains("BuildingWorkingDrawingAlbumTemplate.EnsureSections(album.Definition)", resolver, StringComparison.Ordinal);
    }

    private (ProjectWorkspace, AlbumDefinition, SheetLibrary, SheetPackageLoadResult) Batnasan(string[] marks)
    {
        var entries = new List<SheetPackageEntry>();
        for (int index = 0; index < marks.Length; index++)
        {
            string number = (index + 1).ToString("D2");
            string pdfPath = Path.Combine(workDirectory, "bn-" + number + ".pdf");
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
                SheetId = "bn-" + number,
                Number = number,
                Name = "ХУУДАС " + number,
                Discipline = marks[index],
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
            PackageScope = SheetPackageScope.FullSnapshot,
            Source = new SheetPackageSource
            {
                SourceId = "bn-autocad",
                Application = SheetSourceApplication.AutoCad,
            },
            Sheets = entries,
        };
        string manifestPath = SheetPackageWriter.Write(manifest, workDirectory, "bn");
        SheetPackageLoadResult loaded = SheetPackageReader.Load(manifestPath);
        var library = new SheetLibrary();
        library.Absorb(loaded);

        var source = new ProjectDesignSource { Id = "bn-autocad", Kind = DesignSourceKind.AutoCad };
        ProjectDesignSourceClassification.SetExplicitPurpose(source, ProjectDesignSourcePurpose.Building, "building-1");
        var project = new ProjectWorkspace
        {
            Sources = [source],
            BuildingGroups = [new ProjectBuildingGroup { Id = "building-1", Name = "Барилга 1", Order = 1 }],
        };
        project.Identity.ProjectType = ProjectTypes.BuildingDesignProjectType.TypeId;
        project.Identity.StageCode = "working-drawings";
        return (project, BuildingWorkingDrawingAlbumTemplate.CreateDefinition(""), library, loaded);
    }
}

/// <summary>
/// A working-drawing album never draws a building sub-cover, so a change of
/// the building composition must not leave one waiting to be sent forever
/// (Батнасан, 2026-10-20: a sub-cover code stayed pending after every Sync).
/// One the shared album still holds stays pending - it has to be removed.
/// </summary>
public sealed class ANALBUMWithoutSubCoversNeverWaitsForOneTests
{
    private static ProjectWorkspace Project(params string[] cloudCodes)
    {
        var project = new ProjectWorkspace();
        project.BuildingGroups.Add(new ProjectBuildingGroup { Id = "b1", Name = "Барилга", Order = 1 });
        project.Cloud.SharedAlbumComponents = cloudCodes
            .Select(code => new ProjectCloudAlbumComponentReference { Code = code })
            .ToList();
        return project;
    }

    [Fact]
    public void TheWorkingDrawingsMarkNoSubCoverTheyNeverDraw()
    {
        ProjectWorkspace project = Project();
        string code = ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(project.BuildingGroups[0]);
        project.Cloud.PendingAlbumComponentCodes = [code];

        ProjectCloudSyncMetadata.MarkBuildingCompositionPending(
            project,
            BuildingWorkingDrawingAlbumTemplate.CreateDefinition("BN"));

        Assert.True(project.Cloud.BuildingCompositionPending);
        Assert.DoesNotContain(code, ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    [Fact]
    public void OneTheSharedAlbumStillHoldsWaitsToBeRemoved()
    {
        string stale = ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix + "studio-building:old";
        ProjectWorkspace project = Project(stale);

        ProjectCloudSyncMetadata.MarkBuildingCompositionPending(
            project,
            BuildingWorkingDrawingAlbumTemplate.CreateDefinition("BN"));

        Assert.Equal([stale], ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    [Fact]
    public void AConceptAlbumStillMarksItsSubCovers()
    {
        ProjectWorkspace project = Project();

        ProjectCloudSyncMetadata.MarkBuildingCompositionPending(project);

        Assert.Contains(
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(project.BuildingGroups[0]),
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }
}
