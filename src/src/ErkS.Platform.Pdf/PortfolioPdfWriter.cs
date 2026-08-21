using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Pdf;

public sealed record PortfolioBuildItem(
    string Kind,
    string Layout,
    string Caption,
    string SourcePath,
    int SourcePageNumber,
    double FocalPointX,
    double FocalPointY);

public sealed record PortfolioBuildRequest(
    string Title,
    string OutputPath,
    double PageWidthMm,
    double PageHeightMm,
    IReadOnlyList<PortfolioBuildItem> Items);

public sealed record PortfolioBuildResult(
    string OutputPath,
    int PageCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Writes the portfolio as a presentation: one page per item, drawn edge to
/// edge with nothing else on it.
///
/// This deliberately shares no chrome with the album writer. An album page is
/// a drawing inside a standard frame with a title block; a portfolio page is
/// the material itself, so no frame, grid, sheet header or corner table is
/// ever drawn here.
/// </summary>
public static class PortfolioPdfWriter
{
    private const double PointsPerMm = 72.0 / 25.4;
    private const double ContainMarginMm = 14;
    private const double CaptionBandMm = 16;

    private static readonly XSolidBrush PaperBrush = new(XColors.White);
    private static readonly XSolidBrush BleedBrush = new(XColor.FromArgb(24, 26, 30));
    private static readonly XSolidBrush CaptionOverBrush = new(XColor.FromArgb(210, 16, 17, 20));

    public static PortfolioBuildResult Build(PortfolioBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        WindowsFontResolver.Register();

        var warnings = new List<string>();
        using var document = new PdfDocument();
        document.Info.Title = string.IsNullOrWhiteSpace(request.Title)
            ? "Портфолио"
            : request.Title.Trim();

        foreach (PortfolioBuildItem item in request.Items)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(Math.Max(50, request.PageWidthMm));
            page.Height = XUnit.FromMillimeter(Math.Max(50, request.PageHeightMm));
            using XGraphics gfx = XGraphics.FromPdfPage(page);
            if (!DrawItem(gfx, page, item, warnings))
            {
                // The page stays in the portfolio so the sequence the user
                // arranged is preserved; only its content is missing.
                gfx.DrawRectangle(PaperBrush, 0, 0, page.Width.Point, page.Height.Point);
                DrawCaption(gfx, page, item.Caption, overImage: false);
            }
        }

        if (document.PageCount == 0)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(Math.Max(50, request.PageWidthMm));
            page.Height = XUnit.FromMillimeter(Math.Max(50, request.PageHeightMm));
            warnings.Add("Портфолиод хуудас нэмээгүй тул хоосон баримт үүсгэлээ.");
        }

        string outputPath = Path.GetFullPath(request.OutputPath);
        string directory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.tmp.pdf");
        try
        {
            document.Save(temporaryPath);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup must not mask the build result.
            }
        }

        return new PortfolioBuildResult(outputPath, document.PageCount, warnings);
    }

    private static bool DrawItem(
        XGraphics gfx,
        PdfPage page,
        PortfolioBuildItem item,
        List<string> warnings)
    {
        string path = (item.SourcePath ?? "").Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            warnings.Add($"Файл олдсонгүй, хуудас хоосон үлдлээ: {item.Caption}".Trim());
            return false;
        }

        bool fullBleed = item.Layout.Equals(
            "FullBleed",
            StringComparison.OrdinalIgnoreCase);
        var pageRect = new XRect(0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawRectangle(fullBleed ? BleedBrush : PaperBrush, pageRect);

        try
        {
            using XImage image = OpenSource(item);
            XRect area = fullBleed
                ? pageRect
                : ContentArea(pageRect, hasCaption: item.Caption.Length > 0);
            if (fullBleed)
                DrawCovered(gfx, image, area, item.FocalPointX, item.FocalPointY);
            else
                DrawContained(gfx, image, area);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
                NotSupportedException or ArgumentException)
        {
            warnings.Add($"Файлыг уншиж чадсангүй: {Path.GetFileName(path)} - {exception.Message}");
            return false;
        }

        DrawCaption(gfx, page, item.Caption, fullBleed);
        return true;
    }

    private static XImage OpenSource(PortfolioBuildItem item)
    {
        // A PDF page is placed as a form, so it stays vector.
        if (Path.GetExtension(item.SourcePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var form = XPdfForm.FromFile(item.SourcePath);
            form.PageNumber = Math.Max(1, item.SourcePageNumber);
            return form;
        }
        return XImage.FromFile(item.SourcePath);
    }

    private static XRect ContentArea(XRect pageRect, bool hasCaption)
    {
        double margin = ContainMarginMm * PointsPerMm;
        double bottom = margin + (hasCaption ? CaptionBandMm * PointsPerMm : 0);
        return new XRect(
            pageRect.Left + margin,
            pageRect.Top + margin,
            Math.Max(1, pageRect.Width - margin * 2),
            Math.Max(1, pageRect.Height - margin - bottom));
    }

    /// <summary>Fits the whole source inside the area, centred.</summary>
    private static void DrawContained(XGraphics gfx, XImage image, XRect area)
    {
        double scale = Math.Min(area.Width / image.PointWidth, area.Height / image.PointHeight);
        if (!double.IsFinite(scale) || scale <= 0)
            return;

        double width = image.PointWidth * scale;
        double height = image.PointHeight * scale;
        gfx.DrawImage(
            image,
            area.Left + (area.Width - width) / 2,
            area.Top + (area.Height - height) / 2,
            width,
            height);
    }

    /// <summary>Fills the area, cropping whatever falls outside it.</summary>
    private static void DrawCovered(
        XGraphics gfx,
        XImage image,
        XRect area,
        double focalPointX,
        double focalPointY)
    {
        double scale = Math.Max(area.Width / image.PointWidth, area.Height / image.PointHeight);
        if (!double.IsFinite(scale) || scale <= 0)
            return;

        double width = image.PointWidth * scale;
        double height = image.PointHeight * scale;
        double left = area.Left - (width - area.Width) * Math.Clamp(focalPointX, 0, 1);
        double top = area.Top - (height - area.Height) * Math.Clamp(focalPointY, 0, 1);

        XGraphicsState state = gfx.Save();
        gfx.IntersectClip(area);
        gfx.DrawImage(image, left, top, width, height);
        gfx.Restore(state);
    }

    private static void DrawCaption(XGraphics gfx, PdfPage page, string caption, bool overImage)
    {
        string text = (caption ?? "").Trim();
        if (text.Length == 0)
            return;

        double margin = ContainMarginMm * PointsPerMm;
        double band = CaptionBandMm * PointsPerMm;
        var rect = new XRect(
            margin,
            page.Height.Point - margin - band,
            Math.Max(1, page.Width.Point - margin * 2),
            band);
        if (overImage)
        {
            gfx.DrawRectangle(
                CaptionOverBrush,
                new XRect(rect.Left - 6, rect.Top - 4, rect.Width + 12, rect.Height + 8));
        }

        var font = new XFont("Segoe UI", 11, XFontStyleEx.Regular);
        XBrush brush = overImage
            ? new XSolidBrush(XColors.White)
            : new XSolidBrush(XColor.FromArgb(28, 30, 34));
        gfx.DrawString(text, font, brush, rect, XStringFormats.CenterLeft);
    }
}
