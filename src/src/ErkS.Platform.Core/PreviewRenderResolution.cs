namespace ErkS.Platform.Core;

/// <summary>
/// How many pixels a page preview is rasterised at.
/// </summary>
/// <remarks>
/// The preview used to render every page at a flat 2400 pixels wide. On an A1
/// sheet that is about 72 DPI, and the user's word for it was "маш муу".
///
/// The number was not wrong everywhere - it is fine when the whole sheet is on
/// screen. It is wrong when someone zooms in, which is when a drawing is
/// actually read: this surface magnifies to 6x, so a 2400-pixel raster ends up
/// stretched across 6000 device-independent pixels, well under one sample per
/// pixel. What looked like a DPI setting was really a fixed raster meeting a
/// variable magnification.
///
/// So the width follows the zoom rather than a constant, with 300 DPI as the
/// ceiling. The two are close to the same thing here: A1 at 300 DPI is about
/// 9900 pixels, and 6x zoom needs about 9000, so the cap binds roughly where
/// the magnification stops.
///
/// Widths are quantised. Rendering at every width a zoom gesture passes
/// through would rasterise dozens of times per drag and cache each one; the
/// steps mean a zoom lands on a width already rendered most of the time, and
/// the cache holds a handful of images rather than an unbounded set.
/// </remarks>
public static class PreviewRenderResolution
{
    /// <summary>The quality ceiling, in dots per inch of the paper.</summary>
    public const double TargetDpi = 300d;

    /// <summary>
    /// Enough to read the sheet whole, and cheap enough to show immediately
    /// while a sharper pass is still rendering.
    /// </summary>
    public const int FirstPassWidthPx = 2400;

    private const double PointsPerInch = 72d;

    /// <summary>
    /// The raster width for a page displayed <paramref name="displayWidthPx"/>
    /// pixels wide, never finer than <see cref="TargetDpi"/> on the paper and
    /// never coarser than the first pass.
    /// </summary>
    /// <param name="pageWidthPoints">
    /// The page's own width in PDF points. Zero or nonsense falls back to the
    /// first-pass width rather than guessing a paper size: an unknown page is
    /// a reason to render conservatively, not to invent an A1.
    /// </param>
    public static int ForDisplay(double pageWidthPoints, double displayWidthPx)
    {
        if (!double.IsFinite(displayWidthPx) || displayWidthPx <= 0)
            return FirstPassWidthPx;

        int ceiling = Ceiling(pageWidthPoints);
        int wanted = Quantise((int)Math.Ceiling(displayWidthPx));
        return Math.Clamp(wanted, FirstPassWidthPx, ceiling);
    }

    /// <summary>The width at which the paper reaches <see cref="TargetDpi"/>.</summary>
    public static int Ceiling(double pageWidthPoints)
    {
        if (!double.IsFinite(pageWidthPoints) || pageWidthPoints <= 0)
            return FirstPassWidthPx;

        double pixels = pageWidthPoints / PointsPerInch * TargetDpi;
        // Not quantised: this is the ceiling, so rounding it up would render
        // finer than the target and cost the extra pixels for nothing.
        return Math.Max(FirstPassWidthPx, (int)Math.Ceiling(pixels));
    }

    /// <summary>
    /// Rounds up to the next step. A drag through 3200 different widths would
    /// otherwise rasterise 3200 times.
    /// </summary>
    private static int Quantise(int widthPx)
    {
        int step = FirstPassWidthPx / 2;
        int steps = (widthPx + step - 1) / step;
        return Math.Max(1, steps) * step;
    }
}
