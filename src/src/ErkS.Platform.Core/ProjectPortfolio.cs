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

public static class ProjectPortfolioLayouts
{
    /// <summary>The item fills the whole page and is cropped to it.</summary>
    public const string FullBleed = "FullBleed";

    /// <summary>The item is fitted whole inside the page, with a margin.</summary>
    public const string Contain = "Contain";
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

    /// <summary>Export time of the package this item was last imported from.</summary>
    public DateTimeOffset? SourceExportedAtUtc { get; set; }

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

    public List<ProjectPortfolioItem> Items { get; set; } = [];

    public string LastPdfPath { get; set; } = "";

    public string LastPdfSha256 { get; set; } = "";

    public int LastPageCount { get; set; }

    public DateTimeOffset? LastBuiltAtUtc { get; set; }

    public IReadOnlyList<ProjectPortfolioItem> OrderedItems() => Items
        .OrderBy(item => item.Order)
        .ThenBy(item => item.AddedAtUtc)
        .ToList();

    public void Normalize()
    {
        Items ??= [];
        Title = string.IsNullOrWhiteSpace(Title) ? "Портфолио" : Title.Trim();
        if (PageWidthMm <= 0 || !double.IsFinite(PageWidthMm))
            PageWidthMm = 420;
        if (PageHeightMm <= 0 || !double.IsFinite(PageHeightMm))
            PageHeightMm = 297;

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

    private static string NormalizeLayout(string? layout) =>
        (layout ?? "").Trim().Equals(
            ProjectPortfolioLayouts.FullBleed,
            StringComparison.OrdinalIgnoreCase)
            ? ProjectPortfolioLayouts.FullBleed
            : ProjectPortfolioLayouts.Contain;

    private static double Clamp01(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
}
