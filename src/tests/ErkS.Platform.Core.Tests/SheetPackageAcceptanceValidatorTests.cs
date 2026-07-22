using ErkS.Platform.Contracts;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

public sealed class SheetPackageAcceptanceValidatorTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-package-acceptance-" + Guid.NewGuid().ToString("N"));

    public SheetPackageAcceptanceValidatorTests() => Directory.CreateDirectory(workDirectory);

    [Fact]
    public void StrictVectorPackage_IsAcceptedWithVerifiedPageProfile()
    {
        string manifestPath = WritePackage("vector", WriteVectorPdf);

        SheetPackageAcceptanceReport report = SheetPackageAcceptanceValidator.Validate(manifestPath);

        Assert.True(report.IsAccepted, string.Join(" | ", report.Issues));
        SheetPackageAcceptancePage page = Assert.Single(report.Pages);
        Assert.Equal("layout-1", page.SheetId);
        Assert.InRange(page.WidthMm, 419.99, 420.01);
        Assert.InRange(page.HeightMm, 296.99, 297.01);
        Assert.True(page.HasVectorContent);
        Assert.Equal(0, page.ImageXObjectCount);
    }

    [Fact]
    public void StrictVectorPackage_RejectsFullPageRasterFallback()
    {
        string manifestPath = WritePackage("raster", WriteRasterPdf);

        SheetPackageAcceptanceReport report = SheetPackageAcceptanceValidator.Validate(manifestPath);

        Assert.False(report.IsAccepted);
        Assert.Contains(report.Issues, issue =>
            issue.Contains("raster", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VectorPackage_AllowsEmbeddedRasterAssetWhenVectorContentRemains()
    {
        string manifestPath = WritePackage("hybrid", WriteHybridPdf);

        SheetPackageAcceptanceReport report = SheetPackageAcceptanceValidator.Validate(manifestPath);

        Assert.True(report.IsAccepted, string.Join(" | ", report.Issues));
        SheetPackageAcceptancePage page = Assert.Single(report.Pages);
        Assert.True(page.HasVectorContent);
        Assert.Equal(1, page.ImageXObjectCount);
    }

    [Fact]
    public void MultiSheetPdf_InspectsEachReferencedPageOnce()
    {
        string directory = Path.Combine(workDirectory, "multi-sheet");
        Directory.CreateDirectory(directory);
        string pdfPath = Path.Combine(directory, "layouts.pdf");
        WriteMultiPageVectorPdf(pdfPath, 2);
        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = "autocad-multi-sheet",
                Application = SheetSourceApplication.AutoCad,
                ApplicationVersion = "AutoCAD 2026",
                DocumentPath = @"C:\reference\drawing.dwg",
                DocumentTitle = "drawing.dwg",
            },
            PackageScope = SheetPackageScope.FullSnapshot,
            Sheets =
            [
                CreateSharedPageEntry("layout-1", "01", 1),
                CreateSharedPageEntry("layout-2", "02", 2),
            ],
        };
        string manifestPath = SheetPackageWriter.Write(manifest, directory, "multi-sheet");

        SheetPackageAcceptanceReport report = SheetPackageAcceptanceValidator.Validate(manifestPath);

        Assert.True(report.IsAccepted, string.Join(" | ", report.Issues));
        Assert.Equal(2, report.Pages.Count);
        Assert.Equal([1, 2], report.Pages.Select(page => page.PdfPageNumber));
        Assert.Equal(["layout-1", "layout-2"], report.Pages.Select(page => page.SheetId));
    }

    [Fact]
    public void TamperedPackage_IsRejectedBeforeVectorInspection()
    {
        string manifestPath = WritePackage("tampered", WriteVectorPdf);
        string pdfPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "sheet.pdf");
        File.AppendAllText(pdfPath, "changed");

        SheetPackageAcceptanceReport report = SheetPackageAcceptanceValidator.Validate(manifestPath);

        Assert.False(report.IsAccepted);
        Assert.Empty(report.Pages);
        Assert.Contains(report.Issues, issue =>
            issue.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageContainingNativeAuthoringFile_IsRejected()
    {
        string manifestPath = WritePackage("native-payload", WriteVectorPdf);
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(manifestPath)!, "source.dwg"),
            "native source must remain local");

        SheetPackageAcceptanceReport report = SheetPackageAcceptanceValidator.Validate(manifestPath);

        Assert.False(report.IsAccepted);
        Assert.Contains(report.Issues, issue =>
            issue.Contains("Native authoring file", StringComparison.OrdinalIgnoreCase));
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

    private string WritePackage(string name, Action<string> writePdf)
    {
        string directory = Path.Combine(workDirectory, name);
        Directory.CreateDirectory(directory);
        string pdfPath = Path.Combine(directory, "sheet.pdf");
        writePdf(pdfPath);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = "autocad-reference",
                Application = SheetSourceApplication.AutoCad,
                ApplicationVersion = "AutoCAD 2026",
                DocumentPath = @"C:\reference\drawing.dwg",
                DocumentTitle = "drawing.dwg",
            },
            PackageScope = SheetPackageScope.FullSnapshot,
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = "layout-1",
                    Number = "01",
                    Name = "Layout 1",
                    WidthMm = 420,
                    HeightMm = 297,
                    ContentWidthMm = 420,
                    ContentHeightMm = 297,
                    PdfFileName = "sheet.pdf",
                    PageCount = 1,
                },
            ],
        };
        return SheetPackageWriter.Write(manifest, directory, name);
    }

    private static void WriteVectorPdf(string path)
    {
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(new XPen(XColors.Black, 0.5), 20, 20, 300, 160);
        graphics.DrawLine(new XPen(XColors.DarkBlue, 1), 20, 60, 300, 60);
        document.Save(path);
    }

    private static SheetPackageEntry CreateSharedPageEntry(
        string sheetId,
        string number,
        int pdfPageNumber) => new()
        {
            SheetId = sheetId,
            Number = number,
            Name = "Layout " + number,
            WidthMm = 420,
            HeightMm = 297,
            ContentWidthMm = 420,
            ContentHeightMm = 297,
            PdfFileName = "layouts.pdf",
            PdfPageNumber = pdfPageNumber,
            PageCount = 1,
        };

    private static void WriteMultiPageVectorPdf(string path, int pageCount)
    {
        using var document = new PdfDocument();
        for (int index = 1; index <= pageCount; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420);
            page.Height = XUnit.FromMillimeter(297);
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(new XPen(XColors.Black, 0.5), 20 + index, 20, 300, 160);
        }
        document.Save(path);
    }

    private void WriteRasterPdf(string path)
    {
        string imagePath = Path.Combine(workDirectory, "fallback.png");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        using XImage image = XImage.FromFile(imagePath);
        graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        document.Save(path);
    }

    private void WriteHybridPdf(string path)
    {
        string imagePath = Path.Combine(workDirectory, "hybrid.png");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(new XPen(XColors.Black, 0.5), 20, 20, 300, 160);
        using XImage image = XImage.FromFile(imagePath);
        graphics.DrawImage(image, 30, 30, 40, 40);
        document.Save(path);
    }
}
