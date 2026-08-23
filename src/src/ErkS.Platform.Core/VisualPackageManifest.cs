using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErkS.Platform.Core;

/// <summary>
/// The channel Revit sends board visuals through: renders, shaded diagrams and
/// line views, delivered beside the sheet package rather than inside it.
///
/// It exists because a competition board's hero render is a page with no vector
/// content at all, and the sheet package refuses those on purpose - that check
/// has caught real failures where a drawing quietly became a picture of itself.
/// Rather than weaken it for raster, raster arrives here under its own rules.
/// </summary>
public static class VisualPackageContract
{
    public const int CurrentSchemaVersion = 1;

    public const string ManifestSuffix = ".erks-visuals.json";

    public const string ApplicationRevit = "Revit";
}

/// <summary>What a visual is, as a property rather than as an appearance.</summary>
public static class VisualAssetKinds
{
    /// <summary>A line drawing. Vector, and stays vector at any board size.</summary>
    public const string LineView = "line-view";

    /// <summary>A diagram with flat colour fills.</summary>
    public const string ShadedDiagram = "shaded-diagram";

    /// <summary>A rendered image. Raster, and cannot be restyled.</summary>
    public const string Render = "render";
}

public static class VisualMediaTypes
{
    public const string Pdf = "application/pdf";

    public const string Png = "image/png";

    public const string Jpeg = "image/jpeg";

    /// <summary>
    /// Whether this media is vector. Asked of the media type rather than of the
    /// kind, because the two are stated separately on purpose: a shaded diagram
    /// may arrive either way, and guessing from the kind would be wrong for it.
    /// </summary>
    public static bool IsVector(string? mediaType) =>
        Pdf.Equals((mediaType ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsKnown(string? mediaType)
    {
        string value = (mediaType ?? "").Trim();
        return value.Equals(Pdf, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(Png, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(Jpeg, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Where the view sits on the page it was exported onto.
///
/// This is what lets Revit keep its fixed paper sizes. It exports the view onto
/// whatever standard sheet it likes and says where the drawing landed; the card
/// shows just that rectangle. Revit measured the placement as deterministic to
/// within two hundredths of a millimetre, which is why it can be stated as
/// numbers rather than found by looking at the file.
/// </summary>
public sealed class VisualAssetPage
{
    public double PaperWidthMm { get; set; }

    public double PaperHeightMm { get; set; }

    /// <summary>From the page's top-left corner, as the sheet package measures.</summary>
    public double ViewXMm { get; set; }

    public double ViewYMm { get; set; }

    public double ViewWidthMm { get; set; }

    public double ViewHeightMm { get; set; }

    [JsonIgnore]
    public bool IsUsable =>
        PaperWidthMm > 0 && PaperHeightMm > 0 &&
        ViewWidthMm > 0 && ViewHeightMm > 0 &&
        ViewXMm >= -0.5 && ViewYMm >= -0.5 &&
        ViewXMm + ViewWidthMm <= PaperWidthMm + 0.5 &&
        ViewYMm + ViewHeightMm <= PaperHeightMm + 0.5;

    /// <summary>
    /// The view's rectangle as fractions of the page, which is how a card holds
    /// a crop. Null when the rectangle does not describe a place on the page.
    /// </summary>
    public (double X, double Y, double Width, double Height)? AsNormalizedCrop() => IsUsable
        ? (ViewXMm / PaperWidthMm,
           ViewYMm / PaperHeightMm,
           ViewWidthMm / PaperWidthMm,
           ViewHeightMm / PaperHeightMm)
        : null;
}

public sealed class VisualAsset
{
    /// <summary>
    /// The view's own identity in Revit, stable across exports. The same shape
    /// the sheet package already uses for a sheet, so a re-export refreshes
    /// this asset in place instead of adding a second one beside it.
    /// </summary>
    public string AssetId { get; set; } = "";

    public string ViewName { get; set; } = "";

    public string ViewType { get; set; } = "";

    /// <summary><see cref="VisualAssetKinds"/>.</summary>
    public string Kind { get; set; } = "";

    /// <summary><see cref="VisualMediaTypes"/>. Never inferred from the kind.</summary>
    public string MediaType { get; set; } = "";

    public string FileName { get; set; } = "";

    public string Sha256 { get; set; } = "";

    /// <summary>Required for raster: the resolution guard has nothing without it.</summary>
    public int WidthPx { get; set; }

    public int HeightPx { get; set; }

    public int Dpi { get; set; }

    /// <summary>Required for a vector PDF: where the view landed on its page.</summary>
    public VisualAssetPage? Page { get; set; }

    /// <summary>
    /// The group this belongs to, when several views were exported together at
    /// one angle and one proportion. A board draws such a group as one strip so
    /// that equal size and equal spacing are structural rather than manual.
    /// </summary>
    public string SeriesId { get; set; } = "";

    public int SeriesOrder { get; set; }

    public bool IsPerspective { get; set; }

    public DateTimeOffset? CapturedAtUtc { get; set; }

    [JsonIgnore]
    public bool IsVector => VisualMediaTypes.IsVector(MediaType);
}

public sealed class VisualPackageSource
{
    public string SourceId { get; set; } = "";

    public string Application { get; set; } = "";

    public string ApplicationVersion { get; set; } = "";

    public string DocumentPath { get; set; } = "";

    public string DocumentTitle { get; set; } = "";

    public string ProjectCode { get; set; } = "";
}

public sealed class VisualPackageManifest
{
    public int SchemaVersion { get; set; }

    public string PackageId { get; set; } = "";

    public string ProjectId { get; set; } = "";

    public string StageId { get; set; } = "";

    public string WorkPackageId { get; set; } = "";

    public DateTimeOffset ExportedAtUtc { get; set; }

    public VisualPackageSource Source { get; set; } = new();

    public List<VisualAsset> Assets { get; set; } = [];
}

public sealed class VisualPackageLoadResult
{
    public VisualPackageManifest? Manifest { get; init; }

    /// <summary>Why the package was refused. Empty when it was accepted.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// Assets that arrived but cannot be used, named and counted. One unusable
    /// render should not cost a delivery its other twenty, but an asset that
    /// vanished without a word is the failure this codebase keeps finding.
    /// </summary>
    public IReadOnlyList<string> SkippedAssets { get; init; } = [];

    /// <summary>Assets that survived every check.</summary>
    public IReadOnlyList<VisualAsset> Accepted { get; init; } = [];

    public bool IsLoaded => Manifest is not null && Issues.Count == 0;
}

/// <summary>
/// Reads a Revit visual package, refusing what is not the contract it claims to
/// be and checking that every file in it is the file the manifest describes.
///
/// The hash is verified rather than trusted. A render that was truncated in
/// transit still opens, still draws, and is simply wrong - and it would be
/// wrong on a printed board, discovered by whoever is looking at it.
/// </summary>
public static class VisualPackageReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static VisualPackageLoadResult Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        string fullPath;
        VisualPackageManifest? manifest;
        try
        {
            fullPath = Path.GetFullPath(manifestPath);
            manifest = JsonSerializer.Deserialize<VisualPackageManifest>(
                File.ReadAllBytes(fullPath),
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or ArgumentException or NotSupportedException)
        {
            return new VisualPackageLoadResult { Issues = [$"Багцыг уншиж чадсангүй: {exception.Message}"] };
        }

        if (manifest is null)
            return new VisualPackageLoadResult { Issues = ["Багц хоосон байна."] };

        return Verify(manifest, Path.GetDirectoryName(fullPath) ?? "");
    }

    public static VisualPackageLoadResult Verify(VisualPackageManifest manifest, string packageFolder)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<string>();

        if (manifest.SchemaVersion != VisualPackageContract.CurrentSchemaVersion)
        {
            issues.Add(
                $"Схемийн хувилбар {manifest.SchemaVersion} дэмжигдэхгүй " +
                $"(хүлээж буй {VisualPackageContract.CurrentSchemaVersion}).");
        }

        manifest.Assets ??= [];
        manifest.Source ??= new VisualPackageSource();
        if (string.IsNullOrWhiteSpace(manifest.Source.SourceId))
            issues.Add("Багц эх сурвалжаа заагаагүй байна.");

        var skipped = new List<string>();
        var accepted = new List<VisualAsset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualAsset asset in manifest.Assets)
        {
            string? refusal = Check(asset, packageFolder, seen);
            if (refusal is null)
                accepted.Add(asset);
            else
                skipped.Add(refusal);
        }

        return new VisualPackageLoadResult
        {
            Manifest = issues.Count == 0 ? manifest : null,
            Issues = issues,
            SkippedAssets = skipped,
            Accepted = accepted,
        };
    }

    /// <summary>Why this asset cannot be used, or null if it can.</summary>
    private static string? Check(VisualAsset asset, string packageFolder, HashSet<string> seen)
    {
        string name = string.IsNullOrWhiteSpace(asset.ViewName)
            ? (string.IsNullOrWhiteSpace(asset.FileName) ? "(нэргүй)" : asset.FileName)
            : asset.ViewName;

        if (string.IsNullOrWhiteSpace(asset.AssetId))
        {
            // Without an identity a re-export could only append a duplicate,
            // and any framing or caption the user gave it would be orphaned.
            return $"{name}: танигчгүй.";
        }
        if (!seen.Add(asset.AssetId))
            return $"{name}: танигч давхардсан ({asset.AssetId}).";
        if (!VisualMediaTypes.IsKnown(asset.MediaType))
            return $"{name}: '{asset.MediaType}' төрлийн файл дэмжигдэхгүй.";
        if (string.IsNullOrWhiteSpace(asset.FileName))
            return $"{name}: файлын нэргүй.";

        if (asset.IsVector)
        {
            // The page rectangle is the only authority on where the drawing
            // sits: Revit's PDF keeps the geometry outside the clip as well, so
            // measuring the ink would find a much larger area than the view.
            if (asset.Page is null || !asset.Page.IsUsable)
                return $"{name}: вектор view-ийн хуудсан дахь байрлал дутуу.";
        }
        else if (asset.WidthPx <= 0 || asset.HeightPx <= 0)
        {
            // Without the pixel size a card cannot say whether the render holds
            // up at the size it is placed, which is the guard that keeps a soft
            // image from being discovered at print time.
            return $"{name}: пикселийн хэмжээ дутуу.";
        }

        if (packageFolder.Length == 0)
            return null;

        string filePath = Path.Combine(packageFolder, asset.FileName);
        if (!File.Exists(filePath))
            return $"{name}: файл багцад олдсонгүй ({asset.FileName}).";
        if (string.IsNullOrWhiteSpace(asset.Sha256))
            return $"{name}: хэшгүй тул баталгаажуулах боломжгүй.";

        string actual;
        try
        {
            actual = ProjectDocumentFileStore.ComputeSha256(filePath);
        }
        catch (IOException exception)
        {
            return $"{name}: файлыг уншиж чадсангүй ({exception.Message}).";
        }

        // Checked rather than trusted: a truncated render still opens, still
        // draws, and is simply wrong - on a printed board.
        return actual.Equals(asset.Sha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{name}: файлын хэш таарахгүй байна.";
    }
}
