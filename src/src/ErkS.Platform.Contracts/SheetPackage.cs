using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ErkS.Platform.Contracts;

/// <summary>
/// The lossless hand-off format between design applications (AutoCAD layouts,
/// Revit sheets) and the Erk-S Platform album pipeline.
///
/// A "sheet package" is a folder containing one vector PDF per sheet plus a
/// manifest file (<c>*.erks-sheets.json</c>). Every PDF carries a SHA-256 hash
/// in the manifest so the receiving side can prove nothing was corrupted or
/// lost in transit.
/// </summary>
public sealed class SheetPackageManifest
{
    public const int CurrentSchemaVersion = 4;

    /// <summary>File suffix that marks a manifest: "MyExport.erks-sheets.json".</summary>
    public const string ManifestSuffix = ".erks-sheets.json";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Stable id of this export run; re-exports produce a new id.</summary>
    public Guid PackageId { get; set; } = Guid.NewGuid();

    public SheetPackageSource Source { get; set; } = new();

    public string ProjectId { get; set; } = "";

    public string StageId { get; set; } = "";

    public string WorkPackageId { get; set; } = "";

    public string ExportMode { get; set; } = "Sheets";

    /// <summary>
    /// Delta contains only selected/changed sheets and never implies deletion.
    /// FullSnapshot is the authoritative current sheet set for this source;
    /// sheets omitted from a newer full snapshot were deleted at the source.
    /// Version 1-3 packages deserialize as Delta for backward compatibility.
    /// </summary>
    public SheetPackageScope PackageScope { get; set; } = SheetPackageScope.Delta;

    public DateTimeOffset ExportedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<SheetPackageEntry> Sheets { get; set; } = [];
}

public enum SheetPackageScope
{
    Delta,
    FullSnapshot,
}

public sealed class SheetPackageSource
{
    /// <summary>Studio source registry id; optional for schema version 1 packages.</summary>
    public string SourceId { get; set; } = "";

    public SheetSourceApplication Application { get; set; } = SheetSourceApplication.Manual;

    /// <summary>e.g. "AutoCAD 2026", "Revit 2026".</summary>
    public string ApplicationVersion { get; set; } = "";

    /// <summary>Full path of the DWG/RVT document the sheets came from.</summary>
    public string DocumentPath { get; set; } = "";

    public string DocumentTitle { get; set; } = "";

    /// <summary>Optional project code used to group packages from many files.</summary>
    public string ProjectCode { get; set; } = "";
}

public enum SheetSourceApplication
{
    Manual,
    AutoCad,
    Revit,
    CityGen,
    Pdf,
}

public sealed class SheetPackageEntry
{
    /// <summary>Source-side stable id (layout handle, Revit sheet unique id).</summary>
    public string SheetId { get; set; } = "";

    /// <summary>Sheet number as printed in the corner table, e.g. "AR-05".</summary>
    public string Number { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Discipline/series code, e.g. "AR", "ST", "citygen".</summary>
    public string Discipline { get; set; } = "";

    public string Revision { get; set; } = "";

    /// <summary>Source sheet scale label, for example "1:100", "VARIES" or "NTS".</summary>
    public string ScaleText { get; set; } = "";

    public double WidthMm { get; set; }

    public double HeightMm { get; set; }

    /// <summary>
    /// Stable format identity selected in the authoring application. Version 3
    /// producers also include <see cref="Format"/> so custom formats do not
    /// require a matching built-in Studio catalog entry.
    /// </summary>
    public string PageFormatId { get; set; } = "";

    public PageFormatSpec? Format { get; set; }

    /// <summary>
    /// True when the PDF contains only the vector drawing-space content. The
    /// Studio frame, grid, title and company table must not be present in it.
    /// </summary>
    public bool IsCleanDrawingSpace { get; set; }

    public double ContentWidthMm { get; set; }

    public double ContentHeightMm { get; set; }

    /// <summary>Project-neutral classification used by future auto-layout rules.</summary>
    public string ContentKind { get; set; } = "";

    /// <summary>
    /// Optional source-sheet narrative. Revit uses its "Хуудасны тайлбар"
    /// parameter; Studio may override it per album page without changing the
    /// authoring file. Missing values preserve schema 1-4 compatibility.
    /// </summary>
    public string SheetDescription { get; set; } = "";

    public string BuildingId { get; set; } = "";

    public string BuildingName { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string LevelName { get; set; } = "";

    /// <summary>Stable project-neutral drawing asset id.</summary>
    public string DrawingAssetId { get; set; } = "";

    /// <summary>Immutable export/version id of the drawing asset.</summary>
    public string DrawingAssetVersion { get; set; } = "";

    /// <summary>Hash pinned by a released/archive album page.</summary>
    public string DrawingAssetSha256 { get; set; } = "";

    /// <summary>PDF file name relative to the manifest folder.</summary>
    public string PdfFileName { get; set; } = "";

    /// <summary>SHA-256 of the PDF file, lower-case hex.</summary>
    public string Sha256 { get; set; } = "";

    public int PageCount { get; set; } = 1;
}

/// <summary>
/// Parametric page-format contract shared by Revit, AutoCAD, CityGen and
/// Studio. Coordinates are millimetres from the physical page's top-left.
/// It describes zones, not CAD geometry.
/// </summary>
public sealed class PageFormatSpec
{
    public const int CurrentSpecVersion = 1;

    public int SpecVersion { get; set; } = CurrentSpecVersion;

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Concept/Sketch, WorkingDrawing/Blueprint, Document or Cover.</summary>
    public string Mode { get; set; } = "";

    public string Code { get; set; } = "";

    public string Orientation { get; set; } = "";

    public string BindEdge { get; set; } = "";

    public double WidthMm { get; set; }

    public double HeightMm { get; set; }

    public PageRectSpec DrawingArea { get; set; } = new();

    public PageRectSpec SheetTitleArea { get; set; } = new();

    public PageRectSpec TitleBlockArea { get; set; } = new();

    public bool ShowBorder { get; set; } = true;

    public bool ShowGrid { get; set; }

    public int Revision { get; set; } = 1;

    public int ModuleColumns { get; set; }

    public int ModuleRows { get; set; }

    public bool HasHalfModule { get; set; }

    /// <summary>SHA-256 of the canonical physical geometry and render flags.</summary>
    public string GeometryHash { get; set; } = "";
}

public sealed class PageRectSpec
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

public static class PageFormatSpecGeometry
{
    private const double BoundsToleranceMm = 0.01;

    public static string ComputeHash(PageFormatSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var canonical = string.Join('|',
        [
            spec.SpecVersion.ToString(CultureInfo.InvariantCulture),
            Normalize(spec.Mode),
            Normalize(spec.Code),
            Normalize(spec.Orientation),
            Normalize(spec.BindEdge),
            Number(spec.WidthMm),
            Number(spec.HeightMm),
            Rectangle(spec.DrawingArea),
            Rectangle(spec.SheetTitleArea),
            Rectangle(spec.TitleBlockArea),
            spec.ShowBorder ? "1" : "0",
            spec.ShowGrid ? "1" : "0",
            spec.Revision.ToString(CultureInfo.InvariantCulture),
            spec.ModuleColumns.ToString(CultureInfo.InvariantCulture),
            spec.ModuleRows.ToString(CultureInfo.InvariantCulture),
            spec.HasHalfModule ? "1" : "0",
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static IReadOnlyList<string> Validate(PageFormatSpec? spec)
    {
        var issues = new List<string>();
        if (spec is null)
        {
            issues.Add("Format specification is missing.");
            return issues;
        }

        if (spec.SpecVersion <= 0 || spec.SpecVersion > PageFormatSpec.CurrentSpecVersion)
        {
            issues.Add($"Unsupported format specification version {spec.SpecVersion}.");
        }
        if (string.IsNullOrWhiteSpace(spec.Id))
        {
            issues.Add("Format id is missing.");
        }
        if (!IsFinitePositive(spec.WidthMm) || !IsFinitePositive(spec.HeightMm))
        {
            issues.Add("Format page size must be positive finite millimetres.");
        }

        ValidateRectangle(issues, "drawing area", spec.DrawingArea, spec, required: true);
        ValidateRectangle(issues, "sheet title area", spec.SheetTitleArea, spec, required: false);
        ValidateRectangle(issues, "title block area", spec.TitleBlockArea, spec, required: false);

        if (string.IsNullOrWhiteSpace(spec.GeometryHash))
        {
            issues.Add("Format geometry hash is missing.");
        }
        else if (!string.Equals(spec.GeometryHash, ComputeHash(spec), StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("Format geometry hash does not match its geometry.");
        }

        return issues;
    }

    private static void ValidateRectangle(
        ICollection<string> issues,
        string label,
        PageRectSpec? rectangle,
        PageFormatSpec spec,
        bool required)
    {
        if (rectangle is null)
        {
            if (required)
            {
                issues.Add($"Format {label} is missing.");
            }
            return;
        }

        var hasSize = rectangle.Width > 0 && rectangle.Height > 0;
        if (!hasSize && !required && rectangle.Width == 0 && rectangle.Height == 0)
        {
            return;
        }
        if (!IsFinite(rectangle.X) || !IsFinite(rectangle.Y) ||
            !IsFinitePositive(rectangle.Width) || !IsFinitePositive(rectangle.Height))
        {
            issues.Add($"Format {label} must use finite coordinates and positive size.");
            return;
        }
        if (rectangle.X < -BoundsToleranceMm || rectangle.Y < -BoundsToleranceMm ||
            rectangle.X + rectangle.Width > spec.WidthMm + BoundsToleranceMm ||
            rectangle.Y + rectangle.Height > spec.HeightMm + BoundsToleranceMm)
        {
            issues.Add($"Format {label} is outside the physical page.");
        }
    }

    private static string Rectangle(PageRectSpec? rectangle) => rectangle is null
        ? "0,0,0,0"
        : $"{Number(rectangle.X)},{Number(rectangle.Y)},{Number(rectangle.Width)},{Number(rectangle.Height)}";

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";

    private static string Number(double value) => value == 0d
        ? "0"
        : value.ToString("R", CultureInfo.InvariantCulture);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFinitePositive(double value) => IsFinite(value) && value > 0;
}
