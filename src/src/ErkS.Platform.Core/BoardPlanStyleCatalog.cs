namespace ErkS.Platform.Core;

/// <summary>
/// How a surface is drawn, as a kind rather than as a picture. The board's
/// template owns what each one actually looks like; this is only the vocabulary
/// they are named in.
/// </summary>
public static class PlanFillPatterns
{
    public const string None = "None";

    public const string Solid = "Solid";

    /// <summary>Tufts. The one the whole exercise started from.</summary>
    public const string Grass = "Grass";

    public const string Water = "Water";

    public const string Gravel = "Gravel";

    /// <summary>Jointed paving - a walkway, a square.</summary>
    public const string Paving = "Paving";

    /// <summary>Ruled lines, for anything that needs to read as built surface.</summary>
    public const string Hatch = "Hatch";
}

/// <summary>
/// One entry of the drawing vocabulary: the wash under a surface, the pattern
/// over it, the line around it, and what to call it in a legend.
///
/// The colours here are deliberately plain. They are the placeholder the
/// architecture needs in order to prove it can swap them, not a design - the
/// template library replaces the lot of them later, and nothing outside this
/// file should know what any particular surface looks like.
/// </summary>
public sealed record PlanStyle(
    string Key,
    string Label,
    string FillPattern,
    string FillColorHex,
    string PatternColorHex,
    string OutlineColorHex,
    double OutlineWidthMm)
{
    /// <summary>
    /// True when nothing in the plan's classification was recognised. Such a
    /// surface is still drawn and still named in the legend: a shape that
    /// quietly disappeared because Studio did not know its category would be
    /// the worst kind of failure on a printed board.
    /// </summary>
    public bool IsUnrecognised => Key.Length == 0;
}

/// <summary>
/// Decides how one classified outline is drawn.
///
/// The lookup runs from the most specific thing CityGen knows to the vaguest -
/// subtype, then material, then flow, then category - and every level is
/// optional. That order is what makes the vocabulary open: CityGen can add
/// <c>bioswale</c> or <c>green-roof</c> to the subtype slot whenever it can
/// draw one, and until Studio has an entry for it the surface simply resolves
/// a level down and is drawn as the green area it is. A new value is never a
/// breakage, only a missed refinement.
/// </summary>
public static class BoardPlanStyleCatalog
{
    /// <summary>The order the classification is consulted in, most specific first.</summary>
    public static IReadOnlyList<string> ResolutionOrder { get; } =
        ["subtype", "material", "flow", "category"];

    public static PlanStyle Unrecognised { get; } = new(
        Key: "",
        Label: "Тодорхойлогдоогүй",
        FillPattern: PlanFillPatterns.Solid,
        FillColorHex: "#E4E6EA",
        PatternColorHex: "#B9BDC6",
        OutlineColorHex: "#8A9099",
        OutlineWidthMm: 0.25);

    private static readonly Dictionary<string, PlanStyle> BySubtype = Build(
    [
        new("lawn", "Зүлэг", PlanFillPatterns.Grass, "#D8E8C8", "#7FA45C", "#7FA45C", 0.25),
        new("tree", "Мод", PlanFillPatterns.Solid, "#BFD9A6", "#4E7A36", "#4E7A36", 0.3),
        new("green-area", "Ногоон байгууламж", PlanFillPatterns.Grass, "#D8E8C8", "#7FA45C", "#7FA45C", 0.25),
    ]);

    private static readonly Dictionary<string, PlanStyle> ByMaterial = Build(
    [
        new("grass", "Зүлэг", PlanFillPatterns.Grass, "#D8E8C8", "#7FA45C", "#7FA45C", 0.25),
        new("asphalt", "Асфальт", PlanFillPatterns.Solid, "#D2D5DA", "#AEB3BB", "#9BA1AA", 0.25),
        new("concrete", "Бетон", PlanFillPatterns.Solid, "#E2E4E7", "#C2C6CC", "#AAB0B8", 0.25),
        new("stone", "Чулуун хучилт", PlanFillPatterns.Paving, "#E8E2D8", "#BFB5A4", "#AFA593", 0.25),
        new("sand", "Элс", PlanFillPatterns.Gravel, "#F0E6CE", "#C9B489", "#BCA87E", 0.25),
        new("gravel", "Хайрга", PlanFillPatterns.Gravel, "#E6E2DA", "#B4ADA0", "#A49D90", 0.25),
        new("water", "Ус", PlanFillPatterns.Water, "#CFE2F0", "#6E9EC4", "#6E9EC4", 0.25),
        new("soil", "Хөрс", PlanFillPatterns.Solid, "#E6DCCE", "#C0AE96", "#B2A088", 0.25),
    ]);

    /// <summary>
    /// Where a flow says something the material and category cannot.
    ///
    /// Most of these are markings rather than surfaces: a lane divider is a
    /// painted line, and filling its outline would lay a band of road colour
    /// across the carriageway. On a real road drawing two thirds of the objects
    /// are markings, so drawing them as areas is not a detail.
    /// </summary>
    private static readonly Dictionary<string, PlanStyle> ByFlow = Build(
    [
        new("ROAD_MEDIAN", "Төв зурвас", PlanFillPatterns.Solid, "#DCE4D6", "#A8B79C", "#98A78C", 0.25),
        new("ROAD_CURB", "Хашлага", PlanFillPatterns.None, "#00000000", "#00000000", "#8A9099", 0.35),
        new("ROAD_LANE_DIVIDER", "Эгнээний зураас", PlanFillPatterns.None, "#00000000", "#00000000", "#F4F5F7", 0.4),
        new("ROAD_LANE_LIMIT", "Эгнээний хязгаар", PlanFillPatterns.None, "#00000000", "#00000000", "#F4F5F7", 0.4),
        new("ROAD_TURN_GUIDE", "Эргэлтийн заавар", PlanFillPatterns.None, "#00000000", "#00000000", "#F4F5F7", 0.4),
        new("WALKWAY", "Явган зам", PlanFillPatterns.Paving, "#E8E2D8", "#BFB5A4", "#AFA593", 0.25),
        new("BIKE_PATH", "Дугуйн зам", PlanFillPatterns.Solid, "#E4D8D8", "#BFA4A4", "#AF9393", 0.25),
    ]);

    /// <summary>
    /// The last resort, and in practice the busiest level: on a real masterplan
    /// most objects carry no material at all - a building has no surface - so
    /// the category is all there is to go on.
    ///
    /// Both CityGen's own names and the plainer ones are listed. A producer is
    /// free to send either, and a name nobody here knows still resolves to the
    /// neutral style rather than to nothing.
    /// </summary>
    private static readonly Dictionary<string, PlanStyle> ByCategory = Build(
    [
        new("PlannedGreenArea", "Ногоон байгууламж", PlanFillPatterns.Grass, "#D8E8C8", "#7FA45C", "#7FA45C", 0.25),
        new("PlannedRoad", "Зам", PlanFillPatterns.Solid, "#D2D5DA", "#AEB3BB", "#9BA1AA", 0.25),
        new("PlannedWalkway", "Явган зам", PlanFillPatterns.Paving, "#E8E2D8", "#BFB5A4", "#AFA593", 0.25),
        new("PlannedBuilding", "Барилга", PlanFillPatterns.Solid, "#D9D2C8", "#8C8378", "#6F675E", 0.35),
        new("PlannedWater", "Ус", PlanFillPatterns.Water, "#CFE2F0", "#6E9EC4", "#6E9EC4", 0.25),
        new("PlannedParking", "Зогсоол", PlanFillPatterns.Solid, "#DDDFE3", "#B4B8BF", "#A4A8B0", 0.25),
        new("Green", "Ногоон", PlanFillPatterns.Grass, "#D8E8C8", "#7FA45C", "#7FA45C", 0.25),
        new("Road", "Зам", PlanFillPatterns.Solid, "#D2D5DA", "#AEB3BB", "#9BA1AA", 0.25),
        new("Pedestrian", "Явган", PlanFillPatterns.Paving, "#E8E2D8", "#BFB5A4", "#AFA593", 0.25),
        new("Water", "Ус", PlanFillPatterns.Water, "#CFE2F0", "#6E9EC4", "#6E9EC4", 0.25),
        new("Building", "Барилга", PlanFillPatterns.Solid, "#D9D2C8", "#8C8378", "#6F675E", 0.35),
        new("Parking", "Зогсоол", PlanFillPatterns.Solid, "#DDDFE3", "#B4B8BF", "#A4A8B0", 0.25),
    ]);

    public static PlanStyle Resolve(CityGenBoardObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Resolve(item.Subtype, item.Material, item.Flow, item.Category);
    }

    public static PlanStyle Resolve(string? subtype, string? material, string? flow, string? category)
    {
        if (Lookup(BySubtype, subtype) is { } bySubtype)
            return bySubtype;
        if (Lookup(ByMaterial, material) is { } byMaterial)
            return byMaterial;
        if (Lookup(ByFlow, flow) is { } byFlow)
            return byFlow;
        if (Lookup(ByCategory, category) is { } byCategory)
            return byCategory;
        return Unrecognised;
    }

    /// <summary>
    /// The styles a plan actually uses, in the order they are drawn. This is
    /// what a legend is made of: what is on the board, not what the catalogue
    /// happens to contain.
    /// </summary>
    public static IReadOnlyList<PlanStyle> Legend(CityGenBoardManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var legend = new List<PlanStyle>();
        foreach (CityGenBoardShape shape in CityGenBoardComposition.Shapes(manifest))
        {
            PlanStyle style = Resolve(shape.Outer);
            // An unrecognised surface earns its own line rather than being
            // folded away, so a plan that arrived half-understood says so.
            if (seen.Add(style.IsUnrecognised ? " unrecognised" : style.Key))
                legend.Add(style);
        }
        return legend;
    }

    /// <summary>
    /// CityGen's word for "no information here". It is a stated value rather
    /// than an empty field, and it has to fall through like one - most of a
    /// real masterplan carries it, because a building has no surface material.
    /// </summary>
    private const string NoInformation = "unknown";

    private static PlanStyle? Lookup(Dictionary<string, PlanStyle> catalogue, string? value)
    {
        string key = (value ?? "").Trim();
        if (key.Length == 0 || key.Equals(NoInformation, StringComparison.OrdinalIgnoreCase))
            return null;
        return catalogue.TryGetValue(key, out PlanStyle? style) ? style : null;
    }

    private static Dictionary<string, PlanStyle> Build(IReadOnlyList<PlanStyle> styles)
    {
        var catalogue = new Dictionary<string, PlanStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (PlanStyle style in styles)
            catalogue[style.Key] = style;
        return catalogue;
    }
}
