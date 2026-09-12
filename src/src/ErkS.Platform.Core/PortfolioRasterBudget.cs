namespace ErkS.Platform.Core;

/// <summary>
/// The portfolio page's own geometry, in one place because two readers need it.
///
/// 🔴 THE WRITER HELD THESE PRIVATELY AND THAT WAS FINE UNTIL SOMETHING ELSE HAD TO
/// AGREE WITH IT. Working out how many pixels an image needs means working out the
/// frame it lands in, and the frame is these two numbers. Copying them into the rule
/// would give one fact two homes - and the second home is always the one that stops
/// being updated.
/// </summary>
public static class PortfolioPageGeometry
{
    /// <summary>The clear band around a contained drawing.</summary>
    public const double ContainMarginMm = 14;

    /// <summary>The strip a caption occupies under a contained drawing.</summary>
    public const double CaptionBandMm = 16;

    /// <summary>
    /// Layout names, read from the portfolio's own catalogue.
    ///
    /// 🔴 THESE WERE COPIED HERE AND THAT WAS A SECOND HOME - the same fault the
    /// comment above warns about, committed in the file that warns about it.
    /// <see cref="ProjectPortfolioLayouts"/> has held these names since the portfolio
    /// was built. A copy that outlived a rename would compute a CONTAINED frame for a
    /// page the writer then COVERS: fewer pixels than the page shows, which is the one
    /// failure direction that looks like nothing at all.
    /// </summary>
    public const string FullBleed = ProjectPortfolioLayouts.FullBleed;

    public const string FitPage = ProjectPortfolioLayouts.FitPage;

    /// <summary>
    /// The area one item is drawn into, in millimetres.
    ///
    /// FullBleed and FitPage both take the whole page and differ only in whether the
    /// drawing is scaled to COVER it or to fit INSIDE it. Anything else is contained,
    /// and loses the margin on every side plus the caption band when there is a caption.
    /// </summary>
    public static PageRectMm AreaFor(
        double pageWidthMm,
        double pageHeightMm,
        string? layout,
        bool hasCaption)
    {
        double width = Math.Max(1d, pageWidthMm);
        double height = Math.Max(1d, pageHeightMm);
        string name = (layout ?? "").Trim();

        if (name.Equals(FullBleed, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(FitPage, StringComparison.OrdinalIgnoreCase))
        {
            return new PageRectMm { X = 0, Y = 0, Width = width, Height = height };
        }

        double bottomBand = ContainMarginMm + (hasCaption ? CaptionBandMm : 0);
        return new PageRectMm
        {
            X = ContainMarginMm,
            Y = bottomBand,
            Width = Math.Max(1d, width - (ContainMarginMm * 2)),
            Height = Math.Max(1d, height - ContainMarginMm - bottomBand),
        };
    }
}

/// <summary>
/// How many pixels a portfolio page needs, under the album's own raster rule.
///
/// 🔴 THIS EXISTS BECAUSE «THE RASTER IS FIXED» WAS TRUE OF ONE WRITER OUT OF THREE.
/// The visualisation pages were capped; the portfolio draws THE SAME FILES - its
/// intake copies image.RelativePath straight off the owner's visualisation records -
/// at whatever size they happen to be. So the 16k renders came back the moment the
/// owner exported a portfolio, and the fix looked finished.
///
/// 🔴 AND THE RULE IS THE OWNER'S, NOT THE ALBUM'S. «Яг зөв растерийн дүрэм 300dpi»
/// is a rule about the product; reading it as «raster that goes into the album» was a
/// narrowing nobody was asked for. Applying it here is the rule's full reach, not an
/// extension of it.
///
/// ⚠ WHAT IT CANNOT HELP WITH: a portfolio built with the source's own page size
/// (PortfolioBuildRequest.UseSourcePageSize) makes each page as big as its drawing, so
/// the needed pixels are the source's by definition and there is nothing to reduce.
/// Studio never turns that on today - the flag defaults to false and no caller sets
/// it - but if it ever does, this rule will correctly answer «no work» and the page
/// will be as heavy as the file.
/// </summary>
public static class PortfolioRasterBudget
{
    /// <summary>
    /// What to prepare for one portfolio item.
    ///
    /// 🔴 FULL BLEED COVERS AND THEREFORE COSTS MORE. A covered drawing is scaled until
    /// it fills the page, so the parts hanging off the edges still have to exist at
    /// full density - the same asymmetry <see cref="VisualizationRasterBudget"/> was
    /// built around, and the reason the fit mode is passed through rather than assumed.
    /// </summary>
    public static VisualizationRasterPlan For(
        double pageWidthMm,
        double pageHeightMm,
        string? layout,
        bool hasCaption,
        int sourcePixelWidth,
        int sourcePixelHeight)
    {
        PageRectMm area = PortfolioPageGeometry.AreaFor(
            pageWidthMm,
            pageHeightMm,
            layout,
            hasCaption);

        bool covers = (layout ?? "").Trim()
            .Equals(PortfolioPageGeometry.FullBleed, StringComparison.OrdinalIgnoreCase);

        return VisualizationRasterBudget.For(
            sourcePixelWidth,
            sourcePixelHeight,
            area,
            covers ? VisualizationImageFitMode.CenterCrop : VisualizationImageFitMode.Contain);
    }
}
