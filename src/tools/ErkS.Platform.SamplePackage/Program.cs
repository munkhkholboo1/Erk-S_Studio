using ErkS.Platform.Contracts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

// Generates a demo sheet package (fake AutoCAD layouts as vector PDFs plus a
// verified manifest) so the whole album pipeline can be exercised without
// AutoCAD or Revit. Usage:
//   dotnet run --project tools/ErkS.Platform.SamplePackage -- <outputDir> [sheetCount]

ErkS.Platform.Pdf.WindowsFontResolver.Register();
var outputDirectory = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "sample-package");
var sheetCount = args.Length > 1 && int.TryParse(args[1], out var parsed) ? Math.Clamp(parsed, 1, 100) : 4;

Directory.CreateDirectory(outputDirectory);
var manifest = new SheetPackageManifest
{
    Source = new SheetPackageSource
    {
        Application = SheetSourceApplication.AutoCad,
        ApplicationVersion = "AutoCAD 2026 (sample)",
        DocumentPath = @"C:\Projects\Sample\SiteplanSample.dwg",
        DocumentTitle = "SiteplanSample",
        ProjectCode = "ERKS-SAMPLE",
    },
};

for (var index = 1; index <= sheetCount; index++)
{
    var fileName = $"sheet-{index:00}.pdf";
    WriteFakeSheetPdf(Path.Combine(outputDirectory, fileName), index);
    manifest.Sheets.Add(new SheetPackageEntry
    {
        SheetId = $"LAYOUT-{index:00}",
        Number = $"AR-{index:00}",
        Name = $"Sample layout {index}",
        Discipline = "AR",
        Revision = "0",
        WidthMm = 420,
        HeightMm = 297,
        PdfFileName = fileName,
        PageCount = 1,
    });
}

var manifestPath = SheetPackageWriter.Write(manifest, outputDirectory, "SiteplanSample");
Console.WriteLine($"Sample package written: {manifestPath} ({sheetCount} sheets)");
return 0;

static void WriteFakeSheetPdf(string path, int index)
{
    using var document = new PdfDocument();
    var page = document.AddPage();
    // A3 landscape in points (1 mm = 72 / 25.4 pt).
    page.Width = XUnit.FromMillimeter(420);
    page.Height = XUnit.FromMillimeter(297);

    using var gfx = XGraphics.FromPdfPage(page);
    var border = new XPen(XColors.Black, 1.2);
    gfx.DrawRectangle(border, 14, 14, page.Width.Point - 28, page.Height.Point - 28);
    gfx.DrawRectangle(new XPen(XColors.Gray, 0.6), 24, 24, page.Width.Point - 48, page.Height.Point - 48);

    var font = new XFont("Arial", 22, XFontStyleEx.Bold);
    gfx.DrawString($"SAMPLE LAYOUT {index}", font, XBrushes.Black,
        new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);

    var small = new XFont("Arial", 9);
    gfx.DrawString($"AR-{index:00}  -  Erk-S Platform sample sheet", small, XBrushes.Gray,
        new XPoint(30, page.Height.Point - 32));

    document.Save(path);
}
