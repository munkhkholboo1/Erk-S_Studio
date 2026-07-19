using ErkS.Platform.Contracts;

namespace ErkS.Platform.Pdf;

public sealed class SheetPackageAcceptanceReport
{
    public required string ManifestPath { get; init; }

    public List<string> Issues { get; } = [];

    public List<SheetPackageAcceptancePage> Pages { get; } = [];

    public bool IsAccepted => Issues.Count == 0;
}

public sealed class SheetPackageAcceptancePage
{
    public required string SheetId { get; init; }

    public required string PdfPath { get; init; }

    public required int PdfPageNumber { get; init; }

    public required double WidthMm { get; init; }

    public required double HeightMm { get; init; }

    public required bool HasVectorContent { get; init; }

    public required int ImageXObjectCount { get; init; }
}

/// <summary>
/// Runs the same fail-closed package checks used by Studio intake, then adds
/// structural PDF checks used by Revit and AutoCAD host acceptance runs.
/// </summary>
public static class SheetPackageAcceptanceValidator
{
    private static readonly HashSet<string> NativeSourceExtensions = new(
        [".dwg", ".dws", ".dwt", ".rvt", ".rfa"],
        StringComparer.OrdinalIgnoreCase);

    public static SheetPackageAcceptanceReport Validate(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string fullManifestPath = Path.GetFullPath(manifestPath);
        var report = new SheetPackageAcceptanceReport { ManifestPath = fullManifestPath };
        SheetPackageLoadResult package = SheetPackageReader.Load(fullManifestPath);
        report.Issues.AddRange(package.Issues);
        if (!package.IsLossless || package.Manifest is null)
            return report;

        RejectNativeSourcePayloads(fullManifestPath, report.Issues);
        foreach (SheetPackageEntry sheet in package.Manifest.Sheets)
        {
            if (!package.TryGetVerifiedPdfPath(sheet, out string pdfPath))
            {
                report.Issues.Add($"Sheet '{sheet.Number}': verified PDF path is unavailable.");
                continue;
            }

            try
            {
                PdfVectorDocumentProfile profile = PdfVectorQualityInspector.Inspect(pdfPath);
                for (int index = 0; index < profile.Pages.Count; index++)
                {
                    PdfVectorPageProfile page = profile.Pages[index];
                    bool hasVectorContent = page.HasPathPaintingOperators ||
                        page.HasTextOperators ||
                        page.FormXObjectCount > 0;
                    report.Pages.Add(new SheetPackageAcceptancePage
                    {
                        SheetId = sheet.SheetId,
                        PdfPath = pdfPath,
                        PdfPageNumber = index + 1,
                        WidthMm = page.WidthMm,
                        HeightMm = page.HeightMm,
                        HasVectorContent = hasVectorContent,
                        ImageXObjectCount = page.ImageXObjectCount,
                    });

                    if (!hasVectorContent)
                    {
                        string reason = page.ImageXObjectCount > 0
                            ? "full-page raster fallback was rejected"
                            : "PDF has no vector/text content";
                        report.Issues.Add($"Sheet '{sheet.Number}' page {index + 1}: {reason}.");
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                report.Issues.Add(
                    $"Sheet '{sheet.Number}': vector PDF inspection failed ({exception.Message}).");
            }
        }

        return report;
    }

    private static void RejectNativeSourcePayloads(string manifestPath, ICollection<string> issues)
    {
        string packageFolder = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("Manifest path has no parent folder.");
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };
        foreach (string path in Directory.EnumerateFiles(packageFolder, "*", options))
        {
            if (NativeSourceExtensions.Contains(Path.GetExtension(path)))
            {
                issues.Add(
                    $"Native authoring file is forbidden inside a Studio sheet package: {Path.GetFileName(path)}.");
            }
        }
    }
}
