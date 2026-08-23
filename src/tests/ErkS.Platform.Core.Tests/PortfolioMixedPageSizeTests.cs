using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// One package may carry portfolio pages of different paper sizes - a producer
/// puts no uniformity rule on them, because a presentation mixes formats. Every
/// one of them has to land on the portfolio's own page whole.
/// </summary>
public sealed class PortfolioMixedPageSizeTests : IDisposable
{
    private const double PortfolioWidthMm = 420;
    private const double PortfolioHeightMm = 297;

    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public PortfolioMixedPageSizeTests()
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
    public void PagesOfDifferentSizes_AllLandWholeOnThePortfolioPage()
    {
        // Same size as the portfolio, a much larger sheet, and a portrait one.
        string same = WriteSourcePdf("same.pdf", 420, 297);
        string larger = WriteSourcePdf("larger.pdf", 828, 582);
        string portrait = WriteSourcePdf("portrait.pdf", 297, 420);
        string outputPath = Path.Combine(workDirectory, "portfolio.pdf");

        PortfolioBuildResult result = PortfolioPdfWriter.Build(new PortfolioBuildRequest(
            "Портфолио",
            outputPath,
            PortfolioWidthMm,
            PortfolioHeightMm,
            [Item(same), Item(larger), Item(portrait)]));

        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.PageCount);

        using PdfDocument built = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.All(built.Pages.Cast<PdfPage>(), page =>
        {
            Assert.Equal(PortfolioWidthMm, page.Width.Millimeter, precision: 1);
            Assert.Equal(PortfolioHeightMm, page.Height.Millimeter, precision: 1);
        });

        // Each source, whatever its shape, is scaled to sit inside that page.
        foreach ((double width, double height) in new[] { (420d, 297d), (828d, 582d), (297d, 420d) })
        {
            PortfolioPlacementRect placement = PortfolioPlacement.Fit(
                width,
                height,
                0,
                0,
                PortfolioWidthMm,
                PortfolioHeightMm)!.Value;
            Assert.InRange(placement.Left, -0.001, PortfolioWidthMm);
            Assert.InRange(placement.Top, -0.001, PortfolioHeightMm);
            Assert.InRange(placement.Right, 0, PortfolioWidthMm + 0.001);
            Assert.InRange(placement.Bottom, 0, PortfolioHeightMm + 0.001);
        }
    }

    [Fact]
    public void BuildReportsThePageCountItWrote()
    {
        // The count was read back from the document after saving it, and PdfSharp
        // seals a saved document against every question - so building a portfolio
        // threw after writing the file, and Studio reported a failure over a PDF
        // that was sitting on disk.
        string source = WriteSourcePdf("one.pdf", 420, 297);
        string outputPath = Path.Combine(workDirectory, "counted.pdf");

        PortfolioBuildResult result = PortfolioPdfWriter.Build(new PortfolioBuildRequest(
            "Портфолио",
            outputPath,
            PortfolioWidthMm,
            PortfolioHeightMm,
            [Item(source), Item(source)]));

        Assert.Equal(2, result.PageCount);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public void EmptyPortfolio_StillProducesAReadableDocument()
    {
        string outputPath = Path.Combine(workDirectory, "empty.pdf");

        PortfolioBuildResult result = PortfolioPdfWriter.Build(new PortfolioBuildRequest(
            "Портфолио",
            outputPath,
            PortfolioWidthMm,
            PortfolioHeightMm,
            []));

        Assert.Equal(1, result.PageCount);
        Assert.Single(result.Warnings);
        Assert.True(File.Exists(result.OutputPath));
    }

    private static PortfolioBuildItem Item(string path) => new(
        ProjectPortfolioItemKinds.CadPage,
        ProjectPortfolioLayouts.FitPage,
        Caption: "",
        SourcePath: path,
        SourcePageNumber: 1,
        FocalPointX: 0.5,
        FocalPointY: 0.5);

    private string WriteSourcePdf(string name, double widthMm, double heightMm)
    {
        string path = Path.Combine(workDirectory, name);
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(widthMm);
        page.Height = XUnit.FromMillimeter(heightMm);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(
                new XPen(XColors.Black, 1),
                10,
                10,
                page.Width.Point - 20,
                page.Height.Point - 20);
        }
        document.Save(path);
        return path;
    }
}
