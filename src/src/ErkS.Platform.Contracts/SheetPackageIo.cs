using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErkS.Platform.Contracts;

public static class SheetPackageJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Result of loading a manifest and verifying every referenced PDF.</summary>
public sealed class SheetPackageLoadResult
{
    public required string ManifestPath { get; init; }

    public SheetPackageManifest? Manifest { get; init; }

    public List<string> Issues { get; } = [];

    /// <summary>True only when the manifest parsed and every hash matched.</summary>
    public bool IsLossless => Manifest is not null && Issues.Count == 0;
}

public static class SheetPackageReader
{
    /// <summary>
    /// Loads a manifest and verifies the package: every sheet PDF must exist
    /// and match its SHA-256. Never throws for bad input - problems are
    /// reported in <see cref="SheetPackageLoadResult.Issues"/>.
    /// </summary>
    public static SheetPackageLoadResult Load(string manifestPath)
    {
        SheetPackageManifest? manifest = null;
        var issues = new List<string>();

        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<SheetPackageManifest>(json, SheetPackageJson.Options);
            if (manifest is null)
            {
                issues.Add("Manifest deserialized to null.");
            }
        }
        catch (Exception exception)
        {
            issues.Add($"Manifest could not be read: {exception.Message}");
        }

        var result = new SheetPackageLoadResult { ManifestPath = manifestPath, Manifest = manifest };
        result.Issues.AddRange(issues);
        if (manifest is null)
        {
            return result;
        }

        if (manifest.SchemaVersion > SheetPackageManifest.CurrentSchemaVersion)
        {
            result.Issues.Add(
                $"Manifest schema {manifest.SchemaVersion} is newer than supported {SheetPackageManifest.CurrentSchemaVersion}. Update Erk-S Platform.");
        }

        if (manifest.Sheets.Count == 0 && manifest.PackageScope != SheetPackageScope.FullSnapshot)
        {
            result.Issues.Add("Package contains no sheets.");
        }

        var directory = Path.GetDirectoryName(manifestPath) ?? "";
        foreach (var sheet in manifest.Sheets)
        {
            if (sheet.Format is not null)
            {
                foreach (var issue in PageFormatSpecGeometry.Validate(sheet.Format))
                {
                    result.Issues.Add($"Sheet '{sheet.Number}': {issue}");
                }
                if (!string.IsNullOrWhiteSpace(sheet.PageFormatId) &&
                    !string.Equals(sheet.PageFormatId, sheet.Format.Id, StringComparison.OrdinalIgnoreCase))
                {
                    result.Issues.Add($"Sheet '{sheet.Number}': page format id does not match the inline format.");
                }
            }
            if (sheet.IsCleanDrawingSpace)
            {
                if (sheet.Format is null)
                {
                    result.Issues.Add($"Sheet '{sheet.Number}': clean drawing-space PDF requires an inline format.");
                }
                else if (Math.Abs(sheet.ContentWidthMm - sheet.Format.DrawingArea.Width) > 0.01 ||
                         Math.Abs(sheet.ContentHeightMm - sheet.Format.DrawingArea.Height) > 0.01)
                {
                    result.Issues.Add($"Sheet '{sheet.Number}': content size does not match the format drawing area.");
                }
            }

            var pdfPath = Path.Combine(directory, sheet.PdfFileName);
            if (!File.Exists(pdfPath))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': PDF file missing ({sheet.PdfFileName}).");
                continue;
            }

            var actual = ComputeSha256(pdfPath);
            if (!string.Equals(actual, sheet.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': PDF hash mismatch - file changed after export.");
            }
        }

        return result;
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public static class SheetPackageWriter
{
    /// <summary>
    /// Computes hashes for every sheet PDF (which must already exist next to
    /// the manifest location) and writes the manifest file. Returns the
    /// manifest path.
    /// </summary>
    public static string Write(SheetPackageManifest manifest, string directory, string baseName)
    {
        Directory.CreateDirectory(directory);
        foreach (var sheet in manifest.Sheets)
        {
            if (sheet.Format is not null)
            {
                sheet.PageFormatId = sheet.Format.Id;
                sheet.Format.GeometryHash = PageFormatSpecGeometry.ComputeHash(sheet.Format);
            }
            var pdfPath = Path.Combine(directory, sheet.PdfFileName);
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"Sheet PDF not found for manifest: {pdfPath}");
            }

            sheet.Sha256 = SheetPackageReader.ComputeSha256(pdfPath);
        }

        var manifestPath = Path.Combine(directory, baseName + SheetPackageManifest.ManifestSuffix);
        var json = JsonSerializer.Serialize(manifest, SheetPackageJson.Options);
        File.WriteAllText(manifestPath, json);
        return manifestPath;
    }
}
