namespace ErkS.Platform.Core;

/// <summary>A part of a page to rasterise: where it sits in a full page of the given pixel width.</summary>
public sealed record PreviewRegion(int FullWidthPx, int X, int Y, int Width, int Height);

/// <summary>
/// What of a zoomed sheet to draw sharp: only the part on screen, with a margin for panning.
/// </summary>
/// <remarks>
/// Zooming re-rasterised the whole page at the new width - 34 MP for an A2 at 300 dpi, again at
/// every zoom step - while a 4K screen shows at most 8 MP of it (owner, 2026-10-30: «хуудсыг
/// zoom хийхэд маш удаан тодроод байна»). The whole page now stops at
/// <see cref="FullPageCeilingPx"/>; past it, the visible part is drawn at the zoom's own density,
/// so the cost follows the screen, not the paper.
/// </remarks>
public static class PreviewViewportRegion
{
    /// <summary>The whole page is never drawn wider than this; detail takes over beyond it.</summary>
    public const int FullPageCeilingPx = 4800;

    /// <summary>How much of the view is added on each side, so a short pan stays sharp.</summary>
    public const double MarginFraction = 0.25;

    /// <summary>PDFium's device space is int-addressed; a page wider than this is not asked for.</summary>
    private const int LargestFullWidthPx = 60000;

    /// <summary>The full-page raster width for a display: the old rule, held at the ceiling.</summary>
    public static int FullPageWidth(double pageWidthPoints, double displayWidthPx) =>
        Math.Min(
            PreviewRenderResolution.ForDisplay(pageWidthPoints, displayWidthPx),
            Math.Max(PreviewRenderResolution.FirstPassWidthPx, FullPageCeilingPx));

    /// <summary>
    /// The region to draw, or null when the full-page image already holds as many pixels as the
    /// screen shows. Everything in device-independent units except the result, which is pixels.
    /// </summary>
    public static PreviewRegion? For(
        double pageWidthDip,
        double pageHeightDip,
        double visibleX,
        double visibleY,
        double visibleWidth,
        double visibleHeight,
        double dpiScale,
        double pageWidthPoints,
        int fullPageRenderedPx)
    {
        if (!(pageWidthDip > 0) || !(pageHeightDip > 0) || !(visibleWidth > 0) || !(visibleHeight > 0))
            return null;
        double scale = dpiScale > 0 && double.IsFinite(dpiScale) ? dpiScale : 1d;
        int fullWidthPx = (int)Math.Ceiling(Math.Min(pageWidthDip * scale, LargestFullWidthPx));
        if (fullWidthPx <= fullPageRenderedPx)
            return null;

        double marginX = visibleWidth * MarginFraction;
        double marginY = visibleHeight * MarginFraction;
        double left = Math.Clamp(visibleX - marginX, 0, pageWidthDip);
        double top = Math.Clamp(visibleY - marginY, 0, pageHeightDip);
        double right = Math.Clamp(visibleX + visibleWidth + marginX, 0, pageWidthDip);
        double bottom = Math.Clamp(visibleY + visibleHeight + marginY, 0, pageHeightDip);
        if (right - left < 1 || bottom - top < 1)
            return null;

        double pixelScale = fullWidthPx / pageWidthDip;
        int x = (int)Math.Floor(left * pixelScale);
        int y = (int)Math.Floor(top * pixelScale);
        int width = Math.Max(1, (int)Math.Ceiling(right * pixelScale) - x);
        int height = Math.Max(1, (int)Math.Ceiling(bottom * pixelScale) - y);
        return new PreviewRegion(fullWidthPx, x, y, width, height);
    }
}
