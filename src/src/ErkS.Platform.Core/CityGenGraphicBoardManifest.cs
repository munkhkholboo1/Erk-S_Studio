using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErkS.Platform.Core;

/// <summary>
/// The channel CityGen sends a general plan through for a board: classified
/// outlines in drawing metres, rather than a PDF.
///
/// It exists because a PDF cannot carry what a board needs. Every fill in the
/// plan is a solid hatch, so by the time it reaches a page the meaning is
/// already gone - a lawn and a car park are both just a painted outline. This
/// channel keeps the classification, which is what lets a board draw grass as
/// grass instead of as whatever colour the drawing happened to use.
/// </summary>
public static class CityGenGraphicBoardContract
{
    public const string Schema = "erks.citygen.graphic-board";

    public const int CurrentSchemaVersion = 1;

    public const string SidecarSuffix = ".erks-citygen-board.json";

    /// <summary>Metres. Anything else is refused rather than converted.</summary>
    public const string ExpectedUnits = "meter";

    /// <summary>Drawing space, planar. Not longitude and latitude.</summary>
    public const string ExpectedCoordinateSpace = "drawing";

    /// <summary>The drawing declares a coordinate system, so north is known.</summary>
    /// <remarks>
    /// The name says UTM and the meaning does not: it is written whenever the
    /// drawing declares a coordinate system at all. A drawing on a
    /// Gauss-Kruger grid arrives labelled "utm-grid" and nobody notices, which
    /// PFA pointed out while settling the vocabulary for their own viewport
    /// field on 2026-08-30. The value is what CityGen writes today, so it stays
    /// until both sides change together; the name is left alone rather than
    /// corrected here, because a constant whose name disagrees with its wire
    /// value would be worse than one that disagrees with its meaning.
    /// </remarks>
    public const string NorthFromUtmGrid = "utm-grid";

    /// <summary>
    /// The same thing said the way AutoCAD says it - north from a projected
    /// grid, whichever projection. Accepted, never written by anything here.
    /// </summary>
    /// <remarks>
    /// Accepted in advance on purpose. <see cref="CityGenBoardManifest.NorthIsAssumed"/>
    /// used to be the negation of one literal, so the day a source started
    /// sending the more accurate word its north would have been read as
    /// assumed - and an assumed north is not drawn until the user confirms it.
    /// A better value would have quietly cost the user a confirmation and an
    /// arrow. Widening what is accepted costs nothing and removes that.
    /// </remarks>
    public const string NorthFromProjectedGrid = "grid";

    /// <summary>Nothing declares it; the angle is only CityGen's convention.</summary>
    public const string NorthAssumed = "assumed";

    /// <summary>The values that mean the source knew which way north is.</summary>
    public static bool DeclaresNorth(string? source)
    {
        string value = (source ?? "").Trim();
        return value.Equals(NorthFromUtmGrid, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(NorthFromProjectedGrid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The board file beside a drawing, or "" if there cannot be one.</summary>
    public static string ResolveSidecarPath(string? nativeDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(nativeDocumentPath))
            return "";

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(nativeDocumentPath.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }

        if (fullPath.EndsWith(SidecarSuffix, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        if (!Path.GetExtension(fullPath).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            return "";
        return Path.Combine(
            Path.GetDirectoryName(fullPath) ?? "",
            Path.GetFileNameWithoutExtension(fullPath) + SidecarSuffix);
    }
}

/// <summary>
/// What kind of thing an object is on the ground. An open vocabulary: a value
/// Studio does not know leaves the decision to the style catalogue rather than
/// failing.
/// </summary>
public static class CityGenBoardRoles
{
    /// <summary>A piece of ground. Filled.</summary>
    public const string Surface = "surface";

    /// <summary>A painted line. Drawn, never filled.</summary>
    public const string Marking = "marking";

    /// <summary>Something placed at a point, such as a tree.</summary>
    public const string Symbol = "symbol";
}

public sealed class CityGenBoardVertex
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public bool IsPolylineVertex { get; set; }

    public bool IsArcSample { get; set; }

    public bool IsArcEndpoint { get; set; }
}

/// <summary>
/// One run between two vertices. An arc keeps its bulge rather than being
/// flattened: a board is printed a metre across at a scale where a curve
/// broken into straight runs is visible as facets.
/// </summary>
public sealed class CityGenBoardSegment
{
    public int StartVertexIndex { get; set; }

    public int EndVertexIndex { get; set; }

    public double Bulge { get; set; }

    public bool IsArc { get; set; }

    public double? Radius { get; set; }

    public double? IncludedAngle { get; set; }
}

public sealed class CityGenBoardOrigin
{
    /// <summary>
    /// False when the drawing has no base point. The geometry is still valid;
    /// what is missing is a dependable anchor for lining one export up with
    /// another, so the board says so rather than assuming one.
    /// </summary>
    public bool IsDefined { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }
}

/// <summary>One classified outline of the general plan.</summary>
public sealed class CityGenBoardObject
{
    /// <summary>
    /// Identifies this object within its own manifest, and no further.
    /// </summary>
    /// <remarks>
    /// It is used for one thing: pairing an island with the area it is cut out
    /// of, so a path crossing a lawn becomes a hole in the grass. The lookup is
    /// rebuilt from each file as it is read.
    ///
    /// It carries no guarantee across files. CityGen derives it from the
    /// drawing's entity handle, and until CGA's element-level regeneration
    /// lands those handles are new on every Generate - the same lawn is a
    /// different id each time. Studio does not notice, because it compares
    /// whole-file hashes rather than objects, and stores no ids at all.
    ///
    /// Recorded because both halves of that will change and the change will be
    /// silent. When element-level regeneration ships, these strings become
    /// stable in practice - and practice is not a contract. Anyone who wants to
    /// track an object between exports needs CGA to promise stability first;
    /// reading it from observed behaviour would work until the day a drawing is
    /// rebuilt from scratch.
    /// </remarks>
    public string Id { get; set; } = "";

    /// <summary>
    /// The area this one is cut out of, for an island. Studio draws the pair as
    /// one path with an even-odd fill, so a path crossing a lawn is a hole in
    /// the grass rather than grass drawn over the path.
    /// </summary>
    public string ParentId { get; set; } = "";

    /// <summary>The semantic key. Layer names are secondary; users rename them.</summary>
    public string Flow { get; set; } = "";

    public string Category { get; set; } = "";

    /// <summary>Surface material - the most direct key to a pattern.</summary>
    public string Material { get; set; } = "";

    /// <summary>An open vocabulary. An unknown value falls back, never fails.</summary>
    public string Subtype { get; set; } = "";

    /// <summary>
    /// Whether this is a piece of ground, a painted line, or something placed
    /// at a point - <see cref="CityGenBoardRoles"/>.
    ///
    /// It settles a question Studio could otherwise only guess at from the
    /// shape of a flow name, which is the same guessing-from-names both sides
    /// agreed to stop doing. On a road drawing two thirds of the objects are
    /// markings, and filling them would lay bands of road colour across the
    /// carriageway.
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// What produced the object. More dependable than the category for telling
    /// a building from anything else, because a building being demolished is
    /// still a building.
    /// </summary>
    public string Kind { get; set; } = "";

    public string Layer { get; set; } = "";

    /// <summary>An ordinal only. It settles overlap; it carries no meaning.</summary>
    public int DrawOrder { get; set; }

    /// <summary>Used only where the flow is unrecognised. Never the first choice.</summary>
    public int? FallbackColorIndex { get; set; }

    public bool IsClosed { get; set; }

    /// <summary>Radius for a tree, area for a surface.</summary>
    public double? Metric { get; set; }

    public string SourceKey { get; set; } = "";

    public List<CityGenBoardVertex> Vertices { get; set; } = [];

    public List<CityGenBoardSegment> Segments { get; set; } = [];

    /// <summary>Enough of a shape to draw.</summary>
    [JsonIgnore]
    public bool IsDrawable => Vertices.Count >= 2;

    [JsonIgnore]
    public bool IsIsland => !string.IsNullOrWhiteSpace(ParentId);
}

public sealed class CityGenBoardManifest
{
    public string Schema { get; set; } = "";

    public int SchemaVersion { get; set; }

    public string Units { get; set; } = "";

    public string CoordinateSpace { get; set; } = "";

    public double NorthAngleDegrees { get; set; }

    /// <summary>
    /// Whether north is declared by the drawing or only assumed. A board prints
    /// this as an arrow, so an assumption has to travel labelled as one: a
    /// wrongly pointed north arrow is discovered by the jury, not by the office.
    /// </summary>
    public string NorthAngleSource { get; set; } = "";

    public CityGenBoardOrigin Origin { get; set; } = new();

    public double[] Bbox { get; set; } = [];

    public string SourceDocument { get; set; } = "";

    public DateTimeOffset? GeneratedAtUtc { get; set; }

    public int ObjectCount { get; set; }

    public List<CityGenBoardObject> Objects { get; set; } = [];

    [JsonIgnore]
    public bool NorthIsAssumed => !CityGenGraphicBoardContract.DeclaresNorth(NorthAngleSource);
}

public sealed class CityGenBoardLoadResult
{
    public CityGenBoardManifest? Manifest { get; init; }

    /// <summary>Why the file was refused. Empty when it was accepted.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// Objects that arrived but cannot be drawn. These do not refuse the file -
    /// one unusable outline should not cost a plan its other eight hundred -
    /// but they are counted so their absence is never silent.
    /// </summary>
    public IReadOnlyList<string> SkippedObjects { get; init; } = [];

    public bool IsLoaded => Manifest is not null && Issues.Count == 0;
}

/// <summary>
/// Reads a CityGen board export, refusing anything that is not the contract it
/// claims to be.
///
/// The whole file is judged on its header - the schema, its version, its units
/// and its coordinate space - because those are the promises the geometry means
/// nothing without. Metres read as feet would put a scale bar out by a factor
/// of three and nothing downstream could tell.
/// </summary>
public static class CityGenGraphicBoardReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static CityGenBoardLoadResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        CityGenBoardManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CityGenBoardManifest>(
                File.ReadAllBytes(path),
                JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new CityGenBoardLoadResult
            {
                Issues = [$"Файлыг уншиж чадсангүй: {exception.Message}"],
            };
        }

        return manifest is null
            ? new CityGenBoardLoadResult { Issues = ["Файл хоосон байна."] }
            : Verify(manifest);
    }

    public static CityGenBoardLoadResult Verify(CityGenBoardManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<string>();

        if (!CityGenGraphicBoardContract.Schema.Equals(manifest.Schema, StringComparison.Ordinal))
        {
            issues.Add(
                $"Схем таарахгүй: '{manifest.Schema}' " +
                $"(хүлээж буй '{CityGenGraphicBoardContract.Schema}').");
        }

        if (manifest.SchemaVersion != CityGenGraphicBoardContract.CurrentSchemaVersion)
        {
            issues.Add(
                $"Схемийн хувилбар {manifest.SchemaVersion} дэмжигдэхгүй " +
                $"(хүлээж буй {CityGenGraphicBoardContract.CurrentSchemaVersion}).");
        }

        if (!CityGenGraphicBoardContract.ExpectedUnits.Equals(
                manifest.Units,
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"Нэгж '{manifest.Units}' дэмжигдэхгүй, метр байх ёстой.");
        }

        if (!CityGenGraphicBoardContract.ExpectedCoordinateSpace.Equals(
                manifest.CoordinateSpace,
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                $"Координатын систем '{manifest.CoordinateSpace}' дэмжигдэхгүй, " +
                "зургийн координат байх ёстой.");
        }

        if (!double.IsFinite(manifest.NorthAngleDegrees) ||
            manifest.NorthAngleDegrees < 0 ||
            manifest.NorthAngleDegrees >= 360)
        {
            issues.Add($"Хойд зүгийн өнцөг {manifest.NorthAngleDegrees} хүрээнээс гарсан.");
        }

        manifest.Objects ??= [];
        manifest.Origin ??= new CityGenBoardOrigin();

        var complaints = new List<(string Reason, string Subject)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CityGenBoardObject item in manifest.Objects)
        {
            item.Vertices ??= [];
            item.Segments ??= [];
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                // Without an identity a user's own styling could never be kept
                // across a re-export, so the object is of no use to a board.
                complaints.Add(("танигчгүй объект", Describe(item)));
                continue;
            }
            if (!seen.Add(item.Id))
                complaints.Add(("танигч давхардсан", item.Id));
            if (!item.IsDrawable)
                complaints.Add(("зурахад хангалттай оройгүй", item.Id));
        }

        var skipped = Summarise(complaints);
        if (issues.Count == 0 && manifest.ObjectCount != manifest.Objects.Count)
        {
            // Not fatal, but it means the file was truncated or written by
            // something that miscounted, and either is worth saying out loud.
            skipped.Add(
                $"Тоолол зөрж байна: толгойд {manifest.ObjectCount}, " +
                $"биед {manifest.Objects.Count} объект.");
        }

        return new CityGenBoardLoadResult
        {
            Manifest = issues.Count == 0 ? manifest : null,
            Issues = issues,
            SkippedObjects = skipped,
        };
    }

    /// <summary>
    /// One line per kind of complaint, with a count and a few examples.
    ///
    /// A real plan produced a hundred and ninety-one identical reports, and a
    /// hundred and ninety-one identical lines hide a problem as effectively as
    /// silence does. The count is what makes it legible; the examples are what
    /// make it findable.
    /// </summary>
    private static List<string> Summarise(IReadOnlyList<(string Reason, string Subject)> complaints)
    {
        const int examples = 3;
        return complaints
            .GroupBy(complaint => complaint.Reason, StringComparer.Ordinal)
            .Select(group =>
            {
                string sample = string.Join(
                    ", ",
                    group.Select(complaint => complaint.Subject).Distinct(StringComparer.Ordinal).Take(examples));
                return group.Count() == 1
                    ? $"{sample}: {group.Key}."
                    : $"{group.Key}: {group.Count()} объект (жишээ: {sample}).";
            })
            .ToList();
    }

    private static string Describe(CityGenBoardObject item) =>
        string.IsNullOrWhiteSpace(item.Flow) ? "(тодорхойгүй объект)" : item.Flow;
}
