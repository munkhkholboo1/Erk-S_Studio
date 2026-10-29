namespace ErkS.Platform.Core;

/// <summary>One printed piece of a sheet: where it is cut from (mm, top-left origin) and the paper it prints on.</summary>
public sealed record SheetSplitTile(
    int Label,
    PageRectMm Clip,
    double TargetWidthMm,
    double TargetHeightMm,
    IReadOnlyList<double> VerticalGuidesMm,
    IReadOnlyList<double> HorizontalGuidesMm);

/// <summary>How a sheet splits; <see cref="Code"/> is its Erk-S format («B2», «AH1») or "" when off the grid.</summary>
public sealed record SheetSplitPlan(string Code, IReadOnlyList<SheetSplitTile> Tiles)
{
    public bool Splits => Tiles.Count > 1;
}

/// <summary>
/// The Erk-S PDF Sheet Splitter's rule (owner, 2026-10-29: the splitter moves into Studio): a sheet of
/// joined A3 modules - 1-4 columns, an optional A4 tail («H»), 1-4 rows, 12 mm shared between neighbours -
/// is cut back into its whole A3 (and A4) pieces, each with a guide in the middle of every shared band.
/// A sheet off the grid that is bigger than A3 is cut into the fewest equal pieces that fit A3.
/// </summary>
public static class SheetSplitPlanner
{
    public const double JoinMm = WorkingDrawingAlbumFormatFactory.JoinOverlapMm;
    public const double JoinHalfMm = JoinMm / 2d;
    private const double A3Long = 420d, A3Short = 297d, A4Short = 210d;
    private const double FormatToleranceMm = 3d;

    private sealed record Format(string Code, int Columns, int Rows, bool Tail, double Width, double Height);

    private static readonly IReadOnlyList<Format> Catalog = BuildCatalog();

    public static SheetSplitPlan Plan(double widthMm, double heightMm)
    {
        if (widthMm <= 0 || heightMm <= 0)
            return new SheetSplitPlan("", []);

        Format? best = null;
        bool rotated = false;
        double bestDelta = double.MaxValue;
        foreach (Format format in Catalog)
        {
            double straight = Math.Abs(widthMm - format.Width) + Math.Abs(heightMm - format.Height);
            double turned = Math.Abs(widthMm - format.Height) + Math.Abs(heightMm - format.Width);
            if (straight < bestDelta)
                (best, rotated, bestDelta) = (format, false, straight);
            if (turned < bestDelta)
                (best, rotated, bestDelta) = (format, true, turned);
        }
        return best is not null && bestDelta <= FormatToleranceMm * 2
            ? OnGrid(widthMm, heightMm, best, rotated)
            : OffGrid(widthMm, heightMm);
    }

    private static SheetSplitPlan OnGrid(double widthMm, double heightMm, Format format, bool rotated)
    {
        // Piece sizes along x and y, and what each piece prints on.
        List<double> widths, heights;
        Func<int, int, (double W, double H)> paper;
        if (!rotated)
        {
            widths = [.. Enumerable.Repeat(A3Long, format.Columns), .. format.Tail ? new[] { A4Short } : []];
            heights = [.. Enumerable.Repeat(A3Short, format.Rows)];
            paper = (row, column) => column < format.Columns ? (A3Long, A3Short) : (A4Short, A3Short);
        }
        else
        {
            widths = [.. Enumerable.Repeat(A3Short, format.Rows)];
            heights = [.. Enumerable.Repeat(A3Long, format.Columns), .. format.Tail ? new[] { A4Short } : []];
            paper = (row, column) => row < format.Columns ? (A3Short, A3Long) : (A3Short, A4Short);
        }
        double nominalW = rotated ? format.Height : format.Width;
        double nominalH = rotated ? format.Width : format.Height;
        // A page a little bigger than its format keeps the format centred on it.
        double activeW = Math.Min(nominalW, widthMm), activeH = Math.Min(nominalH, heightMm);
        double x0 = Math.Max(0, widthMm - nominalW) / 2d, y0 = Math.Max(0, heightMm - nominalH) / 2d;
        double sx = activeW / nominalW, sy = activeH / nominalH;

        List<double> xs = Starts(widths), ys = Starts(heights);
        var tiles = new List<SheetSplitTile>();
        int label = 1;
        for (int row = 0; row < heights.Count; row++)
        {
            for (int column = 0; column < widths.Count; column++)
            {
                (double paperW, double paperH) = paper(row, column);
                double left = x0 + xs[column] * sx, top = y0 + ys[row] * sy;
                double right = Math.Min(widthMm, left + paperW * sx), bottom = Math.Min(heightMm, top + paperH * sy);
                left = Math.Max(0, left);
                top = Math.Max(0, top);
                var clip = new PageRectMm { X = left, Y = top, Width = right - left, Height = bottom - top };
                tiles.Add(new SheetSplitTile(
                    label++,
                    clip,
                    paperW,
                    paperH,
                    Guides(column, widths.Count, clip.Width, JoinHalfMm * sx),
                    Guides(row, heights.Count, clip.Height, JoinHalfMm * sy)));
            }
        }
        return new SheetSplitPlan(format.Code, tiles);
    }

    private static SheetSplitPlan OffGrid(double widthMm, double heightMm)
    {
        (int across, int down) = AutoGrid(widthMm, heightMm);
        double tileW = widthMm / across, tileH = heightMm / down;
        var tiles = new List<SheetSplitTile>();
        int label = 1;
        for (int row = 0; row < down; row++)
        {
            for (int column = 0; column < across; column++)
            {
                var clip = new PageRectMm { X = column * tileW, Y = row * tileH, Width = tileW, Height = tileH };
                tiles.Add(new SheetSplitTile(
                    label++,
                    clip,
                    tileW,
                    tileH,
                    Guides(column, across, tileW, JoinHalfMm),
                    Guides(row, down, tileH, JoinHalfMm)));
            }
        }
        return new SheetSplitPlan("", tiles);
    }

    private static List<double> Starts(IReadOnlyList<double> lengths)
    {
        var starts = new List<double>();
        double at = 0;
        for (int i = 0; i < lengths.Count; i++)
        {
            starts.Add(at);
            at += lengths[i] - JoinMm;
        }
        return starts;
    }

    private static IReadOnlyList<double> Guides(int index, int count, double length, double half)
    {
        if (count <= 1)
            return [];
        var guides = new List<double>();
        if (index > 0)
            guides.Add(Math.Clamp(half, 0, length));
        if (index < count - 1)
            guides.Add(Math.Max(0, length - half));
        return guides;
    }

    private static bool FitsA3(double w, double h, double tolerance = 1.2) =>
        (w <= A3Long + tolerance && h <= A3Short + tolerance) || (w <= A3Short + tolerance && h <= A3Long + tolerance);

    private static (int Across, int Down) AutoGrid(double widthMm, double heightMm)
    {
        if (FitsA3(widthMm, heightMm))
            return (1, 1);
        (int, int)? best = null;
        (int Tiles, double Penalty, int Skew) bestScore = (int.MaxValue, double.MaxValue, int.MaxValue);
        bool landscape = widthMm >= heightMm;
        for (int x = 1; x <= 16; x++)
        {
            for (int y = 1; y <= 16; y++)
            {
                if (x * y > 64)
                    continue;
                double tileW = widthMm / x, tileH = heightMm / y;
                if (!FitsA3(tileW, tileH))
                    continue;
                double shape = Math.Abs(Math.Min(tileW, tileH) - A3Short) + Math.Abs(Math.Max(tileW, tileH) - A3Long);
                double layout = Math.Abs(x / (double)y - widthMm / heightMm) * 5d;
                double orientation = (landscape && x < y) || (!landscape && y < x) ? 1d : 0d;
                var score = (x * y, shape + layout + orientation, Math.Abs(x - y));
                if (score.CompareTo(bestScore) < 0)
                    (best, bestScore) = ((x, y), score);
            }
        }
        return best ?? ((int)Math.Ceiling(widthMm / A3Long), (int)Math.Ceiling(heightMm / A3Short));
    }

    private static IReadOnlyList<Format> BuildCatalog()
    {
        var catalog = new List<Format>();
        for (int columns = 1; columns <= 4; columns++)
        {
            foreach (bool tail in new[] { false, true })
            {
                for (int rows = 1; rows <= 4; rows++)
                {
                    double width = A3Long * columns - JoinMm * (columns - 1) + (tail ? A4Short - JoinMm : 0);
                    double height = A3Short * rows - JoinMm * (rows - 1);
                    string code = $"{(char)('A' + columns - 1)}{(tail ? "H" : "")}{rows}";
                    catalog.Add(new Format(code, columns, rows, tail, width, height));
                }
            }
        }
        return catalog;
    }
}
