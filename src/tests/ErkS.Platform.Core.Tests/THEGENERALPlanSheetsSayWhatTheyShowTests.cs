using System.Text.Json;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Erin's general-plan sheets arrived with every information band empty: no
/// legend, no ТЭЗ, no building schedule, no basis (owner's task, 2026-10-19,
/// with CGA - _shared/erin-et-stu-sheet-plan-2026-10-19.md). The information
/// now travels as `erks-sheet-info`; these are what Studio does with it.
///
/// Laid out as data, tested without rendering - the defect being fixed was
/// a band that drew and said nothing.
/// </summary>
public sealed class THEGENERALPlanSheetsSayWhatTheyShowTests
{
    // ---- the legend ----------------------------------------------------

    [Fact]
    public void EveryLegendRowIsPlacedWithItsQuantity()
    {
        SheetInfoDocument info = Sample();
        IReadOnlyList<SheetInfoLegendRow> rows = info.Legends["master-plan"];

        GeneralPlanLegendLayout layout = GeneralPlanSheetInfoLayout.Legend(rows, widthMm: 230, heightMm: 28);

        Assert.Equal(rows.Count, layout.Items.Count);
        Assert.Equal(0, layout.Overflow);
        GeneralPlanLegendItem planned = layout.Items.Single(item => item.Row.Key == "building.planned");
        Assert.Equal("25 237.9 м²", planned.QuantityText);
        foreach (GeneralPlanLegendItem item in layout.Items)
        {
            Assert.InRange(item.XMm, 0, 230);
            Assert.InRange(item.XMm + item.WidthMm, 0, 230.001);
            Assert.InRange(item.YMm, GeneralPlanSheetInfoLayout.TitleHeightMm - 0.001, 28);
            Assert.InRange(item.YMm + GeneralPlanSheetInfoLayout.RowHeightMm, 0, 28.001);
        }
    }

    [Fact]
    public void RowsThatDoNotFitAreCOUNTEDNotDropped()
    {
        IReadOnlyList<SheetInfoLegendRow> rows = Sample().Legends["master-plan"];

        GeneralPlanLegendLayout layout = GeneralPlanSheetInfoLayout.Legend(rows, widthMm: 70, heightMm: 12);

        Assert.True(layout.Overflow > 0);
        Assert.Equal(rows.Count, layout.Items.Count + layout.Overflow);
    }

    [Fact]
    public void ARowWithoutAQuantityPrintsNoNumber()
    {
        var row = new SheetInfoLegendRow { Key = "x", Label = "Хил", Sample = new() { Kind = "line", Color = "#FF0000" } };

        GeneralPlanLegendItem item = Assert.Single(
            GeneralPlanSheetInfoLayout.Legend([row], widthMm: 100, heightMm: 20).Items);

        Assert.Equal("", item.QuantityText);
    }

    // ---- ТЭЗ, schedule, basis -----------------------------------------

    [Fact]
    public void TheSummaryCarriesEveryRowWithItsUnit()
    {
        IReadOnlyList<GeneralPlanTextRow> rows = GeneralPlanSheetInfoLayout.SummaryRows(Sample().Summary);

        Assert.Contains(rows, row => row.Label == "Барилгажих талбай" && row.Value == "25 237.9 м²");
        Assert.Contains(rows, row => row.Label == "Барилгажилтын нягтшил (FAR)" && row.Value == "2.72");
        Assert.Contains(rows, row => row.Label == "Ил авто зогсоол" && row.Value == "400 байр");
    }

    [Fact]
    public void AGroupOfBuildingsSaysHowMany()
    {
        IReadOnlyList<GeneralPlanScheduleRow> rows = GeneralPlanSheetInfoLayout.ScheduleRows(Sample().Buildings);

        GeneralPlanScheduleRow garages = rows.Single(row => row.Name.StartsWith("Газар доорх гараж", StringComparison.Ordinal));
        Assert.Equal("", garages.Number);
        Assert.Equal("14", garages.Count);
        Assert.Equal("Төлөвлөж буй", garages.Stage);
        Assert.Contains(rows, row => row.Number == "055" && row.Floors == "12/-1");
    }

    [Fact]
    public void TheScheduleShrinksToShowEveryBuildingButNotBeyondReading()
    {
        // Erin's 17 rows in the 47 mm column: all of them, at under 3.4 mm.
        Assert.Equal(17, GeneralPlanSheetInfoLayout.ScheduleCapacity(47, 17));
        Assert.Equal(GeneralPlanSheetInfoLayout.ScheduleRowHeightMaxMm, GeneralPlanSheetInfoLayout.ScheduleRowHeight(47, 3));
        // Forty cannot be read at any size that fits; the floor holds and the rest are counted.
        Assert.Equal(GeneralPlanSheetInfoLayout.ScheduleRowHeightMinMm, GeneralPlanSheetInfoLayout.ScheduleRowHeight(47, 40));
        Assert.True(GeneralPlanSheetInfoLayout.ScheduleCapacity(47, 40) < 40);
    }

    [Fact]
    public void BasisLinesComeFromStudioFirstThenTheDocumentThenAPlaceholder()
    {
        var project = new AlbumProject();
        project.PlanningTask.AtdNumber = "АТД-17/2026";

        IReadOnlyList<string> lines = GeneralPlanSheetInfoLayout.BasisLines(project, Sample());

        // Studio's own fact wins over the document's null.
        Assert.Contains(lines, line => line.Contains("АТД-17/2026", StringComparison.Ordinal));
        // The document's own text when Studio has nothing for the key.
        Assert.Contains(lines, line => line.Contains("БНбД 30-01-04", StringComparison.Ordinal));
        // Nothing anywhere: a placeholder that reads as a form field, never an empty line.
        Assert.Contains(lines, line => line.Contains("____", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, string.IsNullOrWhiteSpace);
    }

    // ---- the plumbing -------------------------------------------------

    [Fact]
    public void TheInfoReachesTheLibrarysRecords()
    {
        using var package = new SheetInfoPackageFixture();

        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(package.ManifestPath));

        SheetRecord record = Assert.Single(library.VerifiedSnapshot());
        Assert.Equal("Erin_MP_ZZ_VL.dwg", record.SheetInfo!.Document);
    }

    [Fact]
    public void TheWriterACTUALLYDrawsTheBands()
    {
        // The bands are painted from three call sites in the writer; each was
        // rendered and looked at on Erin's sheets. Without them every rule above
        // holds and the sheet still prints its bands empty - the defect itself.
        string writer = WriterSource("PdfSharpAlbumWriter.cs");

        Assert.Contains("DrawGeneralPlanStrip(gfx, buildPage, regions, borderPen, finePen);", writer, StringComparison.Ordinal);
        Assert.Contains("if (DrawMasterPlanHeaderColumns(gfx, project, buildPage, xApproval, x1, y0, y1, borderPen))", writer, StringComparison.Ordinal);
        Assert.Contains("warnings.AddRange(GeneralPlanInformationWarnings(buildPage));", writer, StringComparison.Ordinal);
    }

    private static string WriterSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Platform.Pdf", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }

    private static SheetInfoDocument Sample() =>
        SheetInfoReader.Load(SharedContractCopies.PathTo(SharedContractCopies.ErinSheetInfoSample)).Document!;
}

/// <summary>A one-sheet master-plan package carrying CGA's sample info.</summary>
internal sealed class SheetInfoPackageFixture : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public SheetInfoPackageFixture()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(directory);
        const string fileName = "ET-08.pdf";
        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            PdfSharp.Pdf.PdfPage page = document.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromMillimeter(582);
            page.Height = PdfSharp.Drawing.XUnit.FromMillimeter(420);
            using (var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                gfx.DrawString("ЕТ", new PdfSharp.Drawing.XFont("Arial", 12), PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(40, 40));
            document.Save(Path.Combine(directory, fileName));
        }

        const string infoName = "Erin_MP_ZZ_VL.erks-sheet-info.json";
        string infoPath = Path.Combine(directory, infoName);
        File.Copy(SharedContractCopies.PathTo(SharedContractCopies.ErinSheetInfoSample), infoPath);
        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.FirstSheetInfoSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = "erin-cga",
                Application = SheetSourceApplication.AutoCad,
                DocumentPath = @"D:\Cloud Work\Nanuto LLC\Erin Apartment\ZZ\DWG\Erin_MP_ZZ_VL.dwg",
            },
            SheetInfo = new SheetInfoReference
            {
                File = infoName,
                Sha256 = SheetPackageReader.ComputeSha256(infoPath),
                SchemaVersion = 1,
            },
            Sheets =
            {
                new SheetPackageEntry
                {
                    SheetId = "et-08",
                    Number = "08",
                    Name = "ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ",
                    TemplateSlotId = "master-plan",
                    WidthMm = 582,
                    HeightMm = 420,
                    PdfFileName = fileName,
                    PdfPageNumber = 1,
                },
            },
        };
        ManifestPath = SheetPackageWriter.Write(manifest, directory, "erin");
    }

    public string ManifestPath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }
}
