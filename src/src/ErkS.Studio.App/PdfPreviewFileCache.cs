using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>Keeps PDF viewers away from canonical album outputs, which must remain replaceable during rebuild and sync.</summary>
internal static class PdfPreviewFileCache
{
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Erk-S Studio",
        "pdf-preview-cache");
    private static readonly CanonicalPdfPreviewCache Cache = new(CacheRoot);

    public static async Task<string> GetPreviewPathAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
        => await Cache.GetPreviewPathAsync(pdfPath, cancellationToken).ConfigureAwait(false);
}
