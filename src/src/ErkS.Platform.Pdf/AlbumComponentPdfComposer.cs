using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

public sealed record AlbumComponentPdfSlot(
    string Code,
    int Order,
    IReadOnlyList<int> PageNumbers);

public sealed record AlbumComponentPdfPatch(
    string Code,
    int Order,
    string PdfPath,
    bool Remove = false);

public sealed record AlbumComponentPdfCompositionResult(
    int PageCount,
    IReadOnlyList<AlbumComponentPdfSlot> Components);

/// <summary>
/// Builds a local working preview by replacing selected components in an
/// immutable canonical PDF. Unmentioned components are copied byte-for-byte at
/// the PDF object level, so one contributor cannot erase another contributor's
/// pages during a local refresh.
/// </summary>
public static class AlbumComponentPdfComposer
{
    public static AlbumComponentPdfCompositionResult Compose(
        string canonicalPdfPath,
        int canonicalPageCount,
        IReadOnlyList<AlbumComponentPdfSlot> canonicalComponents,
        IReadOnlyList<AlbumComponentPdfPatch> patches,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!File.Exists(canonicalPdfPath))
            throw new FileNotFoundException("Canonical album PDF was not found.", canonicalPdfPath);
        if (canonicalPageCount < 1)
            throw new ArgumentOutOfRangeException(nameof(canonicalPageCount));
        if (Path.GetFullPath(canonicalPdfPath).Equals(
                Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Canonical album PDF cannot be overwritten in place.");
        }

        List<AlbumComponentPdfSlot> canonical = ValidateCanonicalComponents(
            canonicalComponents,
            canonicalPageCount);
        List<AlbumComponentPdfPatch> incoming = (patches ?? [])
            .Where(item => item is not null)
            .Select(item => item with
            {
                Code = (item.Code ?? "").Trim(),
                PdfPath = item.PdfPath ?? "",
            })
            .ToList();
        if (incoming.Any(item => string.IsNullOrWhiteSpace(item.Code)))
            throw new InvalidDataException("Every album component patch requires a stable code.");
        if (incoming.Select(item => item.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != incoming.Count)
        {
            throw new InvalidDataException("Album component patch codes must be unique.");
        }

        Dictionary<string, AlbumComponentPdfPatch> patchByCode = incoming
            .ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        var specs = canonical.Select(item => new ComponentSpec(
                item.Code,
                item.Order,
                item.PageNumbers,
                patchByCode.GetValueOrDefault(item.Code)))
            .ToList();
        foreach (AlbumComponentPdfPatch patch in incoming)
        {
            if (canonical.Any(item => item.Code.Equals(
                    patch.Code,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            specs.Add(new ComponentSpec(patch.Code, patch.Order, [], patch));
        }

        specs = specs
            .OrderBy(item => item.Order)
            .ThenBy(item => item.ExistingPages.Count == 0 ? int.MaxValue : item.ExistingPages[0])
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        using PdfDocument current = PdfReader.Open(canonicalPdfPath, PdfDocumentOpenMode.Import);
        if (current.PageCount != canonicalPageCount)
            throw new InvalidDataException("Canonical album page count does not match its component manifest.");

        using var output = new PdfDocument();
        var resultComponents = new List<AlbumComponentPdfSlot>();
        foreach (ComponentSpec spec in specs)
        {
            if (spec.Patch is { Remove: true })
                continue;

            int firstOutputPage = output.PageCount + 1;
            if (spec.Patch is not null)
            {
                if (string.IsNullOrWhiteSpace(spec.Patch.PdfPath) ||
                    !File.Exists(spec.Patch.PdfPath))
                {
                    throw new FileNotFoundException(
                        $"Album component '{spec.Code}' PDF was not found.",
                        spec.Patch.PdfPath);
                }

                using PdfDocument replacement = PdfReader.Open(
                    spec.Patch.PdfPath,
                    PdfDocumentOpenMode.Import);
                if (replacement.PageCount < 1)
                    throw new InvalidDataException($"Album component '{spec.Code}' has no pages.");
                foreach (PdfPage page in replacement.Pages)
                    output.AddPage(page);
            }
            else
            {
                foreach (int pageNumber in spec.ExistingPages)
                    output.AddPage(current.Pages[pageNumber - 1]);
            }

            int addedPageCount = output.PageCount - firstOutputPage + 1;
            if (addedPageCount < 1)
                continue;
            resultComponents.Add(new AlbumComponentPdfSlot(
                spec.Code,
                spec.Order,
                Enumerable.Range(firstOutputPage, addedPageCount).ToArray()));
        }

        if (output.PageCount < 1)
            throw new InvalidDataException("Merged album preview has no pages.");
        int outputPageCount = output.PageCount;
        output.Save(fullOutputPath);
        return new AlbumComponentPdfCompositionResult(outputPageCount, resultComponents);
    }

    private static List<AlbumComponentPdfSlot> ValidateCanonicalComponents(
        IReadOnlyList<AlbumComponentPdfSlot> components,
        int pageCount)
    {
        List<AlbumComponentPdfSlot> normalized = (components ?? [])
            .Where(item => item is not null)
            .Select(item => new AlbumComponentPdfSlot(
                (item.Code ?? "").Trim(),
                item.Order,
                (item.PageNumbers ?? []).Distinct().Order().ToArray()))
            .ToList();
        if (normalized.Count == 0 || normalized.Any(item => string.IsNullOrWhiteSpace(item.Code)))
            throw new InvalidDataException("Canonical album component manifest is incomplete.");
        if (normalized.Select(item => item.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != normalized.Count)
        {
            throw new InvalidDataException("Canonical album component codes must be unique.");
        }

        int[] pages = normalized.SelectMany(item => item.PageNumbers).Order().ToArray();
        if (pages.Length != pages.Distinct().Count() ||
            !pages.SequenceEqual(Enumerable.Range(1, pageCount)))
        {
            throw new InvalidDataException(
                "Canonical album component manifest must cover every page exactly once.");
        }

        return normalized;
    }

    private sealed record ComponentSpec(
        string Code,
        int Order,
        IReadOnlyList<int> ExistingPages,
        AlbumComponentPdfPatch? Patch);
}
