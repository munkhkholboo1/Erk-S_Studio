using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

/// <summary>
/// Extracts one rendered component from a complete or partial Studio album
/// without rasterizing it. The resulting PDF keeps the source vector content.
/// </summary>
public static class AlbumComponentPdfExtractor
{
    public static int Extract(
        string sourcePdfPath,
        IEnumerable<int> pageNumbers,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!File.Exists(sourcePdfPath))
            throw new FileNotFoundException("Rendered album PDF was not found.", sourcePdfPath);

        List<int> pages = (pageNumbers ?? [])
            .Distinct()
            .Order()
            .ToList();
        if (pages.Count == 0)
            throw new InvalidDataException("An album component must contain at least one page.");

        using PdfDocument source = PdfReader.Open(sourcePdfPath, PdfDocumentOpenMode.Import);
        if (pages.Any(page => page < 1 || page > source.PageCount))
            throw new InvalidDataException("Album component contains an out-of-range page number.");

        using var output = new PdfDocument();
        foreach (int page in pages)
            output.AddPage(source.Pages[page - 1]);

        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        int pageCount = output.PageCount;
        output.Save(fullOutputPath);
        return pageCount;
    }

    /// <summary>
    /// Several components cut in one reading of the album: each was opening the whole local
    /// album again - 9 s of a 127 MB Erin refresh (profiled 2026-10-31). Every range is checked
    /// before anything is written, so a bad one leaves no partial set behind.
    /// </summary>
    public static IReadOnlyList<int> ExtractMany(
        string sourcePdfPath,
        IReadOnlyList<(IEnumerable<int> PageNumbers, string OutputPath)> components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdfPath);
        ArgumentNullException.ThrowIfNull(components);
        if (!File.Exists(sourcePdfPath))
            throw new FileNotFoundException("Rendered album PDF was not found.", sourcePdfPath);

        var plans = components
            .Select(component => (
                Pages: (component.PageNumbers ?? []).Distinct().Order().ToList(),
                component.OutputPath))
            .ToList();
        if (plans.Count == 0)
            return [];
        if (plans.Any(plan => plan.Pages.Count == 0))
            throw new InvalidDataException("An album component must contain at least one page.");
        if (plans.Any(plan => string.IsNullOrWhiteSpace(plan.OutputPath)))
            throw new InvalidDataException("An album component needs an output path.");

        using PdfDocument source = PdfReader.Open(sourcePdfPath, PdfDocumentOpenMode.Import);
        if (plans.Any(plan => plan.Pages.Any(page => page < 1 || page > source.PageCount)))
            throw new InvalidDataException("Album component contains an out-of-range page number.");

        var counts = new List<int>(plans.Count);
        foreach ((List<int> pages, string outputPath) in plans)
        {
            using var output = new PdfDocument();
            foreach (int page in pages)
                output.AddPage(source.Pages[page - 1]);
            string fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            counts.Add(output.PageCount);
            output.Save(fullOutputPath);
        }
        return counts;
    }
}
