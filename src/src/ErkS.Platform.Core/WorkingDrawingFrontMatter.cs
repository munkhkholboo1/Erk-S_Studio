using System.Text;
using System.Text.RegularExpressions;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// What Studio writes into the front-matter frames of the working drawings'
/// 01 «ЕРӨНХИЙ ХЭСЭГ»: the explanatory note, the set's composition and the
/// technical indicators. The drawing list needs no data of its own - it is the
/// album.
/// </summary>
/// <remarks>
/// It belongs to the album, not to the project foundation: it is the content
/// of one album's page, written for that set of drawings.
/// </remarks>
public sealed class WorkingDrawingFrontMatter
{
    /// <summary>
    /// The note as plain text. A line written wholly in capitals is a heading;
    /// a blank line separates paragraphs. «[⚠ …]» marks what the owner still
    /// has to fill, and is printed as it stands so it cannot pass unnoticed.
    /// </summary>
    public string ExplanatoryNote { get; set; } = "";

    public List<WorkingDrawingCompositionRow> Composition { get; set; } = [];

    public List<WorkingDrawingIndicatorRow> Indicators { get; set; } = [];

    /// <summary>
    /// Where each block stands on the 01 and how it is set in columns, as the
    /// owner arranged it in the layout editor. A block with no entry takes its
    /// default (<see cref="WorkingDrawingFrontMatterPlacement"/>).
    /// </summary>
    public List<FrontMatterBlockLayout> Layout { get; set; } = [];

    /// <summary>
    /// Parts of the 01's own drawing the owner moved to make room - the site
    /// plan, the elevation. The source PDF is untouched: the part is drawn
    /// again, vector, at its new place and its old place is cleared.
    /// </summary>
    public List<SourceRegionMove> SourceMoves { get; set; } = [];

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ExplanatoryNote) && Composition.Count == 0 && Indicators.Count == 0;

    public WorkingDrawingFrontMatter Clone() => new()
    {
        ExplanatoryNote = ExplanatoryNote,
        Composition = Composition.Select(row => row.Clone()).ToList(),
        Indicators = Indicators.Select(row => row.Clone()).ToList(),
        Layout = Layout.Select(block => block.Clone()).ToList(),
        SourceMoves = SourceMoves.Select(move => move.Clone()).ToList(),
    };

    public void Normalize()
    {
        ExplanatoryNote = (ExplanatoryNote ?? "").Replace("\r\n", "\n").Trim();
        Composition = (Composition ?? [])
            .Where(row => row is not null)
            .Select(row => new WorkingDrawingCompositionRow
            {
                Mark = (row.Mark ?? "").Trim(),
                Name = (row.Name ?? "").Trim(),
                SheetCount = (row.SheetCount ?? "").Trim(),
            })
            .Where(row => row.Mark.Length > 0 || row.Name.Length > 0 || row.SheetCount.Length > 0)
            .ToList();
        Indicators = (Indicators ?? [])
            .Where(row => row is not null)
            .Select(row => new WorkingDrawingIndicatorRow
            {
                Number = (row.Number ?? "").Trim(),
                Name = (row.Name ?? "").Trim(),
                Unit = (row.Unit ?? "").Trim(),
                Value = (row.Value ?? "").Trim(),
            })
            .Where(row => row.Number.Length > 0 || row.Name.Length > 0 || row.Value.Length > 0)
            .ToList();
        Layout = (Layout ?? [])
            .Where(block => block is not null && SheetFrontMatterKinds.IsKnown(block.Kind))
            .GroupBy(block => block.Kind.Trim(), StringComparer.Ordinal)
            .Select(group => group.Last().Normalized())
            .ToList();
        SourceMoves = (SourceMoves ?? [])
            .Where(move => move is not null && move.Width >= 1 && move.Height >= 1)
            .Select(move => move.Clone())
            .ToList();
    }
}

/// <summary>
/// A rectangle of the source sheet (millimetres from the top-left) drawn again
/// moved by (OffsetX, OffsetY).
/// </summary>
/// <remarks>
/// Owner, 2026-10-20: «цаад зургаа яаж зөөх вэ» - the Revit site plan and
/// elevation stood where the explanatory note now goes.
/// </remarks>
public sealed class SourceRegionMove
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    public SourceRegionMove Clone() => new()
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
    };
}

/// <summary>
/// One front-matter block as the owner placed it: its rectangle on the page
/// (millimetres from the top-left) and its columns.
/// </summary>
public sealed class FrontMatterBlockLayout
{
    public const int MaxColumns = 6;
    public const double MinSizeMm = 10;

    public string Kind { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Columns { get; set; } = 1;
    public double ColumnGapMm { get; set; } = 5;

    public FrontMatterBlockLayout Clone() => new()
    {
        Kind = Kind,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Columns = Columns,
        ColumnGapMm = ColumnGapMm,
    };

    public FrontMatterBlockLayout Normalized() => new()
    {
        Kind = (Kind ?? "").Trim(),
        X = Math.Max(0, X),
        Y = Math.Max(0, Y),
        Width = Math.Max(MinSizeMm, Width),
        Height = Math.Max(MinSizeMm, Height),
        Columns = Math.Clamp(Columns, 1, MaxColumns),
        ColumnGapMm = Math.Clamp(ColumnGapMm, 0, 50),
    };

    /// <summary>The width of one column.</summary>
    public double ColumnWidthMm =>
        Math.Max(0, (Width - ColumnGapMm * (Math.Max(1, Columns) - 1)) / Math.Max(1, Columns));
}

/// <summary>
/// Where the 01's front-matter blocks stand by default, and where they stand
/// once the owner moved them.
/// </summary>
/// <remarks>
/// 🔴 OWNER, 2026-10-20: «тайлбар бичгийг булангийн хүснэгтийн дээр
/// байрлуул. давхцлууд ажиглагдаад байна» - the note in Revit's middle frame
/// and the two tables beside the elevation overlapped the drawings. By default
/// the right column above the corner table is the front matter's: the note
/// directly on the corner table, set in two columns, with the composition and
/// the indicators above it; the drawing list keeps Revit's frame. Every block
/// can then be moved, resized and set in columns in the layout editor.
/// </remarks>
public static class WorkingDrawingFrontMatterPlacement
{
    public const double StackGapMm = 3;
    public const double TopMarginMm = 5;
    public const int DefaultNoteColumns = 2;

    /// <summary>Where the drawing area begins below the sheet header.</summary>
    public static double InnerTop(WorkingDrawingPageRegions regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return Math.Max(
            regions.EtalonInnerFrame.Y,
            regions.SheetTitleArea.Y + regions.SheetTitleArea.Height);
    }

    /// <summary>The source's reserved frames by kind.</summary>
    public static Dictionary<string, PageRectSpec> ReservedFrames(SheetPackageEntry? entry) =>
        (entry?.FrontMatterAreas ?? [])
            .Where(frame => frame?.Area is not null && SheetFrontMatterKinds.IsKnown(frame.Kind))
            .GroupBy(frame => frame.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Area!, StringComparer.Ordinal);

    /// <param name="frames">The frames the source reserved (kind -> rectangle).</param>
    /// <param name="corner">The corner table Studio draws.</param>
    /// <param name="innerTopMm">Where the drawing area begins below the sheet header.</param>
    public static IReadOnlyList<FrontMatterBlockLayout> Resolve(
        IReadOnlyDictionary<string, PageRectSpec> frames,
        PageRectMm corner,
        double innerTopMm,
        IReadOnlyList<FrontMatterBlockLayout>? saved)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(corner);
        var byKind = (saved ?? [])
            .Where(block => block is not null)
            .GroupBy(block => block.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Normalized(), StringComparer.Ordinal);
        var result = new List<FrontMatterBlockLayout>();
        if (!StackFits(frames, corner, innerTopMm, byKind))
        {
            // A sheet with no room above its corner table keeps the source's
            // frames, unless the owner placed the block.
            foreach ((string kind, PageRectSpec frame) in frames)
            {
                result.Add(byKind.TryGetValue(kind, out FrontMatterBlockLayout? own)
                    ? own
                    : new FrontMatterBlockLayout { Kind = kind, X = frame.X, Y = frame.Y, Width = frame.Width, Height = frame.Height });
            }
            return InDrawingOrder(result, saved);
        }

        // The right column above the corner table, top-down: composition,
        // indicators, then the note down to the corner table.
        double top = innerTopMm + TopMarginMm;
        double y = top;
        foreach (string kind in new[] { SheetFrontMatterKinds.AlbumComposition, SheetFrontMatterKinds.TechnicalIndicators })
        {
            if (!frames.TryGetValue(kind, out PageRectSpec? frame))
                continue;
            if (byKind.TryGetValue(kind, out FrontMatterBlockLayout? placed))
            {
                result.Add(placed);
                continue;
            }
            result.Add(new FrontMatterBlockLayout
            {
                Kind = kind,
                X = corner.X,
                Y = y,
                Width = corner.Width,
                Height = frame.Height,
            });
            y += frame.Height + StackGapMm;
        }

        if (frames.ContainsKey(SheetFrontMatterKinds.ExplanatoryNote))
        {
            result.Add(byKind.TryGetValue(SheetFrontMatterKinds.ExplanatoryNote, out FrontMatterBlockLayout? note)
                ? note
                : new FrontMatterBlockLayout
                {
                    Kind = SheetFrontMatterKinds.ExplanatoryNote,
                    X = corner.X,
                    Y = y,
                    Width = corner.Width,
                    Height = Math.Max(FrontMatterBlockLayout.MinSizeMm, corner.Y - StackGapMm - y),
                    Columns = DefaultNoteColumns,
                });
        }

        if (frames.TryGetValue(SheetFrontMatterKinds.DrawingList, out PageRectSpec? list))
        {
            result.Add(byKind.TryGetValue(SheetFrontMatterKinds.DrawingList, out FrontMatterBlockLayout? placedList)
                ? placedList
                : new FrontMatterBlockLayout
                {
                    Kind = SheetFrontMatterKinds.DrawingList,
                    X = list.X,
                    Y = list.Y,
                    Width = list.Width,
                    Height = list.Height,
                });
        }

        return InDrawingOrder(result, saved);
    }

    /// <summary>
    /// The order the blocks are drawn in, back to front: the ones the owner
    /// placed in the order he left them - the last is in front (owner,
    /// 2026-10-20: «мэдээллүүд нэг нэгнээ дараад хаагаад байгаа … наана
    /// цаана нь гарч болдог болгоорой») - behind them the ones he did not.
    /// </summary>
    private static IReadOnlyList<FrontMatterBlockLayout> InDrawingOrder(
        List<FrontMatterBlockLayout> blocks,
        IReadOnlyList<FrontMatterBlockLayout>? saved)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        int index = 0;
        foreach (FrontMatterBlockLayout block in saved ?? [])
        {
            if (block is not null)
                order[block.Kind] = index;
            index++;
        }
        return blocks
            .OrderBy(block => order.TryGetValue(block.Kind, out int place) ? place : -1)
            .ToList();
    }

    /// <summary>
    /// Whether the column above the corner table gives the note at least
    /// <see cref="MinNoteShare"/> of the room the source reserved for it.
    /// </summary>
    private static bool StackFits(
        IReadOnlyDictionary<string, PageRectSpec> frames,
        PageRectMm corner,
        double innerTopMm,
        IReadOnlyDictionary<string, FrontMatterBlockLayout> placed)
    {
        if (!frames.TryGetValue(SheetFrontMatterKinds.ExplanatoryNote, out PageRectSpec? note) ||
            placed.ContainsKey(SheetFrontMatterKinds.ExplanatoryNote))
        {
            return true;
        }
        double y = innerTopMm + TopMarginMm;
        foreach (string kind in new[] { SheetFrontMatterKinds.AlbumComposition, SheetFrontMatterKinds.TechnicalIndicators })
        {
            if (frames.TryGetValue(kind, out PageRectSpec? frame) && !placed.ContainsKey(kind))
                y += frame.Height + StackGapMm;
        }
        double noteHeight = corner.Y - StackGapMm - y;
        return noteHeight > 0 &&
            corner.Width * noteHeight >= MinNoteShare * note.Width * note.Height;
    }

    /// <summary>The note's room above the corner table, as a share of its reserved frame.</summary>
    public const double MinNoteShare = 0.6;
}

/// <summary>One line of «Ажлын зургийн иж бүрдлийн жагсаалт».</summary>
public sealed class WorkingDrawingCompositionRow
{
    public string Mark { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>
    /// As typed. Empty for a mark the album itself carries means "count my own
    /// pages" - the number cannot then go stale.
    /// </summary>
    public string SheetCount { get; set; } = "";

    public WorkingDrawingCompositionRow Clone() => new() { Mark = Mark, Name = Name, SheetCount = SheetCount };
}

/// <summary>One line of «Техник эдийн засгийн үзүүлэлт».</summary>
public sealed class WorkingDrawingIndicatorRow
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>Empty until somebody measured it; never borrowed from another project.</summary>
    public string Value { get; set; } = "";

    public WorkingDrawingIndicatorRow Clone() => new() { Number = Number, Name = Name, Unit = Unit, Value = Value };
}

/// <summary>
/// Which album page holds the front matter.
/// </summary>
/// <remarks>
/// A source sheet carrying the drawing-list frame takes the template's 01
/// place - the slot Studio would otherwise fill with a list page of its own -
/// and keeps its number there. The claim is written on the page and re-decided
/// from the delivered entry every time, so a source that stops reserving the
/// frames gives the place back.
/// </remarks>
public static class WorkingDrawingFrontMatterSlot
{
    public const string SlotId = "drawing-list-and-notes";

    public static bool CarriesFrontMatter(SheetPackageEntry? entry) =>
        entry?.FrontMatterAreas?.Any(frame =>
            string.Equals((frame?.Kind ?? "").Trim(), SheetFrontMatterKinds.DrawingList, StringComparison.Ordinal)) == true;

    /// <summary>Claims the 01 place for this page when its sheet reserves the list.</summary>
    public static bool Assign(AlbumDefinition album, AlbumPageDefinition page, SheetPackageEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(page);
        AlbumCompositionItem? slot = album.Composition.FirstOrDefault(item =>
            item.Id.Equals(SlotId, StringComparison.OrdinalIgnoreCase));
        if (slot is null || !CarriesFrontMatter(entry))
        {
            if (page.FrontMatterOnSheet &&
                page.TemplateSlotId.Equals(SlotId, StringComparison.OrdinalIgnoreCase))
            {
                page.TemplateSlotId = "";
            }
            page.FrontMatterOnSheet = false;
            return false;
        }

        page.FrontMatterOnSheet = true;
        page.TemplateSlotId = slot.Id;
        page.SectionId = album.Sections
            .FirstOrDefault(section => section.Title.Equals(slot.SectionTitle, StringComparison.OrdinalIgnoreCase))
            ?.Id;
        return true;
    }

    /// <summary>Whether a page of the album has taken this Studio component's place.</summary>
    public static bool IsTakenBySource(AlbumDefinition album, AlbumCompositionItem component) =>
        component.Kind == AlbumCompositionKind.Generated &&
        album.Pages.Any(page =>
            page.FrontMatterOnSheet &&
            page.TemplateSlotId.Equals(component.Id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Reads the explanatory-note draft PFR wrote in markdown
/// (<c>_shared/bn-tailbar-bichig-draft-2026-10-19.md</c>) into front matter the
/// owner then edits in Studio.
/// </summary>
/// <remarks>
/// The note is everything from the first «##» heading to the first «---»
/// rule; after it come the tables. The indicator table's LAST column is the
/// value - the one computed from the model. The column before it holds what
/// the old sheet printed, which was Grand Mark's, and is never read.
/// </remarks>
public static partial class WorkingDrawingFrontMatterDraft
{
    private const string IndicatorsHeading = "ТЕХНИК ЭДИЙН ЗАСГИЙН ҮЗҮҮЛЭЛТ";
    private const string CompositionHeading = "ИЖ БҮРДЛИЙН ЖАГСААЛТ";

    public static WorkingDrawingFrontMatter Parse(string markdown)
    {
        var matter = new WorkingDrawingFrontMatter();
        var note = new StringBuilder();
        string[] lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
        bool inNote = false;
        bool noteDone = false;
        string tablesHeading = "";
        var paragraph = new List<string>();
        // A list item runs on over the lines that follow it until a blank line
        // or another block - Markdown's own rule. Its continuation was read as
        // a new paragraph and printed as a broken line (PFR, Batnasan,
        // 2026-10-20).
        List<string>? bullet = null;

        void FlushBullet()
        {
            if (bullet is null)
                return;
            AppendLine(note, "– " + string.Join(" ", bullet));
            bullet = null;
        }

        void FlushParagraph()
        {
            FlushBullet();
            if (paragraph.Count == 0)
                return;
            AppendBlock(note, string.Join(" ", paragraph));
            paragraph.Clear();
        }

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                string heading = Clean(line[3..]);
                if (heading.Contains(IndicatorsHeading, StringComparison.OrdinalIgnoreCase) ||
                    heading.Contains(CompositionHeading, StringComparison.OrdinalIgnoreCase))
                {
                    FlushParagraph();
                    noteDone = true;
                    tablesHeading = heading;
                    continue;
                }
                if (noteDone)
                {
                    tablesHeading = heading;
                    continue;
                }
                FlushParagraph();
                inNote = true;
                AppendBlock(note, heading.ToUpperInvariant());
                continue;
            }
            if (line == "---")
            {
                FlushParagraph();
                noteDone = true;
                inNote = false;
                continue;
            }

            if (noteDone)
            {
                if (IsTableRow(line) && !IsSeparator(line))
                    ReadTableRow(matter, tablesHeading, Cells(line));
                continue;
            }
            if (!inNote)
                continue;

            if (line.Length == 0 || line.StartsWith('>'))
            {
                FlushParagraph();
                continue;
            }
            if (IsTableRow(line))
            {
                FlushParagraph();
                if (IsSeparator(line))
                    continue;
                string[] cells = Cells(line);
                if (cells.Length >= 2 && !IsHeaderRow(cells))
                    AppendLine(note, $"{cells[0]} – {string.Join(", ", cells.Skip(1))}");
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                FlushParagraph();
                bullet = [Clean(line[2..])];
                continue;
            }
            if (bullet is not null)
            {
                bullet.Add(Clean(line));
                continue;
            }
            paragraph.Add(Clean(line));
        }
        FlushParagraph();

        matter.ExplanatoryNote = note.ToString();
        matter.Normalize();
        return matter;
    }

    private static void ReadTableRow(WorkingDrawingFrontMatter matter, string heading, string[] cells)
    {
        if (IsHeaderRow(cells))
            return;
        if (heading.Contains(IndicatorsHeading, StringComparison.OrdinalIgnoreCase) && cells.Length >= 4)
        {
            matter.Indicators.Add(new WorkingDrawingIndicatorRow
            {
                Number = cells[0],
                Name = cells[1],
                Unit = Blank(cells[2]),
                Value = Value(cells[^1]),
            });
        }
        else if (heading.Contains(CompositionHeading, StringComparison.OrdinalIgnoreCase) && cells.Length >= 3)
        {
            matter.Composition.Add(new WorkingDrawingCompositionRow
            {
                Mark = Blank(cells[0]),
                Name = Blank(StripWarnings(cells[1])),
                SheetCount = Blank(cells[2]),
            });
        }
    }

    /// <summary>A computed value without its explanation; a ⚠ is no value.</summary>
    private static string Value(string cell)
    {
        string value = Blank(cell);
        int open = value.IndexOf(" (", StringComparison.Ordinal);
        return open > 0 ? value[..open].Trim() : value;
    }

    private static string Blank(string cell)
    {
        string value = cell.Trim();
        return value.Contains('⚠') || value is "—" or "-" ? "" : value;
    }

    private static string StripWarnings(string value) => WarningPattern().Replace(value, "").Trim();

    private static bool IsTableRow(string line) => line.StartsWith('|') && line.EndsWith('|');

    private static bool IsSeparator(string line) => line.Trim('|', ' ').Replace("|", "").All(c => c is '-' or ':' or ' ');

    private static bool IsHeaderRow(string[] cells) =>
        cells.Length > 0 && cells[0] is "Үзүүлэлт" or "№" or "Марк";

    private static string[] Cells(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => Clean(cell)).ToArray();

    /// <summary>Markdown emphasis and code marks removed; the words kept.</summary>
    private static string Clean(string value) =>
        value.Replace("**", "").Replace("`", "").Trim();

    private static void AppendBlock(StringBuilder note, string text)
    {
        if (note.Length > 0)
            note.Append("\n\n");
        note.Append(text.Trim());
    }

    private static void AppendLine(StringBuilder note, string text)
    {
        if (note.Length > 0)
            note.Append('\n');
        note.Append(text.Trim());
    }

    [GeneratedRegex(@"\[⚠[^\]]*\]")]
    private static partial Regex WarningPattern();
}
