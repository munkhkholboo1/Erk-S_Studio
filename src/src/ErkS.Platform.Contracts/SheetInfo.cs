using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ErkS.Platform.Contracts;

/// <summary>
/// Where a sheet package carries its `erks-sheet-info` document: a file in the
/// package folder, its hash, and the document's own schema version.
/// </summary>
public sealed class SheetInfoReference
{
    public string File { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public int SchemaVersion { get; set; }
}

/// <summary>
/// `erks-sheet-info` v1: what a general-plan sheet says besides its drawing -
/// legends with quantities, the building schedule, the ТЭЗ summary, the basis
/// lines and norm text. CGA writes it beside its drawing, PFA packs it with the
/// sheets, Studio draws it (_shared/erks-sheet-info-contract-2026-10-19.md).
/// </summary>
/// <remarks>
/// The officials (БАТЛАВ/ХЯНАСАН), the land and planning-task references and
/// the site boundary are NOT here: Studio holds them, and a second copy would
/// be a second answer. `basis.land` and `basis.planningTask` arrive null.
/// </remarks>
public sealed class SheetInfoDocument
{
    public const string SchemaName = "erks-sheet-info";
    public const int CurrentSchemaVersion = 1;

    public string Schema { get; set; } = "";

    public int SchemaVersion { get; set; }

    public string Producer { get; set; } = "";

    public DateTimeOffset? GeneratedAtUtc { get; set; }

    public string Document { get; set; } = "";

    public SheetInfoSite? Site { get; set; }

    public List<SheetInfoBasisLine> Basis { get; set; } = [];

    public List<SheetInfoBuilding> Buildings { get; set; } = [];

    public List<SheetInfoSummaryRow> Summary { get; set; } = [];

    /// <summary>Legend rows by the sheet they belong to (a template slot id).</summary>
    public Dictionary<string, List<SheetInfoLegendRow>> Legends { get; set; } = [];

    /// <summary>Free text by the sheet it belongs to (a template slot id).</summary>
    public Dictionary<string, string> Notes { get; set; } = [];

    /// <summary>
    /// What CGA could not measure (e.g. "piece-region:BikeSurface:5E681E").
    /// Diagnostic only: not drawn, not a reason to refuse - the build names them,
    /// because a quantity missing a region is a quantity that is too small.
    /// </summary>
    public List<string> Refusals { get; set; } = [];
}

public sealed class SheetInfoSite
{
    public string Location { get; set; } = "";

    /// <summary>A cross-check against Studio's own boundary, not a second boundary.</summary>
    public double? AreaM2 { get; set; }

    public List<double[]>? BoundaryWgs84 { get; set; }

    public double? NorthAngleDeg { get; set; }

    public string Crs { get; set; } = "";
}

public sealed class SheetInfoBasisLine
{
    public string Key { get; set; } = "";

    /// <summary>Null means "not known": Studio draws a placeholder, never an empty line.</summary>
    public string? Text { get; set; }
}

public sealed class SheetInfoBuilding
{
    /// <summary>Null for an un-numbered group (14 underground garages).</summary>
    public string? No { get; set; }

    public string Name { get; set; } = "";

    /// <summary>"16/-1" = sixteen above ground, one below.</summary>
    public string Floors { get; set; } = "";

    public string Stage { get; set; } = "";

    public string? Handle { get; set; }

    public int Count { get; set; } = 1;
}

public sealed class SheetInfoSummaryRow
{
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    public double? Value { get; set; }

    public string Unit { get; set; } = "";
}

public sealed class SheetInfoLegendRow
{
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    public string? Stage { get; set; }

    public SheetInfoSample Sample { get; set; } = new();

    /// <summary>Clipped to the planning boundary. Null prints the row without a number.</summary>
    public double? Quantity { get; set; }

    public string? Unit { get; set; }
}

public sealed class SheetInfoSample
{
    /// <summary>hatch, line or symbol.</summary>
    public string Kind { get; set; } = "";

    public string Color { get; set; } = "";

    public string? Pattern { get; set; }

    public string? Outline { get; set; }

    /// <summary>dashed or dotted; absent for a continuous line.</summary>
    public string? Dash { get; set; }

    public bool? Arrow { get; set; }
}

public static class SheetInfoVocabulary
{
    /// <summary>The sheets a document may speak to - the general-plan template slots.</summary>
    public static IReadOnlySet<string> Slots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "master-plan",
        "traffic-scheme",
        "landscaping",
        "solar-study",
    };

    public static IReadOnlySet<string> Stages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Planned",
        "Existing",
        "UnderConstruction",
        "Demolish",
    };

    public static IReadOnlySet<string> SampleKinds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "hatch",
        "line",
        "symbol",
    };

    public static IReadOnlySet<string> Dashes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "dashed",
        "dotted",
    };
}

public sealed class SheetInfoLoadResult
{
    public SheetInfoDocument? Document { get; init; }

    public List<string> Issues { get; } = [];

    public bool IsLossless => Document is not null && Issues.Count == 0;
}

public static partial class SheetInfoReader
{
    /// <summary>
    /// Unknown members are refused, by name. A reader that drops them prints
    /// a legend with a row missing, and nobody can tell from the sheet.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SheetInfoLoadResult Load(string path)
    {
        SheetInfoDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SheetInfoDocument>(System.IO.File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var failed = new SheetInfoLoadResult();
            failed.Issues.Add($"Sheet info could not be read: {exception.Message}");
            return failed;
        }

        var result = new SheetInfoLoadResult { Document = document };
        if (document is null)
        {
            result.Issues.Add("Sheet info deserialized to null.");
            return result;
        }

        Validate(document, result.Issues);
        return result;
    }

    private static void Validate(SheetInfoDocument document, List<string> issues)
    {
        if (!string.Equals(document.Schema, SheetInfoDocument.SchemaName, StringComparison.Ordinal))
            issues.Add($"Sheet info schema '{document.Schema}' is not '{SheetInfoDocument.SchemaName}'.");
        if (document.SchemaVersion != SheetInfoDocument.CurrentSchemaVersion)
        {
            issues.Add(
                $"Sheet info schema version {document.SchemaVersion} is unsupported; " +
                $"this reader knows version {SheetInfoDocument.CurrentSchemaVersion}.");
        }

        document.Basis ??= [];
        document.Buildings ??= [];
        document.Summary ??= [];
        document.Legends ??= [];
        document.Notes ??= [];
        document.Refusals ??= [];

        foreach (SheetInfoBuilding building in document.Buildings)
        {
            string label = building.No ?? building.Name;
            CheckStage(building.Stage, $"building '{label}'", issues, required: true);
            if (building.Count < 1)
                issues.Add($"Sheet info: building '{label}' has count {building.Count}; a row counts at least one.");
        }

        foreach ((string slot, List<SheetInfoLegendRow> rows) in document.Legends)
        {
            if (!SheetInfoVocabulary.Slots.Contains(slot))
            {
                issues.Add($"Sheet info: a legend for '{slot}' names no general-plan sheet.");
                continue;
            }
            foreach (SheetInfoLegendRow row in rows ?? [])
            {
                string where = $"legend '{slot}' row '{row.Key}'";
                CheckStage(row.Stage, where, issues, required: false);
                SheetInfoSample sample = row.Sample ?? new SheetInfoSample();
                if (!SheetInfoVocabulary.SampleKinds.Contains(sample.Kind))
                    issues.Add($"Sheet info: {where} has sample kind '{sample.Kind}'; known: hatch, line, symbol.");
                CheckColour(sample.Color, where, issues, required: true);
                CheckColour(sample.Outline, where, issues, required: false);
                if (sample.Dash is not null && !SheetInfoVocabulary.Dashes.Contains(sample.Dash))
                    issues.Add($"Sheet info: {where} has dash '{sample.Dash}'; known: dashed, dotted.");
            }
        }

        foreach (string slot in document.Notes.Keys)
        {
            if (!SheetInfoVocabulary.Slots.Contains(slot))
                issues.Add($"Sheet info: a note for '{slot}' names no general-plan sheet.");
        }
    }

    private static void CheckStage(string? stage, string where, List<string> issues, bool required)
    {
        if (stage is null && !required)
            return;
        if (!SheetInfoVocabulary.Stages.Contains(stage ?? ""))
        {
            issues.Add(
                $"Sheet info: {where} has stage '{stage}'; known: Planned, Existing, UnderConstruction, Demolish.");
        }
    }

    private static void CheckColour(string? colour, string where, List<string> issues, bool required)
    {
        if (colour is null && !required)
            return;
        if (!HexColour().IsMatch(colour ?? ""))
            issues.Add($"Sheet info: {where} has colour '{colour}'; expected #RRGGBB.");
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColour();
}
