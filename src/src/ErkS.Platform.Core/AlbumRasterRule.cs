namespace ErkS.Platform.Core;

/// <summary>
/// The album's raster rule: how dense a raster image is allowed to be on the page.
///
/// 🔴 THE OWNER'S OWN RULE, DECLARED 2026-09-12: «яг зөв растерийн дүрэм 300dpi».
/// Not a setting of one feature and not a number invented here - it is how they
/// work. That decides where it lives: a constant named for the visualisation pages
/// would invite a second one for the next raster path, and a rule with two homes
/// is a rule that will eventually disagree with itself.
///
/// 🔴 TWO INDEPENDENT SOURCES AGREED BEFORE EITHER KNEW OF THE OTHER, which is
/// why this carries evidence and not merely authority. Measured in their own album
/// (albums/cloud/…R41-f4ca475d.pdf, 2026-09-12): every raster page in it is
/// 3507 × 2480 at A4 - exactly 300 DPI, JPEG, 0.59-1.67 MB each. The measurement
/// came from the artefact; the rule was then stated by the person who made it.
///
/// ⚠ WHAT THIS DOES NOT CLAIM, NOW MEASURED (2026-09-12). Only the visualisation
/// path consults it. The other raster paths were counted from source rather than
/// assumed, and they are not one kind of thing:
///
///   • TRANSPARENT HATCHES are not Studio's raster at all. There is no DPI decision
///     anywhere in ErkS.Platform.Pdf; the raster is made by AutoCAD's plot and
///     arrives already inside an imported page. The lever is CGA's plot settings.
///   • IMPORTED PDF PAGES pass through as vector - PdfReader.Open(… Import) and
///     XPdfForm, «a PDF page is placed as a form, so it stays vector». This rule
///     cannot reach the raster inside one without re-rasterising the whole page and
///     destroying the vector around it. Deliberately out of reach.
///   • THE PORTFOLIO AND THE BOARDS place raster with NO ceiling -
///     XImage.FromFile(item.SourcePath) and XImage.FromFile(card.SourcePath). These
///     are Studio's own writers, and they draw THE SAME FILES the visualisation
///     pages now cap: portfolio intake copies image.RelativePath straight off the
///     owner's visualisation records.
///
/// 🔴 SO THERE IS AN UNCAPPED TWIN OF THE PATH THAT WAS JUST CAPPED. A portfolio
/// page defaults to 420 × 297 mm and FullBleed fills it, so this rule asks for
/// 4961 px across; a 16k render carries about 3.1× that in each direction, near ten
/// times the pixel data. Naming it here rather than fixing it in passing: the
/// portfolio's frames come from its own writer's layouts, so applying the rule
/// there is the same shape of work as the visualisation path and not a one-liner.
///
/// ⚠ AND THERE IS A SECOND 300 IN THIS PROJECT THAT MUST NOT BE MERGED WITH THIS
/// ONE. <see cref="PreviewRenderResolution.TargetDpi"/> is also 300, and it is a
/// different question with a different answer behind it: how finely a page is
/// rasterised for the SCREEN, arrived at from how far that surface magnifies, and
/// capped to keep a display cache small. This one is about what goes into the
/// produced PDF. They may legitimately diverge - a preview could be coarsened to
/// save memory without touching print quality - so sharing a number that happens
/// to coincide would tie two unrelated decisions together. Two questions, two
/// constants; the coincidence is recorded here so nobody merges them helpfully.
/// </summary>
public static class AlbumRasterRule
{
    /// <summary>Dots per inch a placed raster image may reach.</summary>
    public const double DotsPerInch = 300d;

    public const double MillimetresPerInch = 25.4d;

    /// <summary>Millimetres one allowed pixel covers on the page.</summary>
    public static double MillimetresPerPixel => MillimetresPerInch / DotsPerInch;

    /// <summary>
    /// How many pixels a placed span of <paramref name="millimetres"/> needs.
    ///
    /// Rounded UP: landing a pixel short of the rule is a visible loss on a
    /// printed sheet and an invisible saving in the file. 395 mm is 4665.35 px, so
    /// this answers 4666 - the 4665 that circulated while the rule was being
    /// settled was the truncation.
    /// </summary>
    public static int PixelsAcross(double millimetres) =>
        millimetres <= 0d
            ? 1
            : Math.Max(1, (int)Math.Ceiling(millimetres / MillimetresPerPixel));

    /// <summary>
    /// The JPEG quality a prepared raster image is written at.
    ///
    /// 🔴 IT BELONGS BESIDE THE DENSITY BECAUSE THE TWO DECIDE ONE THING
    /// TOGETHER. Pixels alone do not give a file size: 300 DPI at quality 40 is a
    /// blotched page and at quality 100 is several times the bytes for no visible
    /// gain. Kept apart, the next person tuning one would have no way of knowing
    /// the other had been chosen against it.
    ///
    /// 🔴 AND IT IS A MEASUREMENT, NOT A PREFERENCE. The owner's own album
    /// (albums/cloud/…R41-f4ca475d.pdf, 2026-09-12) carries its raster pages at
    /// 0.59-1.67 MB for 3507 × 2480; quality 90 on the same content measured about
    /// 1 MB an image, which lands inside that range. The number was taken from what
    /// they already ship, so the album Studio produces is the weight they are used
    /// to rather than a figure chosen here.
    ///
    /// ⚠ NOT A FILE-SIZE GUARANTEE. Quality is a compression setting, not a
    /// budget: a noisy render compresses worse than a clean one, and 26 images will
    /// not each be 1 MB. The full-album size is a measurement still owed.
    /// </summary>
    public const int JpegQuality = 90;
}
