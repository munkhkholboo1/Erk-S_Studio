using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

Dictionary<string, string> options = ParseOptions(args);
int[] sheetCounts = Value(options, "sheets", "100,500,1000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(int.Parse)
    .Where(value => value > 0)
    .Distinct()
    .Order()
    .ToArray();
if (sheetCounts.Length == 0)
    throw new ArgumentException("At least one positive --sheets value is required.");
int albumPages = int.Parse(Value(options, "album-pages", "500"));
int largeSourceMb = int.Parse(Value(options, "large-source-mb", "0"));
string reportPath = Path.GetFullPath(Value(
    options,
    "output",
    Path.Combine(Environment.CurrentDirectory, "artifacts", "performance", "studio-performance.json")));
bool keepArtifacts = options.ContainsKey("keep-artifacts");
string workRoot = Path.Combine(Path.GetTempPath(), "erks-studio-performance", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workRoot);

List<PerformanceMeasurement> measurements = [];
List<string> issues = [];
SheetLibrary? largestLibrary = null;

try
{
    string templateRoot = Path.Combine(workRoot, "templates");
    Directory.CreateDirectory(templateRoot);
    IReadOnlyList<PdfTemplate> templates = CreateTemplates(templateRoot);

    foreach (int sheetCount in sheetCounts)
    {
        string packageRoot = Path.Combine(workRoot, $"package-{sheetCount}");
        string manifestPath = CreatePackage(packageRoot, templates, sheetCount);
        ForceCollection();
        SheetLibrary library = new();
        PerformanceMeasurement measurement = Measure($"intake-{sheetCount}", sheetCount, () =>
        {
            SheetPackageLoadResult load = SheetPackageReader.Load(manifestPath);
            if (!load.IsLossless)
                throw new InvalidDataException(string.Join(" | ", load.Issues));
            SheetLibraryChange change = library.Absorb(load);
            if (change.Rejected || change.UpdatedSheetCount != sheetCount)
                throw new InvalidDataException("Performance package was not absorbed completely.");
        });
        measurements.Add(measurement);
        issues.AddRange(PerformanceRegressionPolicy.Evaluate(
            measurement,
            new PerformanceThreshold(
                measurement.Name,
                TimeSpan.FromSeconds(Math.Max(15, sheetCount * 0.09)),
                3072,
                2048)));
        if (largestLibrary is null || library.Snapshot().Count > largestLibrary.Snapshot().Count)
            largestLibrary = library;
    }

    if (largestLibrary is not null && albumPages > 0)
    {
        int count = Math.Min(albumPages, largestLibrary.VerifiedSnapshot().Count);
        string outputPdf = Path.Combine(workRoot, $"mixed-album-{count}.pdf");
        AlbumProject project = CreateAlbumProject(largestLibrary, count, workRoot);
        ForceCollection();
        PerformanceMeasurement albumMeasurement = Measure($"album-{count}", count, () =>
        {
            AlbumBuildResult result = new AlbumBuilder(new PdfSharpAlbumWriter())
                .Build(project, largestLibrary, outputPdf);
            if (result.PageCount != count || !File.Exists(outputPdf))
                throw new InvalidDataException("Performance album build did not produce every requested page.");
        });
        measurements.Add(albumMeasurement);
        issues.AddRange(PerformanceRegressionPolicy.Evaluate(
            albumMeasurement,
            new PerformanceThreshold(albumMeasurement.Name, TimeSpan.FromMinutes(3), 4096, 3072)));

        CanonicalPdfPreviewCache previewCache = new(Path.Combine(workRoot, "preview-cache"));
        ForceCollection();
        PerformanceMeasurement previewMeasurement = await MeasureAsync("preview-cache", count, async () =>
        {
            string previewPath = await previewCache.GetPreviewPathAsync(outputPdf);
            if (!File.Exists(previewPath) || new FileInfo(previewPath).Length != new FileInfo(outputPdf).Length)
                throw new InvalidDataException("Preview cache copy is incomplete.");
        });
        measurements.Add(previewMeasurement);
        issues.AddRange(PerformanceRegressionPolicy.Evaluate(
            previewMeasurement,
            new PerformanceThreshold(previewMeasurement.Name, TimeSpan.FromSeconds(30), 4096, 3072)));
    }

    if (largeSourceMb > 0)
    {
        string largeFile = Path.Combine(workRoot, $"source-{largeSourceMb}mb.bin");
        using (FileStream stream = new(largeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength((long)largeSourceMb * 1024 * 1024);
        ForceCollection();
        PerformanceMeasurement hashMeasurement = Measure($"source-hash-{largeSourceMb}mb", largeSourceMb, () =>
        {
            using FileStream stream = new(largeFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            byte[] hash = SHA256.HashData(stream);
            if (hash.Length != 32)
                throw new InvalidDataException("Large source hash was not produced.");
        });
        measurements.Add(hashMeasurement);
        issues.AddRange(PerformanceRegressionPolicy.Evaluate(
            hashMeasurement,
            new PerformanceThreshold(hashMeasurement.Name, TimeSpan.FromMinutes(2), 4096, 3072)));
    }

    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        generatedAtUtc = DateTimeOffset.UtcNow,
        machine = Environment.MachineName,
        framework = Environment.Version.ToString(),
        processorCount = Environment.ProcessorCount,
        measurements = measurements.Select(item => new
        {
            item.Name,
            item.ItemCount,
            durationMilliseconds = Math.Round(item.Duration.TotalMilliseconds, 2),
            item.PeakWorkingSetMegabytes,
            item.PeakHandleCount,
        }),
        issues,
    }, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine(await File.ReadAllTextAsync(reportPath));
    if (issues.Count > 0)
    {
        Console.Error.WriteLine("Performance regression threshold failed: " + string.Join(" | ", issues));
        return 2;
    }
    return 0;
}
finally
{
    if (!keepArtifacts)
    {
        try
        {
            Directory.Delete(workRoot, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Performance temp cleanup warning: " + error.Message);
        }
    }
    else
    {
        Console.WriteLine("Performance artifacts: " + workRoot);
    }
}

static IReadOnlyList<PdfTemplate> CreateTemplates(string root)
{
    PdfTemplate[] templates =
    [
        new("A3 landscape", 420, 297),
        new("A4 portrait", 210, 297),
        new("Custom 630x297", 630, 297),
    ];
    foreach (PdfTemplate template in templates)
    {
        string path = Path.Combine(root, SafeName(template.Name) + ".pdf");
        using PdfDocument document = new();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(template.WidthMm);
        page.Height = XUnit.FromMillimeter(template.HeightMm);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(new XPen(XColors.Black, 0.5), 12, 12, page.Width.Point - 24, page.Height.Point - 24);
        graphics.DrawLine(new XPen(XColors.Black, 0.25), 24, 24, page.Width.Point - 24, page.Height.Point - 24);
        document.Save(path);
        template.Path = path;
    }
    return templates;
}

static string CreatePackage(string root, IReadOnlyList<PdfTemplate> templates, int sheetCount)
{
    Directory.CreateDirectory(root);
    SheetPackageManifest manifest = new()
    {
        SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
        PackageScope = SheetPackageScope.FullSnapshot,
        Source = new SheetPackageSource
        {
            SourceId = $"performance-source-{sheetCount}",
            Application = SheetSourceApplication.AutoCad,
            ApplicationVersion = "Performance harness",
            DocumentTitle = $"Synthetic mixed-format {sheetCount}",
        },
    };
    for (int index = 0; index < sheetCount; index++)
    {
        PdfTemplate template = templates[index % templates.Count];
        string fileName = $"sheet-{index + 1:0000}.pdf";
        File.Copy(template.Path, Path.Combine(root, fileName));
        manifest.Sheets.Add(new SheetPackageEntry
        {
            SheetId = $"PERF-{index + 1:0000}",
            Number = $"P-{index + 1:0000}",
            Name = $"Performance sheet {index + 1}",
            Discipline = "AR",
            WidthMm = template.WidthMm,
            HeightMm = template.HeightMm,
            PdfFileName = fileName,
            PageCount = 1,
            ContentKind = (index % 4) switch
            {
                0 => "FloorPlan",
                1 => "Section",
                2 => "Elevation",
                _ => "Detail",
            },
        });
    }
    return SheetPackageWriter.Write(manifest, root, $"performance-{sheetCount}");
}

static AlbumProject CreateAlbumProject(SheetLibrary library, int pageCount, string root)
{
    IReadOnlyList<SheetRecord> sheets = library.VerifiedSnapshot().Take(pageCount).ToList();
    return new AlbumProject
    {
        Name = "Performance project",
        Code = "PERF",
        ProjectFolder = root,
        OutputFolder = root,
        Album = new AlbumDefinition
        {
            Title = "Mixed-format performance album",
            IncludeCover = false,
            IncludeTableOfContents = false,
            Pages = sheets.Select(sheet => new AlbumPageDefinition
            {
                SheetKey = sheet.Key,
                PageFormatId = PageFormatCatalog.SourceAsIsId,
                PlacementMode = PagePlacementMode.FullPage,
            }).ToList(),
        },
    };
}

static PerformanceMeasurement Measure(string name, int itemCount, Action action)
{
    Process process = Process.GetCurrentProcess();
    process.Refresh();
    long memoryBefore = process.WorkingSet64;
    int handlesBefore = SafeHandleCount(process);
    Stopwatch stopwatch = Stopwatch.StartNew();
    action();
    stopwatch.Stop();
    process.Refresh();
    return new PerformanceMeasurement(
        name,
        itemCount,
        stopwatch.Elapsed,
        BytesToMegabytes(Math.Max(memoryBefore, process.WorkingSet64)),
        Math.Max(handlesBefore, SafeHandleCount(process)));
}

static async Task<PerformanceMeasurement> MeasureAsync(string name, int itemCount, Func<Task> action)
{
    Process process = Process.GetCurrentProcess();
    process.Refresh();
    long memoryBefore = process.WorkingSet64;
    int handlesBefore = SafeHandleCount(process);
    Stopwatch stopwatch = Stopwatch.StartNew();
    await action();
    stopwatch.Stop();
    process.Refresh();
    return new PerformanceMeasurement(
        name,
        itemCount,
        stopwatch.Elapsed,
        BytesToMegabytes(Math.Max(memoryBefore, process.WorkingSet64)),
        Math.Max(handlesBefore, SafeHandleCount(process)));
}

static int SafeHandleCount(Process process)
{
    try
    {
        return process.HandleCount;
    }
    catch (PlatformNotSupportedException)
    {
        return 0;
    }
}

static long BytesToMegabytes(long bytes) => (long)Math.Ceiling(bytes / 1024d / 1024d);

static void ForceCollection()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    Dictionary<string, string> parsed = new(StringComparer.OrdinalIgnoreCase);
    for (int index = 0; index < values.Length; index++)
    {
        string key = values[index].TrimStart('-');
        if (index + 1 < values.Length && !values[index + 1].StartsWith("--", StringComparison.Ordinal))
            parsed[key] = values[++index];
        else
            parsed[key] = "true";
    }
    return parsed;
}

static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
    values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

static string SafeName(string value) => string.Concat(value.Select(character =>
    Path.GetInvalidFileNameChars().Contains(character) || char.IsWhiteSpace(character) ? '-' : char.ToLowerInvariant(character)));

sealed class PdfTemplate(string name, double widthMm, double heightMm)
{
    public string Name { get; } = name;
    public double WidthMm { get; } = widthMm;
    public double HeightMm { get; } = heightMm;
    public string Path { get; set; } = "";
}
