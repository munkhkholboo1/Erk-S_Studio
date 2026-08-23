using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Choosing a large sheet for a drawing is a decision about how it should be
/// seen. The portfolio can hold every page at one size, or keep each page at
/// the size it was drawn on.
/// </summary>
public sealed class PortfolioPageSizeTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public PortfolioPageSizeTests()
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
    public void FixedMode_PutsEveryPageAtTheChosenSize()
    {
        string small = WriteSourcePdf("small.pdf", 420, 297);
        string large = WriteSourcePdf("large.pdf", 828, 582);

        using PdfDocument built = Build(useSourcePageSize: false, small, large);

        Assert.All(built.Pages.Cast<PdfPage>(), page =>
        {
            Assert.Equal(420, page.Width.Millimeter, precision: 1);
            Assert.Equal(297, page.Height.Millimeter, precision: 1);
        });
    }

    [Fact]
    public void SourceMode_KeepsEachPageAtTheSizeItWasDrawnOn()
    {
        string small = WriteSourcePdf("small.pdf", 420, 297);
        string large = WriteSourcePdf("large.pdf", 828, 582);

        using PdfDocument built = Build(useSourcePageSize: true, small, large);

        Assert.Equal(2, built.PageCount);
        Assert.Equal(420, built.Pages[0].Width.Millimeter, precision: 1);
        Assert.Equal(297, built.Pages[0].Height.Millimeter, precision: 1);
        // The large sheet stays large - which is the whole point of the mode.
        Assert.Equal(828, built.Pages[1].Width.Millimeter, precision: 1);
        Assert.Equal(582, built.Pages[1].Height.Millimeter, precision: 1);
    }

    [Fact]
    public void SourceMode_FallsBackWhenASourceCannotBeMeasured()
    {
        string missing = Path.Combine(workDirectory, "missing.pdf");

        using PdfDocument built = Build(useSourcePageSize: true, missing);

        // The page is still produced at the portfolio size rather than lost.
        Assert.Equal(1, built.PageCount);
        Assert.Equal(420, built.Pages[0].Width.Millimeter, precision: 1);
    }

    [Fact]
    public void ChangingThePageSetup_LeavesThePagesAndTheirOrderAlone()
    {
        // The size belongs to the portfolio, the arrangement to the user.
        var portfolio = new ProjectPortfolio();
        portfolio.Items.Add(new ProjectPortfolioItem
        {
            Order = 1,
            Kind = ProjectPortfolioItemKinds.CadPage,
            Title = "Эхнийх",
            Caption = "Гараар бичсэн",
            Layout = ProjectPortfolioLayouts.Contain,
            SourceSheetKey = "a",
        });
        portfolio.Items.Add(new ProjectPortfolioItem
        {
            Order = 2,
            Kind = ProjectPortfolioItemKinds.CadPage,
            Title = "Хоёрдугаарх",
            SourceSheetKey = "b",
            RemovedAtUtc = DateTimeOffset.UtcNow,
        });
        portfolio.Normalize();

        portfolio.PageSizeMode = ProjectPortfolioPageSizeModes.SourcePage;
        portfolio.PageWidthMm = 828;
        portfolio.PageHeightMm = 582;
        portfolio.Normalize();

        Assert.True(portfolio.UsesSourcePageSize);
        Assert.Equal(["a", "b"], portfolio.OrderedItems().Select(item => item.SourceSheetKey));
        ProjectPortfolioItem first = portfolio.OrderedItems()[0];
        Assert.Equal("Гараар бичсэн", first.Caption);
        Assert.Equal(ProjectPortfolioLayouts.Contain, first.Layout);
        Assert.True(portfolio.OrderedItems()[1].IsRemoved);
    }

    [Fact]
    public void UnknownPageSizeMode_FallsBackToFixed()
    {
        var portfolio = new ProjectPortfolio { PageSizeMode = "Whatever" };

        portfolio.Normalize();

        Assert.Equal(ProjectPortfolioPageSizeModes.Fixed, portfolio.PageSizeMode);
        Assert.False(portfolio.UsesSourcePageSize);
    }

    private PdfDocument Build(bool useSourcePageSize, params string[] sources)
    {
        string outputPath = Path.Combine(
            workDirectory,
            $"portfolio-{(useSourcePageSize ? "source" : "fixed")}.pdf");
        PortfolioPdfWriter.Build(new PortfolioBuildRequest(
            "Портфолио",
            outputPath,
            420,
            297,
            sources.Select(path => new PortfolioBuildItem(
                ProjectPortfolioItemKinds.CadPage,
                ProjectPortfolioLayouts.FitPage,
                Caption: "",
                SourcePath: path,
                SourcePageNumber: 1,
                FocalPointX: 0.5,
                FocalPointY: 0.5)).ToList(),
            useSourcePageSize));
        return PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
    }

    private string WriteSourcePdf(string name, double widthMm, double heightMm)
    {
        string path = Path.Combine(workDirectory, name);
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(widthMm);
        page.Height = XUnit.FromMillimeter(heightMm);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(new XPen(XColors.Black, 1), 10, 10, page.Width.Point - 20, page.Height.Point - 20);
        }
        document.Save(path);
        return path;
    }
}
