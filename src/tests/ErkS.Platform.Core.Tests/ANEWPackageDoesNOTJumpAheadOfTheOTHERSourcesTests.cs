using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Эмээлт (MAD-2026-ЕТ/03), 2026-11-19: six Noorog boards came into an urban-planning album one
/// after another, and the album's buildings came out in the REVERSE of their arrival - the board
/// sent last stood first, and a board sent again jumped to the front. Reconciling one source's
/// package orders that source's own pages by its package; it must not move them ahead of every
/// other source's pages.
/// </summary>
public sealed class ANEWPackageDoesNOTJumpAheadOfTheOTHERSourcesTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(), "erks-urban-order-" + Guid.NewGuid().ToString("N"));

    public ANEWPackageDoesNOTJumpAheadOfTheOTHERSourcesTests() => Directory.CreateDirectory(workDirectory);

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
    public void ASOURCEThatArrivesLATERComesAFTERTheOnesAlreadyThere()
    {
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library) = UrbanProject();

        Reconcile(project, album, library, "first", "p1", ["F-1", "F-2"]);
        Reconcile(project, album, library, "second", "p1", ["S-1", "S-2"]);
        Reconcile(project, album, library, "third", "p1", ["T-1"]);

        // MEASURED BEFORE THE FIX: [T-1 S-1 S-2 F-1 F-2] - newest first.
        Assert.Equal(["F-1", "F-2", "S-1", "S-2", "T-1"], Order(album));
    }

    [Fact]
    public void ASOURCESentAGAINKeepsItsPlace()
    {
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library) = UrbanProject();
        Reconcile(project, album, library, "first", "p1", ["F-1", "F-2"]);
        Reconcile(project, album, library, "second", "p1", ["S-1", "S-2"]);
        Reconcile(project, album, library, "third", "p1", ["T-1"]);

        // The middle board again, its sheets now in a new order, one new and one gone.
        Reconcile(project, album, library, "second", "p2", ["S-3", "S-1"]);

        Assert.Equal(["F-1", "F-2", "S-3", "S-1", "T-1"], Order(album));
    }

    [Fact]
    public void ASOURCEWhoseSheetsAreALLReplacedKeepsItsPlaceToo()
    {
        // Ноорог renumbered a board: every sheet id changed, so none of the old pages survive
        // to hold the place - the place is the source's, not its pages'.
        (ProjectWorkspace project, AlbumDefinition album, SheetLibrary library) = UrbanProject();
        Reconcile(project, album, library, "first", "p1", ["F-1", "F-2"]);
        Reconcile(project, album, library, "second", "p1", ["S-1"]);

        Reconcile(project, album, library, "first", "p2", ["G-1", "G-2", "G-3"]);

        Assert.Equal(["G-1", "G-2", "G-3", "S-1"], Order(album));
    }

    private IReadOnlyList<string> Order(AlbumDefinition album) =>
        album.Pages
            .Select(page => numbers.TryGetValue(page.SheetKey, out string? number) ? number : page.SheetKey)
            .ToList();

    private readonly Dictionary<string, string> numbers = new(StringComparer.Ordinal);

    private static (ProjectWorkspace, AlbumDefinition, SheetLibrary) UrbanProject()
    {
        var project = new ProjectWorkspace();
        project.Identity.ProjectType = "urban-planning";
        project.Identity.StageCode = "development-project";
        Assert.True(UrbanPlanningAlbumTemplate.Supports(project.Identity.ProjectType, project.Identity.StageCode));
        AlbumDefinition album = UrbanPlanningAlbumTemplate.CreateDefinition(project.Identity.StageCode);
        album.Pages.Clear();
        return (project, album, new SheetLibrary());
    }

    private void Reconcile(
        ProjectWorkspace project,
        AlbumDefinition album,
        SheetLibrary library,
        string sourceName,
        string packageName,
        string[] sheetIds)
    {
        string sourceId = (sourceName + "00000000000000000000000000000000")[..32];
        if (project.Sources.All(source => source.Id != sourceId))
        {
            project.Sources.Add(new ProjectDesignSource
            {
                Id = sourceId,
                Name = sourceName,
                Kind = DesignSourceKind.Folder,
            });
        }

        string folder = Path.Combine(workDirectory, sourceName + "-" + packageName);
        Directory.CreateDirectory(folder);
        var entries = new List<SheetPackageEntry>();
        foreach (string sheetId in sheetIds)
        {
            string pdfPath = Path.Combine(folder, sheetId + ".pdf");
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
                SheetId = "sheet-" + sheetId,
                Number = sheetId,
                Name = sheetId,
                BuildingName = sourceName,
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
                SourceId = sourceId,
                Application = SheetSourceApplication.Manual,
            },
            Sheets = entries,
        };
        SheetPackageLoadResult loaded = SheetPackageReader.Load(SheetPackageWriter.Write(manifest, folder, packageName));
        library.Absorb(loaded);
        foreach (SheetRecord record in library.Snapshot())
            numbers[record.Key] = record.Entry.Number;

        Assert.NotNull(ProjectPackageReconciliationService.Apply(project, album, library, loaded));
    }
}
