using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

public sealed record SheetSplitResult(int SourcePages, int OutputPages, int SplitPages);

/// <summary>
/// The Erk-S PDF Sheet Splitter inside Studio (owner, 2026-10-29). <see cref="Split"/> writes the album cut
/// into its A3 (and A4) pieces - vector, each piece a clip of the whole page - with the join guides the
/// splitter draws. <see cref="WriteSplitPreview"/> draws the cuts over a copy of the whole album so they can be
/// seen; the album itself is never changed.
/// </summary>
public static class AlbumSheetSplitWriter
{
    private const double PointsPerMm = 72d / 25.4;

    private static readonly XColor[] Palette =
    [
        XColor.FromArgb(58, 160, 255),
        XColor.FromArgb(255, 176, 0),
        XColor.FromArgb(84, 214, 87),
        XColor.FromArgb(255, 92, 138),
        XColor.FromArgb(140, 125, 255),
        XColor.FromArgb(0, 224, 184),
    ];

    public static SheetSplitResult Split(string sourcePdf, string outputPdf, bool drawJoinGuides = true)
    {
        RefuseSameFile(sourcePdf, outputPdf);
        int sourcePages, outputPages = 0, splitPages = 0;
        using (var output = new PdfDocument())
        using (XPdfForm form = XPdfForm.FromFile(sourcePdf))
        {
            sourcePages = form.PageCount;
            for (int number = 1; number <= sourcePages; number++)
            {
                form.PageNumber = number;
                double widthPt = Math.Max(1, form.PointWidth), heightPt = Math.Max(1, form.PointHeight);
                SheetSplitPlan plan = SheetSplitPlanner.Plan(widthPt / PointsPerMm, heightPt / PointsPerMm);
                if (!plan.Splits)
                {
                    PdfPage whole = output.AddPage();
                    whole.Width = XUnit.FromPoint(widthPt);
                    whole.Height = XUnit.FromPoint(heightPt);
                    using XGraphics gfx = XGraphics.FromPdfPage(whole);
                    DrawForm(gfx, form, new XRect(0, 0, widthPt, heightPt));
                    outputPages++;
                    continue;
                }

                splitPages++;
                foreach (SheetSplitTile tile in plan.Tiles)
                {
                    PdfPage page = output.AddPage();
                    page.Width = XUnit.FromMillimeter(tile.TargetWidthMm);
                    page.Height = XUnit.FromMillimeter(tile.TargetHeightMm);
                    double pageW = page.Width.Point, pageH = page.Height.Point;
                    double scaleX = pageW / Math.Max(1e-6, tile.Clip.Width * PointsPerMm);
                    double scaleY = pageH / Math.Max(1e-6, tile.Clip.Height * PointsPerMm);
                    using XGraphics gfx = XGraphics.FromPdfPage(page);
                    XGraphicsState state = gfx.Save();
                    gfx.IntersectClip(new XRect(0, 0, pageW, pageH));
                    DrawForm(gfx, form, new XRect(
                        -tile.Clip.X * PointsPerMm * scaleX,
                        -tile.Clip.Y * PointsPerMm * scaleY,
                        widthPt * scaleX,
                        heightPt * scaleY));
                    gfx.Restore(state);
                    if (drawJoinGuides)
                    {
                        var pen = new XPen(XColor.FromArgb(128, 128, 128), 0.35);
                        foreach (double x in tile.VerticalGuidesMm)
                            gfx.DrawLine(pen, x * PointsPerMm * scaleX, 0, x * PointsPerMm * scaleX, pageH);
                        foreach (double y in tile.HorizontalGuidesMm)
                            gfx.DrawLine(pen, 0, y * PointsPerMm * scaleY, pageW, y * PointsPerMm * scaleY);
                    }
                    outputPages++;
                }
            }
            output.Save(outputPdf);
        }
        return new SheetSplitResult(sourcePages, outputPages, splitPages);
    }

    /// <summary>The whole album with every split sheet's pieces outlined and numbered, and its join guides dashed.</summary>
    public static SheetSplitResult WriteSplitPreview(string sourcePdf, string outputPdf)
    {
        RefuseSameFile(sourcePdf, outputPdf);
        WindowsFontResolver.Register();
        File.Copy(sourcePdf, outputPdf, overwrite: true);
        int pages, splitPages = 0, tiles = 0;
        using (PdfDocument document = PdfReader.Open(outputPdf, PdfDocumentOpenMode.Modify))
        {
            pages = document.PageCount;
            foreach (PdfPage page in document.Pages)
            {
                double widthPt = page.Width.Point, heightPt = page.Height.Point;
                SheetSplitPlan plan = SheetSplitPlanner.Plan(widthPt / PointsPerMm, heightPt / PointsPerMm);
                if (!plan.Splits)
                {
                    tiles++;
                    continue;
                }
                splitPages++;
                tiles += plan.Tiles.Count;
                using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var guidePen = new XPen(XColor.FromArgb(220, 120, 128, 138), 0.6) { DashStyle = XDashStyle.Dash };
                foreach (SheetSplitTile tile in plan.Tiles)
                {
                    XColor color = Palette[(tile.Label - 1) % Palette.Length];
                    var frame = new XRect(
                        tile.Clip.X * PointsPerMm,
                        tile.Clip.Y * PointsPerMm,
                        tile.Clip.Width * PointsPerMm,
                        tile.Clip.Height * PointsPerMm);
                    gfx.DrawRectangle(new XPen(XColor.FromArgb(230, color.R, color.G, color.B), 2), frame);
                    double size = Math.Min(frame.Width, frame.Height) * 0.35;
                    var font = new XFont("Segoe UI", Math.Max(12, size), XFontStyleEx.Bold);
                    gfx.DrawString(
                        tile.Label.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        font,
                        new XSolidBrush(XColor.FromArgb(90, 150, 150, 150)),
                        frame,
                        XStringFormats.Center);
                    foreach (double x in tile.VerticalGuidesMm)
                        gfx.DrawLine(guidePen, frame.X + x * PointsPerMm, frame.Y, frame.X + x * PointsPerMm, frame.Bottom);
                    foreach (double y in tile.HorizontalGuidesMm)
                        gfx.DrawLine(guidePen, frame.X, frame.Y + y * PointsPerMm, frame.Right, frame.Y + y * PointsPerMm);
                }
            }
            document.Save(outputPdf);
        }
        return new SheetSplitResult(pages, tiles, splitPages);
    }

    /// <summary>The whole form drawn so its page fills <paramref name="destination"/> (PDFsharp's MediaBox offset undone).</summary>
    private static void DrawForm(XGraphics gfx, XPdfForm form, XRect destination)
    {
        PdfRectangle mediaBox = form.Page?.MediaBox ??
                                new PdfRectangle(new XPoint(0, 0), new XPoint(Math.Max(1, form.PointWidth), Math.Max(1, form.PointHeight)));
        gfx.DrawImage(form, PdfSharpAlbumWriter.CalculatePdfSharpFormDrawRectangle(
            destination,
            Math.Max(1, form.PointWidth),
            Math.Max(1, form.PointHeight),
            mediaBox.X1,
            mediaBox.Y1));
    }

    private static void RefuseSameFile(string sourcePdf, string outputPdf)
    {
        if (string.Equals(Path.GetFullPath(sourcePdf), Path.GetFullPath(outputPdf), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Эх PDF дээр дарж бичихгүй — өөр файлд хадгална.");
    }
}
