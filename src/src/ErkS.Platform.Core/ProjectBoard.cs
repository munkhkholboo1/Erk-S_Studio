namespace ErkS.Platform.Core;

/// <summary>
/// A rectangle on a board, in millimetres from its top-left corner. The board
/// is measured rather than pixelled because everything placed on it - a
/// drawing, a scale bar, a printed sheet - is a physical size before it is
/// anything else.
/// </summary>
public readonly record struct BoardRectMm(
    double LeftMm,
    double TopMm,
    double WidthMm,
    double HeightMm)
{
    public double RightMm => LeftMm + WidthMm;

    public double BottomMm => TopMm + HeightMm;
}

/// <summary>
/// The cells an element occupies. Boards are laid out on a grid rather than by
/// dragging: a competition board's tidiness is almost entirely alignment, and
/// forty hand-placed elements never align.
/// </summary>
public readonly record struct BoardGridSpan(
    int Column,
    int ColumnSpan,
    int Row,
    int RowSpan);

/// <summary>
/// The grid every element on a series of boards is placed against. It belongs
/// to the series rather than to one board, because boards in a submission have
/// to look like each other.
/// </summary>
public sealed class BoardGrid
{
    public double MarginLeftMm { get; set; } = 20;

    public double MarginTopMm { get; set; } = 20;

    public double MarginRightMm { get; set; } = 20;

    public double MarginBottomMm { get; set; } = 20;

    public int Columns { get; set; } = 12;

    public int Rows { get; set; } = 12;

    public double ColumnGutterMm { get; set; } = 6;

    public double RowGutterMm { get; set; } = 6;

    public void Normalize()
    {
        MarginLeftMm = NonNegative(MarginLeftMm, 20);
        MarginTopMm = NonNegative(MarginTopMm, 20);
        MarginRightMm = NonNegative(MarginRightMm, 20);
        MarginBottomMm = NonNegative(MarginBottomMm, 20);
        ColumnGutterMm = NonNegative(ColumnGutterMm, 6);
        RowGutterMm = NonNegative(RowGutterMm, 6);
        Columns = Math.Max(1, Columns);
        Rows = Math.Max(1, Rows);
    }

    public BoardGrid Clone() => new()
    {
        MarginLeftMm = MarginLeftMm,
        MarginTopMm = MarginTopMm,
        MarginRightMm = MarginRightMm,
        MarginBottomMm = MarginBottomMm,
        Columns = Columns,
        Rows = Rows,
        ColumnGutterMm = ColumnGutterMm,
        RowGutterMm = RowGutterMm,
    };

    private static double NonNegative(double value, double fallback) =>
        double.IsFinite(value) && value >= 0 ? value : fallback;
}

/// <summary>
/// Turns a span of grid cells into the rectangle it occupies. Kept apart from
/// the drawing, like the portfolio's placement geometry, so what the grid
/// promises - cells that tile the board exactly, with the gutter between them
/// and nothing lost to rounding - can be asserted directly.
/// </summary>
public static class BoardGridGeometry
{
    /// <summary>
    /// The rectangle a span occupies, or null if the span falls outside the
    /// grid or the board is too small to hold it.
    ///
    /// A span that does not fit returns nothing rather than being clamped into
    /// range. Clamping would place the element somewhere plausible and say
    /// nothing, and an element silently moved is worse on a printed board than
    /// one visibly missing.
    /// </summary>
    public static BoardRectMm? Resolve(
        BoardGrid grid,
        double boardWidthMm,
        double boardHeightMm,
        BoardGridSpan span)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (span.ColumnSpan < 1 || span.RowSpan < 1 ||
            span.Column < 0 || span.Row < 0 ||
            span.Column + span.ColumnSpan > grid.Columns ||
            span.Row + span.RowSpan > grid.Rows)
        {
            return null;
        }

        if (!double.IsFinite(boardWidthMm) || !double.IsFinite(boardHeightMm))
            return null;

        double contentWidth = boardWidthMm - grid.MarginLeftMm - grid.MarginRightMm;
        double contentHeight = boardHeightMm - grid.MarginTopMm - grid.MarginBottomMm;
        double columnWidth =
            (contentWidth - grid.ColumnGutterMm * (grid.Columns - 1)) / grid.Columns;
        double rowHeight =
            (contentHeight - grid.RowGutterMm * (grid.Rows - 1)) / grid.Rows;
        if (columnWidth <= 0 || rowHeight <= 0)
            return null;

        return new BoardRectMm(
            grid.MarginLeftMm + span.Column * (columnWidth + grid.ColumnGutterMm),
            grid.MarginTopMm + span.Row * (rowHeight + grid.RowGutterMm),
            span.ColumnSpan * columnWidth + (span.ColumnSpan - 1) * grid.ColumnGutterMm,
            span.RowSpan * rowHeight + (span.RowSpan - 1) * grid.RowGutterMm);
    }

    /// <summary>The whole area inside the margins.</summary>
    public static BoardRectMm? Content(
        BoardGrid grid,
        double boardWidthMm,
        double boardHeightMm) =>
        Resolve(
            grid,
            boardWidthMm,
            boardHeightMm,
            new BoardGridSpan(0, grid?.Columns ?? 1, 0, grid?.Rows ?? 1));
}

public static class BoardElementKinds
{
    /// <summary>
    /// A framed area holding one piece of the project's material, or nothing
    /// yet. The catalogue of element kinds is deliberately closed; a board is a
    /// competition board and not a drawing program.
    /// </summary>
    public const string Card = "Card";
}

/// <summary>
/// One thing placed on a board.
///
/// A card cites an asset rather than owning one, and may cite nothing at all:
/// the layout is made before the content arrives, so an empty card is a state
/// of the design rather than a fault in it.
/// </summary>
public sealed class BoardElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Kind { get; set; } = BoardElementKinds.Card;

    public int Column { get; set; }

    public int ColumnSpan { get; set; } = 1;

    public int Row { get; set; }

    public int RowSpan { get; set; } = 1;

    /// <summary>Higher draws later, so over.</summary>
    public int ZOrder { get; set; }

    public bool IsLocked { get; set; }

    public bool IsHidden { get; set; }

    /// <summary>
    /// The portfolio item this card shows. Empty means the card is still a
    /// placeholder. Citing the item rather than the file is what separates the
    /// pool of material from the composition: several cards, on several boards,
    /// can show the same asset, and the asset keeps its own link back to the
    /// source that delivered it.
    /// </summary>
    public string AssetItemId { get; set; } = "";

    /// <summary><see cref="ProjectPortfolioLayouts"/>.</summary>
    public string Layout { get; set; } = ProjectPortfolioLayouts.FitPage;

    public string Caption { get; set; } = "";

    /// <summary>
    /// The part of the source this card shows, as fractions of it. The whole
    /// source by default. This is how a page plotted at whatever size the
    /// source program allows is reduced to just its drawn area: the exporter
    /// says where that area sits, and the card shows only it.
    /// </summary>
    public double CropX { get; set; }

    public double CropY { get; set; }

    public double CropWidth { get; set; } = 1;

    public double CropHeight { get; set; } = 1;

    public double FocalPointX { get; set; } = 0.5;

    public double FocalPointY { get; set; } = 0.5;

    public BoardGridSpan Span => new(Column, ColumnSpan, Row, RowSpan);

    public bool IsPlaceholder => string.IsNullOrWhiteSpace(AssetItemId);

    public bool ShowsWholeSource =>
        CropX == 0 && CropY == 0 && CropWidth == 1 && CropHeight == 1;

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Kind = BoardElementKinds.Card;
        Column = Math.Max(0, Column);
        Row = Math.Max(0, Row);
        ColumnSpan = Math.Max(1, ColumnSpan);
        RowSpan = Math.Max(1, RowSpan);
        AssetItemId = (AssetItemId ?? "").Trim();
        Caption = (Caption ?? "").Trim();
        Layout = NormalizeLayout(Layout);
        CropX = Clamp01(CropX, 0);
        CropY = Clamp01(CropY, 0);
        CropWidth = Clamp01(CropWidth, 1);
        CropHeight = Clamp01(CropHeight, 1);
        // A crop reaching past the source would show nothing there; pull it
        // back rather than leaving a card that draws a band of emptiness.
        if (CropX + CropWidth > 1)
            CropWidth = 1 - CropX;
        if (CropY + CropHeight > 1)
            CropHeight = 1 - CropY;
        if (CropWidth <= 0)
        {
            CropX = 0;
            CropWidth = 1;
        }
        if (CropHeight <= 0)
        {
            CropY = 0;
            CropHeight = 1;
        }
        FocalPointX = Clamp01(FocalPointX, 0.5);
        FocalPointY = Clamp01(FocalPointY, 0.5);
    }

    public BoardElement Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Column = Column,
        ColumnSpan = ColumnSpan,
        Row = Row,
        RowSpan = RowSpan,
        ZOrder = ZOrder,
        IsLocked = IsLocked,
        IsHidden = IsHidden,
        AssetItemId = AssetItemId,
        Layout = Layout,
        Caption = Caption,
        CropX = CropX,
        CropY = CropY,
        CropWidth = CropWidth,
        CropHeight = CropHeight,
        FocalPointX = FocalPointX,
        FocalPointY = FocalPointY,
    };

    private static string NormalizeLayout(string? layout)
    {
        string value = (layout ?? "").Trim();
        return value.Equals(ProjectPortfolioLayouts.FullBleed, StringComparison.OrdinalIgnoreCase)
            ? ProjectPortfolioLayouts.FullBleed
            : value.Equals(ProjectPortfolioLayouts.Contain, StringComparison.OrdinalIgnoreCase)
                ? ProjectPortfolioLayouts.Contain
                : ProjectPortfolioLayouts.FitPage;
    }

    private static double Clamp01(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}

/// <summary>One board of a submission.</summary>
public sealed class ProjectBoard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The code the submission gives this board, such as "A1".</summary>
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>One-based position in the series.</summary>
    public int Order { get; set; }

    public List<BoardElement> Elements { get; set; } = [];

    /// <summary>
    /// The elements to draw, back to front. Order within a z-level follows the
    /// list, so two elements at the same level keep the order they were added.
    /// </summary>
    public IReadOnlyList<BoardElement> OrderedVisibleElements() => Elements
        .Where(element => !element.IsHidden)
        .OrderBy(element => element.ZOrder)
        .ToList();

    public void Normalize()
    {
        Elements ??= [];
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Code = (Code ?? "").Trim();
        Title = (Title ?? "").Trim();
        foreach (BoardElement element in Elements)
            element.Normalize();
    }

    public ProjectBoard Clone() => new()
    {
        Id = Id,
        Code = Code,
        Title = Title,
        Order = Order,
        Elements = Elements.Select(element => element.Clone()).ToList(),
    };
}

/// <summary>
/// A set of boards prepared together - a competition submission, or any other
/// presentation made of large composed sheets.
///
/// The size and the grid belong here rather than to a board because the boards
/// of one submission have to match: the same grid, the same margins, the same
/// proportions. Letting each board carry its own would make the commonest
/// requirement the hardest thing to keep.
/// </summary>
public sealed class ProjectBoardSeries
{
    public string Title { get; set; } = "Самбар";

    /// <summary>A0 upright, the commonest competition board.</summary>
    public double BoardWidthMm { get; set; } = 841;

    public double BoardHeightMm { get; set; } = 1189;

    public BoardGrid Grid { get; set; } = new();

    public List<ProjectBoard> Boards { get; set; } = [];

    public string LastPdfPath { get; set; } = "";

    public DateTimeOffset? LastBuiltAtUtc { get; set; }

    public IReadOnlyList<ProjectBoard> OrderedBoards() => Boards
        .OrderBy(board => board.Order)
        .ToList();

    public void Normalize()
    {
        Boards ??= [];
        Grid ??= new BoardGrid();
        Title = string.IsNullOrWhiteSpace(Title) ? "Самбар" : Title.Trim();
        // A board is not a sheet, so it is not held to the sheet formats. A
        // competition names its own size and they are rarely standard.
        if (!double.IsFinite(BoardWidthMm) || BoardWidthMm <= 0)
            BoardWidthMm = 841;
        if (!double.IsFinite(BoardHeightMm) || BoardHeightMm <= 0)
            BoardHeightMm = 1189;
        Grid.Normalize();

        List<ProjectBoard> ordered = OrderedBoards().ToList();
        for (int index = 0; index < ordered.Count; index++)
        {
            ordered[index].Order = index + 1;
            ordered[index].Normalize();
        }
        Boards = ordered;
    }

    /// <summary>The rectangle a card occupies on a board of this series.</summary>
    public BoardRectMm? Resolve(BoardElement element) =>
        element is null
            ? null
            : BoardGridGeometry.Resolve(Grid, BoardWidthMm, BoardHeightMm, element.Span);
}
