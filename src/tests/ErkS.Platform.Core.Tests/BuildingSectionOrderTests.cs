using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Does correcting a sheet's building assignment actually move its section?
///
/// The user's report of 2026-09-07: they had assigned sheets to the wrong
/// building types, corrected them, pressed rebuild, and the sections with
/// building sub-covers still did not come out in the right order.
///
/// The ordering rule had no direct test of its own, which is part of why this
/// could survive: the sequencer decides the whole structure of the album and
/// nothing asked it a question.
/// </summary>
public sealed class BuildingSectionOrderTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-building-order-" + Guid.NewGuid().ToString("N"));

    public BuildingSectionOrderTests() => Directory.CreateDirectory(workDirectory);

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
    public void CORRECTINGAnAssignmentMovesTheSectionWithIt()
    {
        // Two buildings, one sheet each, and the groups ordered 1 then 2. The
        // album must follow the GROUPS, not the order the sheets happened to
        // arrive in.
        (AlbumProject project, SheetLibrary library, string firstKey, string secondKey) =
            CreateTwoBuildingProject();

        AlbumBuildRequest asAssigned = AlbumBuilder.CreateRequest(project, library);
        Assert.Equal(
            ["Барилга А", "Барилга Б"],
            BuildingSectionTitles(asAssigned));

        // 🔴 THE CORRECTION THE USER MADE. The two sheets were on the wrong
        // buildings; swapping them must swap the sections, because the section a
        // sheet belongs to IS its assignment.
        project.SheetBuildingAssignments[firstKey] = project.BuildingGroups[1].Id;
        project.SheetBuildingAssignments[secondKey] = project.BuildingGroups[0].Id;

        AlbumBuildRequest afterCorrection = AlbumBuilder.CreateRequest(project, library);

        // The titles stay in group order - what changes is WHICH SHEET is under
        // each of them. That is the observable the user was looking at.
        Assert.Equal(
            ["Барилга А", "Барилга Б"],
            BuildingSectionTitles(afterCorrection));
        Assert.Equal(
            SheetNumbersOf(asAssigned, "Барилга Б"),
            SheetNumbersOf(afterCorrection, "Барилга А"));
        Assert.Equal(
            SheetNumbersOf(asAssigned, "Барилга А"),
            SheetNumbersOf(afterCorrection, "Барилга Б"));
    }

    [Fact]
    public void REORDERINGTheGROUPSReordersTheSections()
    {
        // The other half: the group's own Order is what places its section, so
        // changing it must move the section - sub-cover and pages together.
        (AlbumProject project, SheetLibrary library, _, _) = CreateTwoBuildingProject();

        Assert.Equal(["Барилга А", "Барилга Б"], BuildingSectionTitles(
            AlbumBuilder.CreateRequest(project, library)));

        project.BuildingGroups[0].Order = 2;
        project.BuildingGroups[1].Order = 1;

        Assert.Equal(["Барилга Б", "Барилга А"], BuildingSectionTitles(
            AlbumBuilder.CreateRequest(project, library)));
    }

    [Fact]
    public void EACHBuildingSectionIsONERunSoItGetsONESubCover()
    {
        // The sub-cover is drawn once per building section, immediately before
        // its pages. Sections are formed from CONSECUTIVE items sharing a key,
        // so a building whose pages come out split into two blocks would be
        // drawn as two sections - and would print its sub-cover twice.
        (AlbumProject project, SheetLibrary library, _, _) = CreateTwoBuildingProject();

        AlbumBuildRequest request = AlbumBuilder.CreateRequest(project, library);
        IReadOnlyList<string> buildingKeys = request.Sections
            .Where(section => section.Kind == AlbumBuildSectionKind.Building)
            .Select(section => section.Key)
            .ToList();

        Assert.Equal(buildingKeys.Count, buildingKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(
            request.Sections.Where(section => section.Kind == AlbumBuildSectionKind.Building),
            section => Assert.NotEmpty(section.Pages));
    }

    private static IReadOnlyList<string> BuildingSectionTitles(AlbumBuildRequest request) =>
        request.Sections
            .Where(section => section.Kind == AlbumBuildSectionKind.Building)
            .Select(section => section.Title)
            .ToList();

    private static IReadOnlyList<string> SheetNumbersOf(AlbumBuildRequest request, string title) =>
        request.Sections
            .Where(section => section.Title.Equals(title, StringComparison.Ordinal))
            .SelectMany(section => section.Pages.Select(page => page.Sheet.Entry.Number))
            .ToList();

    private (AlbumProject Project, SheetLibrary Library, string FirstKey, string SecondKey)
        CreateTwoBuildingProject()
    {
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(CreatePackage("building-a", "A-01")));
        library.Absorb(SheetPackageReader.Load(CreatePackage("building-b", "B-01")));

        IReadOnlyList<SheetRecord> records = library.Snapshot()
            .OrderBy(record => record.Entry.Number, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(2, records.Count);

        var project = new AlbumProject
        {
            Name = "Хоёр барилга",
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар"),
        };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;

        project.BuildingGroups.Add(new ProjectBuildingGroup { Name = "Барилга А", Order = 1 });
        project.BuildingGroups.Add(new ProjectBuildingGroup { Name = "Барилга Б", Order = 2 });

        foreach (SheetRecord record in records)
        {
            project.DesignSources.Add(new ProjectDesignSource
            {
                Id = record.SourceId,
                Name = record.Entry.Number,
                Kind = DesignSourceKind.Revit,
            });
            project.Album.Pages.Add(new AlbumPageDefinition
            {
                SheetKey = record.Key,
                PageFormatId = PageFormatCatalog.SourceAsIsId,
                PlacementMode = PagePlacementMode.FullPage,
            });
        }

        project.SheetBuildingAssignments[records[0].Key] = project.BuildingGroups[0].Id;
        project.SheetBuildingAssignments[records[1].Key] = project.BuildingGroups[1].Id;

        return (project, library, records[0].Key, records[1].Key);
    }

    private string CreatePackage(string name, string sheetNumber)
    {
        string pdfPath = Path.Combine(workDirectory, name + ".pdf");
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(297);
            using (XGraphics graphics = XGraphics.FromPdfPage(page))
                graphics.DrawRectangle(XPens.Black, 20, 20, 100, 100);
            document.Save(pdfPath);
        }

        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = name + "-source",
                Application = SheetSourceApplication.Revit,
            },
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = name + "-sheet",
                    Number = sheetNumber,
                    Name = name,
                    WidthMm = 210,
                    HeightMm = 297,
                    ContentWidthMm = 210,
                    ContentHeightMm = 297,
                    PdfFileName = Path.GetFileName(pdfPath),
                    PageCount = 1,
                },
            ],
        };

        return SheetPackageWriter.Write(manifest, workDirectory, name);
    }
}
