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
}
