using System.Collections.Concurrent;
using System.Globalization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core;

/// <summary>
/// Received sheets heavy enough to slow every album build, preview and sync, named with their
/// size and image count so their source can lighten them (Мастер, 2026-10-31: a CGA sun sheet
/// of 20 MB carried about 19 000 image objects, 9 500 of them soft masks).
/// </summary>
/// <remarks>
/// Reported, never refused, like <see cref="DrawingScaleSurvey"/>: the drawing is still the
/// drawing, and the fix belongs in the program that wrote it. A file is named once however many
/// sheets it carries; its image count is read only past the size limit, and kept by path, size
/// and write time so an unchanged sheet is not parsed again on the next build.
/// </remarks>
public static class HeavySheetSurvey
{
    public const string Prefix = "Хүнд хуудас";

    /// <summary>5 MB: past it a sheet costs seconds in every build and every preview.</summary>
    public const long LimitBytes = 5L * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, int> ImageCounts = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Review(AlbumBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var notices = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AlbumBuildPage page in request.Sections.SelectMany(section => section.Pages))
        {
            string path = page.Sheet.PdfPath ?? "";
            if (path.Length == 0 || !seen.Add(path))
                continue;

            FileInfo file;
            try
            {
                file = new FileInfo(path);
                if (!file.Exists || file.Length < LimitBytes)
                    continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            string megabytes = (file.Length / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);
            int images = CountImages(file);
            string imageText = images >= 0
                ? $", {images.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} зураг"
                : "";
            notices.Add(
                $"{Prefix}: {page.Number} {page.Title} — {megabytes} МБ{imageText}. " +
                "Альбум, preview, синкийг удаашруулна — эх үүсвэрт (CGA/PFA/PFR) хөнгөлүүлж засуулна уу.");
        }

        return notices;
    }

    /// <summary>Image XObjects in the whole file (soft masks included), or -1 when unreadable.</summary>
    private static int CountImages(FileInfo file)
    {
        string key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        return ImageCounts.GetOrAdd(key, _ =>
        {
            try
            {
                using PdfDocument document = PdfReader.Open(file.FullName, PdfDocumentOpenMode.Import);
                return document.Internals.GetAllObjects()
                    .OfType<PdfDictionary>()
                    .Count(dictionary => dictionary.Elements.GetName("/Subtype") == "/Image");
            }
            catch (Exception)
            {
                // PdfSharp throws plain Exception on a file it cannot parse; the size alone is reported.
                return -1;
            }
        });
    }
}
