using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfSharp.Pdf.IO;

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
    private readonly Dictionary<SheetPackageEntry, string> resolvedPdfPaths =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SheetPackageEntry, VerifiedFileState> verifiedPdfStates =
        new(ReferenceEqualityComparer.Instance);
    private VerifiedFileState? verifiedManifestState;

    public required string ManifestPath { get; init; }

    public SheetPackageManifest? Manifest { get; init; }

    /// <summary>SHA-256 of the manifest bytes read during verification.</summary>
    public string ManifestSha256 { get; init; } = "";

    public List<string> Issues { get; } = [];

    /// <summary>True only when the manifest and every referenced PDF passed all checks.</summary>
    public bool IsLossless => Manifest is not null && Issues.Count == 0;

    internal void SetResolvedPdfPath(SheetPackageEntry entry, string path)
    {
        resolvedPdfPaths[entry] = path;
        verifiedPdfStates[entry] = VerifiedFileState.Capture(path);
    }

    internal void SetVerifiedManifestState() =>
        verifiedManifestState = VerifiedFileState.Capture(ManifestPath);

    /// <summary>
    /// Returns a package-contained path only after the entire package passed verification.
    /// Consumers must not resolve manifest paths independently.
    /// </summary>
    public bool TryGetVerifiedPdfPath(SheetPackageEntry entry, out string path)
    {
        if (IsLossless && resolvedPdfPaths.TryGetValue(entry, out var resolved))
        {
            path = resolved;
            return true;
        }

        path = "";
        return false;
    }

    /// <summary>
    /// Cheap dirty check used between package intake and project reconciliation.
    /// The expensive hash and PDF structure checks are reused only while every
    /// verified file still has the same filesystem identity metadata.
    /// </summary>
    public bool IsVerificationCurrent()
    {
        if (!IsLossless || verifiedManifestState is not { } manifestState ||
            !manifestState.Matches(ManifestPath))
        {
            return false;
        }

        foreach (SheetPackageEntry entry in Manifest!.Sheets)
        {
            if (!resolvedPdfPaths.TryGetValue(entry, out string? path) ||
                !verifiedPdfStates.TryGetValue(entry, out VerifiedFileState state) ||
                !state.Matches(path))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct VerifiedFileState(
        long Length,
        long LastWriteTimeUtcTicks,
        long CreationTimeUtcTicks)
    {
        public static VerifiedFileState Capture(string path)
        {
            var file = new FileInfo(path);
            file.Refresh();
            return new VerifiedFileState(
                file.Length,
                file.LastWriteTimeUtc.Ticks,
                file.CreationTimeUtc.Ticks);
        }

        public bool Matches(string path)
        {
            try
            {
                var file = new FileInfo(path);
                file.Refresh();
                return file.Exists &&
                    file.Length == Length &&
                    file.LastWriteTimeUtc.Ticks == LastWriteTimeUtcTicks &&
                    file.CreationTimeUtc.Ticks == CreationTimeUtcTicks;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
    }
}

/// <summary>Resolves package files without allowing absolute paths, traversal or links outside the package.</summary>
public static class SheetPackagePathSecurity
{
    public static bool TryResolvePackageFile(
        string packageDirectory,
        string? relativePath,
        out string fullPath,
        out string issue)
    {
        fullPath = "";
        issue = "";

        try
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                issue = "unsafe PDF path: package file path is empty.";
                return false;
            }
            if (relativePath.IndexOf('\0') >= 0)
            {
                issue = "unsafe PDF path: package file path contains an invalid null character.";
                return false;
            }

            var value = relativePath.Trim();
            var segments = value.Split(['/', '\\'], StringSplitOptions.None);
            if (segments.Any(segment => segment is "." or ".."))
            {
                issue = "unsafe PDF path: dot-segment traversal is not allowed.";
                return false;
            }
            if (value.StartsWith('/') || value.StartsWith('\\') ||
                Path.IsPathRooted(value) || IsWindowsDrivePath(value) ||
                Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                issue = "unsafe PDF path: absolute paths and URIs are not allowed.";
                return false;
            }

            var root = Path.GetFullPath(packageDirectory);
            var normalizedRelativePath = value
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
            if (!TryValidateResolvedPackagePath(root, candidate, out issue))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            issue = $"unsafe PDF path: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Defense-in-depth validation for callers that already hold normalized
    /// absolute paths. The candidate does not need to exist for containment checks.
    /// </summary>
    public static bool TryValidateResolvedPackagePath(
        string packageRoot,
        string candidatePath,
        out string issue)
    {
        issue = "";
        var root = Path.GetFullPath(packageRoot);
        var candidate = Path.GetFullPath(candidatePath);
        var relativeToRoot = Path.GetRelativePath(root, candidate);
        if (relativeToRoot == ".." ||
            relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeToRoot))
        {
            issue = "unsafe PDF path: resolved file is outside the package directory.";
            return false;
        }
        if (HasReparsePoint(root, candidate))
        {
            issue = "unsafe PDF path: symbolic links and reparse points are not allowed.";
            return false;
        }

        return true;
    }

    private static bool IsWindowsDrivePath(string value) =>
        value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';

    private static bool HasReparsePoint(string root, string candidate)
    {
        var current = root;
        if (Exists(current) && IsReparsePoint(current))
        {
            return true;
        }

        var relative = Path.GetRelativePath(root, candidate);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Exists(current) && IsReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

public static class SheetPackageReader
{
    private const double PageSizeToleranceMm = 0.75;

    /// <summary>
    /// Loads and verifies a package. Bad or hostile input never becomes a valid
    /// package; expected data/IO failures are returned in <see cref="SheetPackageLoadResult.Issues"/>.
    /// </summary>
    public static SheetPackageLoadResult Load(string manifestPath)
    {
        return LoadCore(manifestPath, null, null);
    }

    /// <summary>
    /// Rehydrates a package previously accepted into a persisted project.
    /// Matching manifest and PDF hashes prove the bytes are identical to the
    /// fully verified package, so reopening does not parse every PDF again.
    /// A different package automatically receives the normal full validation.
    /// </summary>
    public static SheetPackageLoadResult LoadForHydration(
        string manifestPath,
        Guid expectedPackageId,
        string expectedManifestSha256)
    {
        return LoadCore(manifestPath, expectedPackageId, expectedManifestSha256);
    }

    private static SheetPackageLoadResult LoadCore(
        string manifestPath,
        Guid? expectedPackageId,
        string? expectedManifestSha256)
    {
        SheetPackageManifest? manifest = null;
        string manifestSha256 = "";
        var issues = new List<string>();

        try
        {
            manifestSha256 = ComputeSha256(manifestPath);
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<SheetPackageManifest>(json, SheetPackageJson.Options);
            if (manifest is null)
            {
                issues.Add("Manifest deserialized to null.");
            }
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            issues.Add($"Manifest could not be read: {exception.Message}");
        }

        var result = new SheetPackageLoadResult
        {
            ManifestPath = manifestPath,
            Manifest = manifest,
            ManifestSha256 = manifestSha256,
        };
        if (manifest is not null)
        {
            try
            {
                result.SetVerifiedManifestState();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                issues.Add($"Manifest state could not be verified: {exception.Message}");
            }
        }
        result.Issues.AddRange(issues);
        if (manifest is null)
        {
            return result;
        }

        manifest.Source ??= new SheetPackageSource();
        manifest.Sheets ??= [];
        ValidateManifestHeader(manifest, result.Issues);
        bool recordedPackageBytes =
            expectedPackageId.HasValue &&
            manifest.PackageId == expectedPackageId.Value &&
            !string.IsNullOrWhiteSpace(expectedManifestSha256) &&
            manifestSha256.Equals(
                expectedManifestSha256.Trim(),
                StringComparison.OrdinalIgnoreCase);

        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        var sheetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pdfStructureCache = new Dictionary<string, VerifiedPdfStructure>(StringComparer.OrdinalIgnoreCase);
        bool usesPageReferences = manifest.SchemaVersion >= 5;

        foreach (var sheet in manifest.Sheets)
        {
            ValidateEntryMetadata(manifest, sheet, result.Issues);

            if (!string.IsNullOrWhiteSpace(sheet.SheetId) && !sheetIds.Add(sheet.SheetId.Trim()))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': duplicate sheet id '{sheet.SheetId}'.");
            }
            var normalizedFileName = (sheet.PdfFileName ?? "").Trim().Replace('\\', '/');
            if (!usesPageReferences &&
                !string.IsNullOrWhiteSpace(normalizedFileName) &&
                !fileNames.Add(normalizedFileName))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': duplicate PDF filename '{sheet.PdfFileName}'.");
            }

            if (!SheetPackagePathSecurity.TryResolvePackageFile(
                    directory,
                    sheet.PdfFileName,
                    out var pdfPath,
                    out var pathIssue))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': {pathIssue}");
                continue;
            }
            if (!usesPageReferences && !resolvedPaths.Add(pdfPath))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': duplicate resolved PDF path '{sheet.PdfFileName}'.");
            }
            else if (usesPageReferences &&
                     !pageReferences.Add($"{pdfPath}\0{sheet.PdfPageNumber}"))
            {
                result.Issues.Add(
                    $"Sheet '{sheet.Number}': duplicate PDF page reference " +
                    $"'{sheet.PdfFileName}' page {sheet.PdfPageNumber}.");
            }
            if (!string.Equals(Path.GetExtension(pdfPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': referenced package file is not a PDF.");
                continue;
            }
            if (!File.Exists(pdfPath))
            {
                result.Issues.Add($"Sheet '{sheet.Number}': PDF file missing ({sheet.PdfFileName}).");
                continue;
            }

            VerifyHash(sheet, pdfPath, result.Issues, hashCache);
            if (!recordedPackageBytes)
            {
                VerifyPdfStructure(manifest, sheet, pdfPath, result.Issues, pdfStructureCache);
            }
            try
            {
                result.SetResolvedPdfPath(sheet, pdfPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                result.Issues.Add($"Sheet '{sheet.Number}': PDF state could not be verified ({exception.Message}).");
            }
        }

        return result;
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ValidateManifestHeader(SheetPackageManifest manifest, ICollection<string> issues)
    {
        if (manifest.SchemaVersion <= 0 || manifest.SchemaVersion > SheetPackageManifest.CurrentSchemaVersion)
        {
            issues.Add(
                $"Manifest schema {manifest.SchemaVersion} is unsupported; supported versions are 1-{SheetPackageManifest.CurrentSchemaVersion}.");
        }
        if (manifest.PackageId == Guid.Empty)
        {
            issues.Add("Manifest package id is missing.");
        }
        if (manifest.SchemaVersion >= 4 && string.IsNullOrWhiteSpace(manifest.Source.SourceId))
        {
            issues.Add("Manifest source id is required for schema version 4 or newer.");
        }
        if (manifest.Sheets.Count == 0 && manifest.PackageScope != SheetPackageScope.FullSnapshot)
        {
            issues.Add("Package contains no sheets.");
        }
    }

    private static void ValidateEntryMetadata(
        SheetPackageManifest manifest,
        SheetPackageEntry sheet,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(sheet.SheetId))
        {
            issues.Add($"Sheet '{sheet.Number}': sheet id is missing.");
        }
        if (string.IsNullOrWhiteSpace(sheet.PdfFileName))
        {
            issues.Add($"Sheet '{sheet.Number}': PDF file name is missing.");
        }
        if (sheet.PageCount <= 0)
        {
            issues.Add($"Sheet '{sheet.Number}': page count must be positive.");
        }
        if (manifest.SchemaVersion >= 5 && sheet.PdfPageNumber <= 0)
        {
            issues.Add($"Sheet '{sheet.Number}': PDF page number must be positive for schema version 5 or newer.");
        }
        if (manifest.SchemaVersion >= 4 && (!IsPositiveFinite(sheet.WidthMm) || !IsPositiveFinite(sheet.HeightMm)))
        {
            issues.Add($"Sheet '{sheet.Number}': physical page size must be positive finite millimetres.");
        }

        if (sheet.Format is not null)
        {
            foreach (var issue in PageFormatSpecGeometry.Validate(sheet.Format))
            {
                issues.Add($"Sheet '{sheet.Number}': {issue}");
            }
            if (!string.IsNullOrWhiteSpace(sheet.PageFormatId) &&
                !string.Equals(sheet.PageFormatId, sheet.Format.Id, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Sheet '{sheet.Number}': page format id does not match the inline format.");
            }
            if (IsPositiveFinite(sheet.WidthMm) && IsPositiveFinite(sheet.HeightMm) &&
                (!NearlyEqual(sheet.WidthMm, sheet.Format.WidthMm) ||
                 !NearlyEqual(sheet.HeightMm, sheet.Format.HeightMm)))
            {
                issues.Add($"Sheet '{sheet.Number}': manifest page size does not match the inline format.");
            }
        }

        if (!sheet.IsCleanDrawingSpace)
        {
            return;
        }
        if (sheet.Format is null)
        {
            issues.Add($"Sheet '{sheet.Number}': clean drawing-space PDF requires an inline format.");
        }
        else if (!NearlyEqual(sheet.ContentWidthMm, sheet.Format.DrawingArea.Width, 0.01) ||
                 !NearlyEqual(sheet.ContentHeightMm, sheet.Format.DrawingArea.Height, 0.01))
        {
            issues.Add($"Sheet '{sheet.Number}': content size does not match the format drawing area.");
        }
    }

    private static void VerifyHash(
        SheetPackageEntry sheet,
        string pdfPath,
        ICollection<string> issues,
        IDictionary<string, string> hashCache)
    {
        try
        {
            if (!hashCache.TryGetValue(pdfPath, out string? actual))
            {
                actual = ComputeSha256(pdfPath);
                hashCache[pdfPath] = actual;
            }
            if (!string.Equals(actual, sheet.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Sheet '{sheet.Number}': PDF hash mismatch - file changed after export.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Sheet '{sheet.Number}': PDF hash could not be verified ({exception.Message}).");
        }
    }

    private static void VerifyPdfStructure(
        SheetPackageManifest manifest,
        SheetPackageEntry sheet,
        string pdfPath,
        ICollection<string> issues,
        IDictionary<string, VerifiedPdfStructure> structureCache)
    {
        try
        {
            if (!structureCache.TryGetValue(pdfPath, out VerifiedPdfStructure? structure))
            {
                using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                structure = new VerifiedPdfStructure(
                    document.Pages
                        .Cast<PdfSharp.Pdf.PdfPage>()
                        .Select(page => new VerifiedPdfPageSize(
                            page.Width.Millimeter,
                            page.Height.Millimeter))
                        .ToArray());
                structureCache[pdfPath] = structure;
            }

            if (manifest.SchemaVersion < 5 && structure.PageCount != sheet.PageCount)
            {
                issues.Add(
                    $"Sheet '{sheet.Number}': PDF page count {structure.PageCount} does not match manifest {sheet.PageCount}.");
            }
            if (structure.PageCount == 0)
            {
                issues.Add($"Sheet '{sheet.Number}': PDF contains no pages.");
                return;
            }

            int pageIndex = manifest.SchemaVersion >= 5 ? sheet.PdfPageNumber - 1 : 0;
            if (pageIndex < 0 || pageIndex >= structure.PageCount)
            {
                issues.Add(
                    $"Sheet '{sheet.Number}': referenced PDF page {sheet.PdfPageNumber} is outside " +
                    $"the document's {structure.PageCount} pages.");
                return;
            }

            var expectedWidth = sheet.IsCleanDrawingSpace ? sheet.ContentWidthMm : sheet.WidthMm;
            var expectedHeight = sheet.IsCleanDrawingSpace ? sheet.ContentHeightMm : sheet.HeightMm;
            VerifiedPdfPageSize page = structure.Pages[pageIndex];
            // Schema 1-3 producers did not consistently report physical PDF
            // geometry. Keep them readable; schema 4 makes geometry authoritative.
            if (manifest.SchemaVersion >= 4 &&
                (!NearlyEqual(page.WidthMm, expectedWidth) ||
                 !NearlyEqual(page.HeightMm, expectedHeight)))
            {
                issues.Add(
                    $"Sheet '{sheet.Number}': PDF page size {page.WidthMm:0.###} x " +
                    $"{page.HeightMm:0.###} mm does not match manifest " +
                    $"{expectedWidth:0.###} x {expectedHeight:0.###} mm.");
            }
        }
        catch (Exception exception)
        {
            issues.Add($"Sheet '{sheet.Number}': PDF structure could not be verified ({exception.Message}).");
        }
    }

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    private static bool NearlyEqual(double left, double right, double tolerance = PageSizeToleranceMm) =>
        Math.Abs(left - right) <= tolerance;

    private sealed record VerifiedPdfStructure(IReadOnlyList<VerifiedPdfPageSize> Pages)
    {
        public int PageCount => Pages.Count;
    }

    private sealed record VerifiedPdfPageSize(double WidthMm, double HeightMm);
}

public static class SheetPackageWriter
{
    /// <summary>
    /// Computes hashes, validates the complete package, and atomically publishes
    /// its manifest. A producer cannot publish a manifest the reader would reject.
    /// </summary>
    public static string Write(SheetPackageManifest manifest, string directory, string baseName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(baseName) ||
            !string.Equals(baseName, Path.GetFileName(baseName), StringComparison.Ordinal))
        {
            throw new ArgumentException("Manifest base name must be a simple file name.", nameof(baseName));
        }

        Directory.CreateDirectory(directory);
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in manifest.Sheets)
        {
            if (sheet.Format is not null)
            {
                sheet.PageFormatId = sheet.Format.Id;
                sheet.Format.GeometryHash = PageFormatSpecGeometry.ComputeHash(sheet.Format);
            }
            if (!SheetPackagePathSecurity.TryResolvePackageFile(
                    directory,
                    sheet.PdfFileName,
                    out var pdfPath,
                    out var issue))
            {
                throw new InvalidDataException($"Sheet '{sheet.Number}': {issue}");
            }
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"Sheet PDF not found for manifest: {pdfPath}");
            }

            if (!hashCache.TryGetValue(pdfPath, out string? sha256))
            {
                sha256 = SheetPackageReader.ComputeSha256(pdfPath);
                hashCache[pdfPath] = sha256;
            }
            sheet.Sha256 = sha256;
        }

        var manifestPath = Path.Combine(Path.GetFullPath(directory), baseName + SheetPackageManifest.ManifestSuffix);
        var temporaryPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(manifest, SheetPackageJson.Options);
            File.WriteAllText(temporaryPath, json);
            var verification = SheetPackageReader.Load(temporaryPath);
            if (!verification.IsLossless)
            {
                throw new InvalidDataException(
                    "Sheet package validation failed: " + string.Join(" | ", verification.Issues));
            }

            File.Move(temporaryPath, manifestPath, overwrite: true);
            return manifestPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
