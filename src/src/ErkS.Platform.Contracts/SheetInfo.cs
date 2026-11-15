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
/// `erks-sheet-info` v1-v3: what a general-plan sheet says besides its drawing -
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
    /// <summary>
    /// v2 (2026-10-29) adds the legend sample's paper width, dash, heads and
    /// symbol shape, so the movement legend can be drawn as CGA draws the scheme.
    /// </summary>
    /// <remarks>
    /// v3 (2026-11-16, CGA) adds each building's footprint, total area, capacity and handles, and the sport
    /// courts and parks - the ХЕТ building schedule the owner asked for, which PFA draws on its sheet.
    /// </remarks>
    public const int CurrentSchemaVersion = 3;

    public const int OldestSchemaVersion = 1;

    public static bool IsKnownVersion(int version) =>
        version >= OldestSchemaVersion && version <= CurrentSchemaVersion;

    public string Schema { get; set; } = "";

    public int SchemaVersion { get; set; }

    public string Producer { get; set; } = "";

    public DateTimeOffset? GeneratedAtUtc { get; set; }

    public string Document { get; set; } = "";

    public SheetInfoSite? Site { get; set; }

    public List<SheetInfoBasisLine> Basis { get; set; } = [];

    public List<SheetInfoBuilding> Buildings { get; set; } = [];

    /// <summary>v3: the sport courts in the plan.</summary>
    public List<SheetInfoSportCourt> SportCourts { get; set; } = [];

    /// <summary>v3: the parks (green zones) in the plan.</summary>
    public List<SheetInfoPark> Parks { get; set; } = [];

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

    /// <summary>v3: one building's footprint, m² (the row's <see cref="Count"/> multiplies it).</summary>
    public double? FootprintM2 { get; set; }

    /// <summary>v3: one building's total floor area, m².</summary>
    public double? TotalAreaM2 { get; set; }

    /// <summary>v3: one building's capacity - flats or parking places; null when it has none.</summary>
    public SheetInfoCapacity? Capacity { get; set; }

    /// <summary>v3: every drawing handle the row stands for.</summary>
    public List<string>? Handles { get; set; }
}

public sealed class SheetInfoCapacity
{
    public double Value { get; set; }

    /// <summary>"айл" or "машин".</summary>
    public string Unit { get; set; } = "";
}

public sealed class SheetInfoSportCourt
{
    public string Name { get; set; } = "";

    public double? AreaM2 { get; set; }

    public string? Handle { get; set; }
}

public sealed class SheetInfoPark
{
    public string Name { get; set; } = "";

    public string? Label { get; set; }

    public double? AreaM2 { get; set; }

    public string? ZoneId { get; set; }
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

    /// <summary>v3, <c>zoning</c> only: the labels of the zones the sub-zone row covers.</summary>
    public List<string>? Zones { get; set; }
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

    /// <summary>A head at the line's end. v1's only head; <see cref="Heads"/> says more.</summary>
    public bool? Arrow { get; set; }

    /// <summary>v2: the line's width on paper, mm - a lane band is a wide line.</summary>
    public double? WidthMm { get; set; }

    /// <summary>
    /// v2: the dash as [dash, gap] in paper mm, as the drawing's linetype has it;
    /// a zero dash is a round dot. Overrides <see cref="Dash"/>.
    /// </summary>
    public double[]? DashMm { get; set; }

    /// <summary>v2: end or both - a parking entry is driven both ways.</summary>
    public string? Heads { get; set; }

    /// <summary>v2, symbol: circle or triangle.</summary>
    public string? Shape { get; set; }
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

    /// <summary>
    /// v3: the legends a document may carry. Besides the four sheets Studio draws on, the ХЕТ's zoning and
    /// planning-phase sheets - PFA draws those legends on its own A1 sheets, Studio only reads them. Kept apart
    /// from <see cref="Slots"/> so Studio never starts drawing a panel of its own on those sheets.
    /// </summary>
    public static IReadOnlySet<string> LegendSlots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "master-plan",
        "traffic-scheme",
        "landscaping",
        "solar-study",
        "zoning",
        "planning-phase",
    };

    /// <summary>The legends that arrived with schema version 3.</summary>
    public static IReadOnlySet<string> Version3LegendSlots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "zoning",
        "planning-phase",
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

    public static IReadOnlySet<string> Heads { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "end",
        "both",
    };

    public static IReadOnlySet<string> Shapes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "circle",
        "triangle",
    };

    /// <summary>v3: what a building's capacity counts.</summary>
    public static IReadOnlySet<string> CapacityUnits { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "айл",
        "машин",
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
        if (!SheetInfoDocument.IsKnownVersion(document.SchemaVersion))
        {
            issues.Add(
                $"Sheet info schema version {document.SchemaVersion} is unsupported; " +
                $"this reader knows versions {SheetInfoDocument.OldestSchemaVersion}-{SheetInfoDocument.CurrentSchemaVersion}.");
        }

        document.Basis ??= [];
        document.Buildings ??= [];
        document.SportCourts ??= [];
        document.Parks ??= [];
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
            bool version3 = building.FootprintM2 is not null || building.TotalAreaM2 is not null ||
                            building.Capacity is not null || building.Handles is not null;
            if (version3 && document.SchemaVersion < 3)
                issues.Add($"Sheet info: building '{label}' uses footprintM2/totalAreaM2/capacity/handles, which need schema version 3.");
            CheckArea(building.FootprintM2, $"building '{label}' footprintM2", issues);
            CheckArea(building.TotalAreaM2, $"building '{label}' totalAreaM2", issues);
            if (building.Capacity is { } capacity)
            {
                if (!SheetInfoVocabulary.CapacityUnits.Contains(capacity.Unit ?? ""))
                    issues.Add($"Sheet info: building '{label}' has capacity unit '{capacity.Unit}'; known: айл, машин.");
                if (!double.IsFinite(capacity.Value) || capacity.Value < 0)
                    issues.Add($"Sheet info: building '{label}' has capacity {capacity.Value}; expected a number >= 0.");
            }
        }

        if ((document.SportCourts.Count > 0 || document.Parks.Count > 0) && document.SchemaVersion < 3)
            issues.Add("Sheet info: sportCourts/parks need schema version 3.");
        foreach (SheetInfoSportCourt court in document.SportCourts)
            CheckArea(court.AreaM2, $"sport court '{court.Name}' areaM2", issues);
        foreach (SheetInfoPark park in document.Parks)
            CheckArea(park.AreaM2, $"park '{park.Name}' areaM2", issues);

        foreach ((string slot, List<SheetInfoLegendRow> rows) in document.Legends)
        {
            if (!SheetInfoVocabulary.LegendSlots.Contains(slot))
            {
                issues.Add($"Sheet info: a legend for '{slot}' names no general-plan sheet.");
                continue;
            }
            if (SheetInfoVocabulary.Version3LegendSlots.Contains(slot) && document.SchemaVersion < 3)
                issues.Add($"Sheet info: a legend for '{slot}' needs schema version 3.");
            foreach (SheetInfoLegendRow row in rows ?? [])
            {
                string where = $"legend '{slot}' row '{row.Key}'";
                if (row.Zones is not null && slot != "zoning")
                    issues.Add($"Sheet info: {where} carries zones, which only a 'zoning' legend row has.");
                CheckStage(row.Stage, where, issues, required: false);
                SheetInfoSample sample = row.Sample ?? new SheetInfoSample();
                if (!SheetInfoVocabulary.SampleKinds.Contains(sample.Kind))
                    issues.Add($"Sheet info: {where} has sample kind '{sample.Kind}'; known: hatch, line, symbol.");
                CheckColour(sample.Color, where, issues, required: true);
                CheckColour(sample.Outline, where, issues, required: false);
                if (sample.Dash is not null && !SheetInfoVocabulary.Dashes.Contains(sample.Dash))
                    issues.Add($"Sheet info: {where} has dash '{sample.Dash}'; known: dashed, dotted.");
                CheckVersion2Sample(document.SchemaVersion, sample, where, issues);
            }
        }

        foreach (string slot in document.Notes.Keys)
        {
            if (!SheetInfoVocabulary.Slots.Contains(slot))
                issues.Add($"Sheet info: a note for '{slot}' names no general-plan sheet.");
        }
    }

    private static void CheckVersion2Sample(int version, SheetInfoSample sample, string where, List<string> issues)
    {
        bool any = sample.WidthMm is not null || sample.DashMm is not null || sample.Heads is not null || sample.Shape is not null;
        if (!any)
            return;
        if (version < 2)
        {
            // A v1 reader refuses these members by name; a v1 document carrying
            // them was written by a producer that skipped the bump.
            issues.Add($"Sheet info: {where} uses widthMm/dashMm/heads/shape, which need schema version 2.");
            return;
        }
        if (sample.WidthMm is { } width && (!double.IsFinite(width) || width <= 0 || width > 10))
            issues.Add($"Sheet info: {where} has widthMm {width}; expected 0 < widthMm <= 10.");
        if (sample.DashMm is { } dash &&
            (dash.Length != 2 || !dash.All(double.IsFinite) || dash[0] < 0 || dash[1] <= 0))
        {
            issues.Add($"Sheet info: {where} has dashMm [{string.Join(", ", dash)}]; expected [dash >= 0, gap > 0].");
        }
        if (sample.Heads is not null && !SheetInfoVocabulary.Heads.Contains(sample.Heads))
            issues.Add($"Sheet info: {where} has heads '{sample.Heads}'; known: end, both.");
        if (sample.Shape is not null && !SheetInfoVocabulary.Shapes.Contains(sample.Shape))
            issues.Add($"Sheet info: {where} has shape '{sample.Shape}'; known: circle, triangle.");
    }

    private static void CheckArea(double? area, string where, List<string> issues)
    {
        if (area is { } value && (!double.IsFinite(value) || value < 0))
            issues.Add($"Sheet info: {where} is {value}; expected a number >= 0.");
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
