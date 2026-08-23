namespace ErkS.Platform.Core;

/// <summary>
/// An area and the islands cut out of it - a lawn with a path across it, a
/// square with a planting bed in the middle.
///
/// The pair is kept rather than merged. Studio draws them as one path with an
/// even-odd fill, which needs no polygon arithmetic and gets the answer right:
/// grass stops at the path instead of being drawn under it.
/// </summary>
public sealed record CityGenBoardShape(
    CityGenBoardObject Outer,
    IReadOnlyList<CityGenBoardObject> Holes)
{
    public bool HasHoles => Holes.Count > 0;
}

/// <summary>
/// What arrived, said plainly enough to check against the drawing it came from.
/// </summary>
public sealed record CityGenBoardSummary(
    int ObjectCount,
    int ShapeCount,
    int HoleCount,
    /// <summary>
    /// Islands whose area is not in the file. They are drawn as shapes of their
    /// own rather than dropped, and counted here so the gap is never silent.
    /// </summary>
    int OrphanedIslandCount,
    IReadOnlyList<CityGenBoardTally> ByCategory,
    IReadOnlyList<CityGenBoardTally> ByMaterial,
    IReadOnlyList<CityGenBoardTally> ByFlow,
    bool NorthIsAssumed,
    bool OriginIsDefined,
    double WidthMetres,
    double HeightMetres);

public sealed record CityGenBoardTally(string Value, int Count);

public static class CityGenBoardComposition
{
    /// <summary>
    /// The plan as things to draw, back to front. Islands are folded into the
    /// area they belong to; everything else stands on its own.
    /// </summary>
    public static IReadOnlyList<CityGenBoardShape> Shapes(CityGenBoardManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        List<CityGenBoardObject> drawable = (manifest.Objects ?? [])
            .Where(item => item.IsDrawable && !string.IsNullOrWhiteSpace(item.Id))
            .ToList();

        var byId = new Dictionary<string, CityGenBoardObject>(StringComparer.Ordinal);
        foreach (CityGenBoardObject item in drawable)
            byId.TryAdd(item.Id, item);

        var holesByParent = new Dictionary<string, List<CityGenBoardObject>>(StringComparer.Ordinal);
        var shapes = new List<CityGenBoardObject>();
        foreach (CityGenBoardObject item in drawable)
        {
            // An island pointing at an area that is not here is still part of
            // the plan. It draws on its own rather than disappearing.
            if (item.IsIsland && byId.ContainsKey(item.ParentId))
            {
                if (!holesByParent.TryGetValue(item.ParentId, out List<CityGenBoardObject>? holes))
                    holesByParent[item.ParentId] = holes = [];
                holes.Add(item);
                continue;
            }
            shapes.Add(item);
        }

        return shapes
            .OrderBy(item => item.DrawOrder)
            .Select(item => new CityGenBoardShape(
                item,
                holesByParent.TryGetValue(item.Id, out List<CityGenBoardObject>? holes)
                    ? holes
                    : []))
            .ToList();
    }

    public static CityGenBoardSummary Summarize(CityGenBoardManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        List<CityGenBoardObject> objects = manifest.Objects ?? [];
        IReadOnlyList<CityGenBoardShape> shapes = Shapes(manifest);
        var known = new HashSet<string>(
            objects.Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => item.Id),
            StringComparer.Ordinal);

        double[] bbox = manifest.Bbox ?? [];
        bool hasBox = bbox.Length == 4 && bbox.All(double.IsFinite);

        return new CityGenBoardSummary(
            objects.Count,
            shapes.Count,
            shapes.Sum(shape => shape.Holes.Count),
            objects.Count(item => item.IsIsland && !known.Contains(item.ParentId)),
            Tally(objects, item => item.Category),
            Tally(objects, item => item.Material),
            Tally(objects, item => item.Flow),
            manifest.NorthIsAssumed,
            manifest.Origin?.IsDefined ?? false,
            hasBox ? bbox[2] - bbox[0] : 0,
            hasBox ? bbox[3] - bbox[1] : 0);
    }

    private static IReadOnlyList<CityGenBoardTally> Tally(
        IEnumerable<CityGenBoardObject> objects,
        Func<CityGenBoardObject, string> select) => objects
        .Select(item => (select(item) ?? "").Trim())
        .Select(value => value.Length == 0 ? "(хоосон)" : value)
        .GroupBy(value => value, StringComparer.Ordinal)
        .Select(group => new CityGenBoardTally(group.Key, group.Count()))
        .OrderByDescending(tally => tally.Count)
        .ThenBy(tally => tally.Value, StringComparer.Ordinal)
        .ToList();
}
