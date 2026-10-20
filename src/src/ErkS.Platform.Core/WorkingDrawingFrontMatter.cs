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

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ExplanatoryNote) && Composition.Count == 0 && Indicators.Count == 0;

    public WorkingDrawingFrontMatter Clone() => new()
    {
        ExplanatoryNote = ExplanatoryNote,
        Composition = Composition.Select(row => row.Clone()).ToList(),
        Indicators = Indicators.Select(row => row.Clone()).ToList(),
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
    }
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
