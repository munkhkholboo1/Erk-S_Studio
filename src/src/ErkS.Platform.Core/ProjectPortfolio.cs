namespace ErkS.Platform.Core;

public static class ProjectPortfolioItemKinds
{
    /// <summary>A project visualization image.</summary>
    public const string Image = "Image";

    /// <summary>An image or PDF the user added straight to the portfolio.</summary>
    public const string Document = "Document";

    /// <summary>One page of the project's album.</summary>
    public const string AlbumPage = "AlbumPage";

    /// <summary>An authored CAD page a sheet package marked for the portfolio.</summary>
    public const string CadPage = "CadPage";
}

/// <summary>
/// How big the pages of the built presentation are.
/// </summary>
public static class ProjectPortfolioPageSizeModes
{
    /// <summary>Every page is the one size the portfolio was given.</summary>
    public const string Fixed = "Fixed";

    /// <summary>
    /// Each page keeps the size of the drawing it shows, so the document holds
    /// pages of several sizes. Choosing a large sheet for a drawing is a
    /// decision about how it should be seen, and this keeps that decision.
    /// </summary>
    public const string SourcePage = "SourcePage";
}

public static class ProjectPortfolioLayouts
{
    /// <summary>The item fills the whole page and is cropped to it.</summary>
    public const string FullBleed = "FullBleed";

    /// <summary>The item is fitted whole inside the page, with a margin.</summary>
    public const string Contain = "Contain";

    /// <summary>
    /// The item is fitted whole to the page edge: no margin is added and
    /// nothing is cropped. This is what an authored CAD page needs - it already
    /// carries its own margin, so a second one would frame it twice, while
    /// filling the page would cut drawing off the edges.
    /// </summary>
    public const string FitPage = "FitPage";
}

public sealed class ProjectPortfolioItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>One-based position in the portfolio.</summary>
    public int Order { get; set; }

    public string Kind { get; set; } = ProjectPortfolioItemKinds.Image;

    public string Layout { get; set; } = ProjectPortfolioLayouts.Contain;

    /// <summary>Shown in the item list; not printed.</summary>
    public string Title { get; set; } = "";

    /// <summary>Printed under or over the item. Empty prints nothing.</summary>
    public string Caption { get; set; } = "";

    /// <summary>Project-relative path of the file this item shows.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>One-based page of a multi-page PDF.</summary>
    public int SourcePageNumber { get; set; } = 1;

    /// <summary>
    /// The album page an <see cref="ProjectPortfolioItemKinds.AlbumPage"/> item
    /// was taken from, so it survives an album rebuild that moves the page.
    /// </summary>
    public string AlbumPageId { get; set; } = "";

    /// <summary>
    /// Sheet-library-style key (source identity + sheet id) of the authored
    /// page a <see cref="ProjectPortfolioItemKinds.CadPage"/> item was imported
    /// from, so a re-export replaces this item instead of appending a duplicate.
    /// </summary>
    public string SourceSheetKey { get; set; } = "";

    /// <summary>
    /// The description the source last gave this page, beside the caption that
    /// is printed. Comparing the two tells a caption the user wrote from one
    /// the source set, so a description added or changed at the source reaches
    /// a page nobody has captioned, and never touches one they have.
    /// </summary>
    public string SourceCaption { get; set; } = "";

    /// <summary>Export time of the package this item was last imported from.</summary>
    public DateTimeOffset? SourceExportedAtUtc { get; set; }

    /// <summary>
    /// The title the source last gave this page. It is kept beside the shown
    /// title so a re-import can tell a name the user changed from one it set
    /// itself, and leave the user's wording alone.
    /// </summary>
    public string SourceTitle { get; set; } = "";

    /// <summary>
    /// When the source stopped offering this page - a full snapshot arrived
    /// without it. The item stays: a portfolio is the project's own
    /// presentation and does not lose material because a drawing was
    /// reorganised. Recording it lets the page say so for itself.
    /// </summary>
    public DateTimeOffset? MissingFromSourceSinceUtc { get; set; }

    /// <summary>
    /// When the user took this page out of the portfolio. An imported page is
    /// hidden rather than deleted, so the next export does not quietly put it
    /// back and so taking one out can be undone.
    /// </summary>
    public DateTimeOffset? RemovedAtUtc { get; set; }

    public bool IsRemoved => RemovedAtUtc.HasValue;

    /// <summary>Where a cropped item is centred, 0..1 of the source.</summary>
    public double FocalPointX { get; set; } = 0.5;

    public double FocalPointY { get; set; } = 0.5;

    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ProjectPortfolioItem Clone() => new()
    {
        Id = Id,
        Order = Order,
        Kind = Kind,
        Layout = Layout,
        Title = Title,
        Caption = Caption,
        RelativePath = RelativePath,
        SourcePageNumber = SourcePageNumber,
        AlbumPageId = AlbumPageId,
        SourceSheetKey = SourceSheetKey,
        SourceExportedAtUtc = SourceExportedAtUtc,
        SourceTitle = SourceTitle,
        SourceCaption = SourceCaption,
        MissingFromSourceSinceUtc = MissingFromSourceSinceUtc,
        RemovedAtUtc = RemovedAtUtc,
        FocalPointX = FocalPointX,
        FocalPointY = FocalPointY,
        AddedAtUtc = AddedAtUtc,
    };
}

/// <summary>
/// A presentation assembled from the project's own material - pages of its
/// album, its imagery, and files added straight to it.
///
/// It is deliberately not an album: it carries no sheet frame, no title block
/// and no drawing standard, and it is local to the device. The album remains
/// the record of the design; this is how that design is shown.
/// </summary>
public sealed class ProjectPortfolio
{
    public string Title { get; set; } = "Портфолио";

    public double PageWidthMm { get; set; } = 420;

    public double PageHeightMm { get; set; } = 297;

    /// <summary>
    /// <see cref="ProjectPortfolioPageSizeModes"/>. Fixed by default, so a
    /// portfolio behaves as it always has until someone chooses otherwise.
    /// </summary>
    public string PageSizeMode { get; set; } = ProjectPortfolioPageSizeModes.Fixed;

    public bool UsesSourcePageSize => PageSizeMode.Equals(
        ProjectPortfolioPageSizeModes.SourcePage,
        StringComparison.OrdinalIgnoreCase);

    public List<ProjectPortfolioItem> Items { get; set; } = [];

    public string LastPdfPath { get; set; } = "";

    public string LastPdfSha256 { get; set; } = "";

    public int LastPageCount { get; set; }

    public DateTimeOffset? LastBuiltAtUtc { get; set; }

    public IReadOnlyList<ProjectPortfolioItem> OrderedItems() => Items
        .OrderBy(item => item.Order)
        .ThenBy(item => item.AddedAtUtc)
        .ToList();

    /// <summary>
    /// The pages the portfolio actually shows. A page the user took out keeps
    /// its place and its wording, but is not presented or printed.
    /// </summary>
    public IReadOnlyList<ProjectPortfolioItem> OrderedVisibleItems() => OrderedItems()
        .Where(item => !item.IsRemoved)
        .ToList();

    public IReadOnlyList<ProjectPortfolioItem> OrderedRemovedItems() => OrderedItems()
        .Where(item => item.IsRemoved)
        .ToList();

    public void Normalize()
    {
        Items ??= [];
        Title = string.IsNullOrWhiteSpace(Title) ? "Портфолио" : Title.Trim();
        if (PageWidthMm <= 0 || !double.IsFinite(PageWidthMm))
            PageWidthMm = 420;
        if (PageHeightMm <= 0 || !double.IsFinite(PageHeightMm))
            PageHeightMm = 297;
        PageSizeMode = UsesSourcePageSize
            ? ProjectPortfolioPageSizeModes.SourcePage
            : ProjectPortfolioPageSizeModes.Fixed;

        List<ProjectPortfolioItem> ordered = OrderedItems().ToList();
        for (int index = 0; index < ordered.Count; index++)
        {
            ProjectPortfolioItem item = ordered[index];
            item.Order = index + 1;
            item.Id = string.IsNullOrWhiteSpace(item.Id)
                ? Guid.NewGuid().ToString("N")
                : item.Id.Trim();
            item.Kind = NormalizeKind(item.Kind);
            item.Layout = NormalizeLayout(item.Layout);
            item.Title = (item.Title ?? "").Trim();
            item.Caption = (item.Caption ?? "").Trim();
            item.RelativePath = (item.RelativePath ?? "").Trim();
            item.AlbumPageId = (item.AlbumPageId ?? "").Trim();
            item.SourceSheetKey = (item.SourceSheetKey ?? "").Trim();
            item.SourceTitle = (item.SourceTitle ?? "").Trim();
            item.SourceCaption = (item.SourceCaption ?? "").Trim();
            item.SourcePageNumber = Math.Max(1, item.SourcePageNumber);
            item.FocalPointX = Clamp01(item.FocalPointX);
            item.FocalPointY = Clamp01(item.FocalPointY);
        }
        Items = ordered;
    }

    private static string NormalizeKind(string? kind)
    {
        string value = (kind ?? "").Trim();
        return value.Equals(ProjectPortfolioItemKinds.Document, StringComparison.OrdinalIgnoreCase)
            ? ProjectPortfolioItemKinds.Document
            : value.Equals(ProjectPortfolioItemKinds.AlbumPage, StringComparison.OrdinalIgnoreCase)
                ? ProjectPortfolioItemKinds.AlbumPage
                : value.Equals(ProjectPortfolioItemKinds.CadPage, StringComparison.OrdinalIgnoreCase)
                    ? ProjectPortfolioItemKinds.CadPage
                    : ProjectPortfolioItemKinds.Image;
    }

    private static string NormalizeLayout(string? layout)
    {
        string value = (layout ?? "").Trim();
        return value.Equals(ProjectPortfolioLayouts.FullBleed, StringComparison.OrdinalIgnoreCase)
            ? ProjectPortfolioLayouts.FullBleed
            : value.Equals(ProjectPortfolioLayouts.FitPage, StringComparison.OrdinalIgnoreCase)
                ? ProjectPortfolioLayouts.FitPage
                : ProjectPortfolioLayouts.Contain;
    }

    private static double Clamp01(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
}
