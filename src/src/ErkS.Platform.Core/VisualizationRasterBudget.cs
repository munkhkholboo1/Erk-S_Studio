namespace ErkS.Platform.Core;

/// <summary>
/// What a prepared visualisation image should be, and whether it needs preparing
/// at all.
/// </summary>
/// <param name="PixelWidth">Width to prepare, in pixels.</param>
/// <param name="PixelHeight">Height to prepare, in pixels.</param>
/// <param name="Resample">
/// False when the source is already at or below the budget. Nothing is done in
/// that case - enlarging an image invents detail that was never photographed.
/// </param>
public sealed record VisualizationRasterPlan(int PixelWidth, int PixelHeight, bool Resample);

/// <summary>
/// The ceiling Studio puts on its own raster pages.
///
/// 🔴 THE OWNER'S DECISION, AND THE CEILING IS STUDIO'S - NOT THE SOURCE'S.
/// «харагдах байдлын хуудаснуудын ачааллыг бууруулж Студиогийн өөрийнх нь
/// хангалттай гэж үзэх хэмжээнд бариулж болно. Тэгэхгүй бол харагдах байдлын нэг
/// зураг л гэхэд 16к рендер байгаа». They render at whatever size they like; the
/// album decides what it carries.
///
/// 🔴 AND IT IS NOT A PIXEL COUNT - IT IS A DENSITY AT PLACED SIZE. A fixed
/// «2125 px» is right only for a 180 mm frame: the same rule on a full-width
/// 395 mm tile would starve it, and on a small tile would keep four times the
/// pixels needed. What is constant is millimetres per pixel on the page.
///
///   180 mm → 2126 px      260 mm → 3071 px      395 mm → 4665 px
///
/// 🔴 300 IS NOT A GUESS. Measured 2026-09-12 in the owner's own album
/// (albums/cloud/…R41-f4ca475d.pdf): every raster page in it is 3507 × 2480 at A4
/// - exactly 300 DPI, JPEG, 0.59-1.67 MB each. The ceiling is the album's own
/// existing standard, not a new one introduced here.
/// </summary>
public static class VisualizationRasterBudget
{
    /// <summary>
    /// The density a placed image is allowed to reach. Dated because it is a
    /// measurement of the album as it stood on 2026-09-12, not a law: if the
    /// album's own raster pages move, this number is stale and so is its reason.
    /// </summary>
    public const double TargetDotsPerInch = 300d;

    public const double MillimetresPerInch = 25.4d;

    /// <summary>Millimetres one allowed pixel covers on the page.</summary>
    public static double MillimetresPerPixel => MillimetresPerInch / TargetDotsPerInch;

    /// <summary>
    /// How many pixels a placed span of <paramref name="millimetres"/> needs.
    ///
    /// Rounded UP: landing a pixel short of the target to save a rounding error
    /// is a visible loss on a printed sheet and an invisible saving in the file.
    /// </summary>
    public static int PixelsAcross(double millimetres) =>
        millimetres <= 0d
            ? 1
            : Math.Max(1, (int)Math.Ceiling(millimetres / MillimetresPerPixel));

    /// <summary>
    /// What to prepare for this image in this frame.
    ///
    /// 🔴 THE FIT MODE DECIDES THE SCALE, AND THE TWO MODES DISAGREE. Contain
    /// scales the image to sit INSIDE the frame, so one dimension touches the
    /// edge and the other is smaller - the limiting factor is the smaller ratio.
    /// CenterCrop scales it to COVER the frame, so the image is bigger than the
    /// frame and the overflow is cut - the limiting factor is the larger ratio,
    /// and the whole image therefore needs MORE pixels than the frame alone
    /// suggests. Using one rule for both would leave every cropped tile soft.
    /// </summary>
    public static VisualizationRasterPlan For(
        int sourcePixelWidth,
        int sourcePixelHeight,
        PageRectMm frame,
        VisualizationImageFitMode fitMode)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (sourcePixelWidth <= 0 || sourcePixelHeight <= 0 ||
            frame.Width <= 0d || frame.Height <= 0d)
        {
            // Nothing measurable to reason about. Left exactly as it is rather
            // than guessed at: a zero here means the record was never filled in,
            // and inventing a size would write a number nobody measured.
            return new VisualizationRasterPlan(
                Math.Max(1, sourcePixelWidth),
                Math.Max(1, sourcePixelHeight),
                Resample: false);
        }

        double acrossWidth = frame.Width / sourcePixelWidth;
        double acrossHeight = frame.Height / sourcePixelHeight;

        // Millimetres each SOURCE pixel covers once the image is placed.
        double millimetresPerSourcePixel = fitMode == VisualizationImageFitMode.CenterCrop
            ? Math.Max(acrossWidth, acrossHeight)
            : Math.Min(acrossWidth, acrossHeight);

        // 🔴 THERE WAS A SECOND GUARD HERE AND IT WAS DEAD. An explicit «at or
        // below the budget, return unchanged» test sat above this line, and a
        // mutation that deleted it changed no answer at all: when the source is
        // already coarse enough the factor is at least one, so the integer
        // comparison at the end returns the source anyway. Two gates for one
        // decision means the experiment cannot tell which one is load-bearing -
        // so there is one, and it is the one that also settles the rounding
        // boundary.
        double factor = millimetresPerSourcePixel / MillimetresPerPixel;

        // Both dimensions by the SAME factor. A frame-shaped resize would squash
        // the photograph, and the fit mode - not this method - decides what is
        // cropped.
        int width = Math.Max(1, (int)Math.Ceiling(sourcePixelWidth * factor));
        int height = Math.Max(1, (int)Math.Ceiling(sourcePixelHeight * factor));

        // 🔴 THE DECISION IS MADE ON THE PIXEL COUNT, NOT ON THE DENSITY - AND
        // THE DIFFERENCE IS A BUILD THAT NEVER SETTLES. PixelsAcross rounds UP,
        // so an image prepared to exactly the budget comes back marginally DENSER
        // than the budget; a density comparison then says «resample» about a file
        // that is already correct, for ever, and the prepared-file cache never
        // once reports a hit. Comparing the counts is exact integer arithmetic
        // with no epsilon to tune: if the target is not smaller, there is no work.
        // 🔴 AND THERE WAS A THIRD DEAD GUARD - A Math.Min CLAMP ON EACH DIMENSION.
        // It could never bind: both dimensions are scaled by the SAME factor, so
        // either the factor is under one and both results are at most the source,
        // or it is at least one and the line above returns the source untouched.
        // Sabotage removed the clamp and no answer moved. Two dead gates in one
        // fresh function is what a mutation pass is for - each looked like care.
        return width >= sourcePixelWidth && height >= sourcePixelHeight
            ? new VisualizationRasterPlan(sourcePixelWidth, sourcePixelHeight, Resample: false)
            : new VisualizationRasterPlan(width, height, Resample: true);
    }
}
