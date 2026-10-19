using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The «Зөвшилцсөн» block on a working sheet: the section engineers who
/// consulted on it (ББ, ХАС, ЦБУ, ХТ, ХД, ДГ …), mark | name | signature.
///
/// It is NOT the concept cover's ЗӨВШИЛЦСӨН (the bodies that concur with the
/// concept) and not its ЗӨВШӨӨРӨЛЦСӨН (officials). The owner decided on
/// 2026-10-19 that working drawings carry it as a list of their own. The old
/// field `WorkingDrawingConsultedBy` is deliberately emptied into the concept
/// roster on every load, so giving it this new meaning would have carried any
/// agency rows an old file still held into a table of engineers.
///
/// Revit reserves an empty frame for it (PFR 5f2c7f4) and says where in the
/// manifest; Studio fills the frame.
/// </summary>
public sealed class THESECTIONConsentTableFillsTheReservedFrameTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-platform-tests",
        Guid.NewGuid().ToString("N"));

    public THESECTIONConsentTableFillsTheReservedFrameTests()
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

    // ---- the manifest ---------------------------------------------------

    [Fact]
    public void TheReservedFrameTravelsAndVerifies()
    {
        string manifestPath = WritePackage("frame");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        SheetPackageEntry entry = Assert.Single(result.Manifest!.Sheets);
        Assert.Equal(6, entry.ConsultationTableRows);
        Assert.Equal((230d, 216d, 180d, 35d), Rect(entry.ConsultationTableArea!));
    }

    [Fact]
    public void AFrameWithNoRowsIsRefused()
    {
        string manifestPath = WritePackage("no-rows");
        RewriteEntry(manifestPath, entry => entry["consultationTableRows"] = 0);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("consultation", StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains("row", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RowsWithNoFrameAreRefused()
    {
        string manifestPath = WritePackage("no-frame");
        RewriteEntry(manifestPath, entry => entry.Remove("consultationTableArea"));

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("consultation", StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains("area", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFrameOffThePageIsRefused()
    {
        string manifestPath = WritePackage("off-page");
        RewriteEntry(manifestPath, entry => entry["consultationTableArea"]!["x"] = 400);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("consultation", StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains("page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFrameIsRefusedBelowSchema7()
    {
        string manifestPath = WritePackage("old-schema");
        RewriteManifest(manifestPath, manifest => manifest["schemaVersion"] = 6);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("consultation", StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains("7", StringComparison.Ordinal));
    }

    [Fact]
    public void ASheetWithoutTheTableWritesNothingNew()
    {
        // Absent means absent: a producer that never heard of the table must
        // write the same manifest it always did.
        var entry = new SheetPackageEntry();
        string json = System.Text.Json.JsonSerializer.Serialize(entry, SheetPackageJson.Options);

        Assert.DoesNotContain("consultation", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the project list ----------------------------------------------

    [Fact]
    public void TheSectionListSurvivesNormalizeAndIsNEVERMovedToTheConcept()
    {
        var workflow = new ProjectApprovalWorkflow();
        workflow.WorkingDrawingSectionConsent.Add(new WorkingDrawingSectionConsentEntry
        {
            SectionMark = " ББ ",
            PersonName = " А.Буянжаргал ",
        });

        workflow.Normalize();

        WorkingDrawingSectionConsentEntry entry = Assert.Single(workflow.WorkingDrawingSectionConsent);
        Assert.Equal("ББ", entry.SectionMark);
        Assert.Equal("А.Буянжаргал", entry.PersonName);
        Assert.Empty(workflow.ConceptDesign.ConcurredBy);
    }

    [Fact]
    public void TheOldFieldStillMovesToTheConceptAndNotIntoTheNewList()
    {
        // The positive control for the old one-way move: rows written under the
        // old name are agencies, and they must not appear among the engineers.
        var workflow = new ProjectApprovalWorkflow();
        workflow.WorkingDrawingConsultedBy.Add(new ProjectApprovalEntry { OrganizationName = "Онцгой байдал" });

        workflow.Normalize();

        Assert.Single(workflow.ConceptDesign.ConcurredBy);
        Assert.Empty(workflow.WorkingDrawingSectionConsent);
    }

    [Fact]
    public void CloneCarriesTheSectionList()
    {
        var workflow = new ProjectApprovalWorkflow();
        workflow.WorkingDrawingSectionConsent.Add(new WorkingDrawingSectionConsentEntry
        {
            SectionMark = "ХАС",
            PersonName = "Э.Ариунцэцэг",
        });

        ProjectApprovalWorkflow copy = workflow.Clone();

        Assert.Equal("Э.Ариунцэцэг", Assert.Single(copy.WorkingDrawingSectionConsent).PersonName);
        Assert.NotSame(workflow.WorkingDrawingSectionConsent[0], copy.WorkingDrawingSectionConsent[0]);
    }

    // ---- the layout ----------------------------------------------------

    [Fact]
    public void EachRowCarriesAMarkAndAName()
    {
        IReadOnlyList<WorkingTitleBlockCell> cells = ConsultationTableLayout.Cells(
            widthMm: 180, heightMm: 35, rows: 6, Sections());

        string[] texts = cells.Select(cell => cell.Text).ToArray();
        Assert.Contains("ББ", texts);
        Assert.Contains("А.Буянжаргал", texts);
        Assert.Contains("ХТ", texts);
        Assert.Contains("Ө.Батбаяр", texts);
        Assert.Contains(ConsultationTableLayout.Title, texts);
    }

    [Fact]
    public void NamesBeyondTheReservedRowsAreReportedNotDropped()
    {
        // Revit decided the height. If the project lists more engineers than the
        // frame has rows, the extra ones cannot be drawn - and the build must say
        // so instead of printing a shorter list that looks complete.
        IReadOnlyList<WorkingDrawingSectionConsentEntry> sections = Sections();

        Assert.Equal(2, ConsultationTableLayout.Overflow(rows: 4, sections));
        Assert.Equal(0, ConsultationTableLayout.Overflow(rows: 6, sections));
        Assert.Equal(0, ConsultationTableLayout.Overflow(rows: 8, sections));
    }

    [Fact]
    public void EveryCellStaysInsideTheFrame()
    {
        foreach (WorkingTitleBlockCell cell in ConsultationTableLayout.Cells(180, 35, 6, Sections()))
        {
            Assert.InRange(cell.XMm, 0, 180);
            Assert.InRange(cell.YMm, 0, 35);
            Assert.InRange(cell.XMm + cell.WidthMm, 0, 180.001);
            Assert.InRange(cell.YMm + cell.HeightMm, 0, 35.001);
        }
    }

    private static IReadOnlyList<WorkingDrawingSectionConsentEntry> Sections() =>
    [
        new() { SectionMark = "ББ", PersonName = "А.Буянжаргал" },
        new() { SectionMark = "ХАС", PersonName = "Э.Ариунцэцэг" },
        new() { SectionMark = "ЦБУ", PersonName = "С.Доржзовд" },
        new() { SectionMark = "ХТ", PersonName = "Ө.Батбаяр" },
        new() { SectionMark = "ХД", PersonName = "Б.Бат-Эрдэнэ" },
        new() { SectionMark = "ДГ", PersonName = "Б.Бат-Эрдэнэ" },
    ];

    private string WritePackage(string folderName)
    {
        string directory = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(directory);
        const string fileName = "BA-11.pdf";
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420);
            page.Height = XUnit.FromMillimeter(297);
            using (XGraphics gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("БА-11", new XFont("Arial", 12), XBrushes.Black, new XPoint(40, 40));
            document.Save(Path.Combine(directory, fileName));
        }

        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = "batnasan-revit",
                Application = SheetSourceApplication.Revit,
                DocumentPath = @"D:\Cloud Work\Batnasan Apartment\RVT\Arch\Arch_BN_SHEET V2.rvt",
            },
            Sheets =
            {
                new SheetPackageEntry
                {
                    SheetId = "ba-11",
                    Number = "БА-11",
                    WidthMm = 420,
                    HeightMm = 297,
                    PdfFileName = fileName,
                    PdfPageNumber = 1,
                    // Directly above a 180 × 36 corner table at (230, 251):
                    // 5 mm header + 6 × 5 mm rows.
                    ConsultationTableArea = new PageRectSpec { X = 230, Y = 216, Width = 180, Height = 35 },
                    ConsultationTableRows = 6,
                },
            },
        };
        return SheetPackageWriter.Write(manifest, directory, "batnasan");
    }

    private static (double, double, double, double) Rect(PageRectSpec rect) =>
        (rect.X, rect.Y, rect.Width, rect.Height);

    private static void RewriteEntry(string manifestPath, Action<JsonObject> change) =>
        RewriteManifest(manifestPath, manifest => change(manifest["sheets"]!.AsArray()[0]!.AsObject()));

    private static void RewriteManifest(string manifestPath, Action<JsonObject> change)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        change(manifest);
        File.WriteAllText(manifestPath, manifest.ToJsonString(SheetPackageJson.Options));
    }
}
