using System.Security.Cryptography;

namespace ErkS.Platform.Pdf;

/// <summary>A component of the current album as the server's manifest records it.</summary>
public sealed record LocalMergeComponent(
    string Code,
    string Label,
    int Order,
    IReadOnlyList<int> PageNumbers,
    string Status,
    string OwnerEmail,
    string SourceKey,
    string ComponentKind,
    string SectionKey,
    string SequenceKey,
    IReadOnlyList<LocalMergePage> Pages);

public sealed record LocalMergePage(
    int PageNumber,
    string PageKey,
    string Title,
    string SortKey,
    string SectionKey,
    string SequenceKey);

/// <summary>A component this Studio sent: its PDF, or its removal.</summary>
public sealed record LocalMergeReplacement(
    string Code,
    string Label,
    int Order,
    string PdfPath,
    bool Remove,
    string OwnerEmail,
    string SourceKey,
    string ComponentKind,
    string SectionKey,
    string SequenceKey,
    IReadOnlyList<LocalMergePage>? Pages);

/// <summary>
/// The server's album component merge, run here on the same inputs (ErkS.AlbumMerge, compiled in
/// from the server's Shared folder). When its bytes hash to the revision the server answered
/// with, the 127 MB album need not be downloaded back (Мастер, 2026-10-31: GET /files 23.6 s of
/// every sync). Any doubt - another version, a missing input, a different hash - means download.
/// </summary>
public static class LocalAlbumMerge
{
    /// <summary>The shared merge's version in this build; 0 when it was built without it (CI).</summary>
    public static int AlgorithmVersion =>
#if ERKS_ALBUM_MERGE
        ErkS.AlbumMerge.AlbumMergeAlgorithm.Version;
#else
        0;
#endif

    /// <summary>
    /// Writes the merge to <paramref name="outputPath"/> and returns its SHA-256 (lower case), or
    /// null when this build has no shared merge or the merge refused its inputs. The orders are
    /// the merged revision's own, so the page order is the server's.
    /// </summary>
    public static string? TryWrite(
        string currentPdfPath,
        int currentPageCount,
        IReadOnlyList<LocalMergeComponent> currentManifest,
        IReadOnlyList<LocalMergeReplacement> replacements,
        IReadOnlyDictionary<string, int> mergedOrders,
        string outputPath)
    {
#if ERKS_ALBUM_MERGE
        try
        {
            ErkS.AlbumMerge.AlbumComponentMerge.Merge(
                currentPdfPath,
                currentPageCount,
                currentManifest.Select(ToMerge).ToList(),
                replacements.Select(ToMerge).ToList(),
                outputPath,
                mergedOrders);
            using FileStream stream = File.OpenRead(outputPath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
#else
        return null;
#endif
    }

#if ERKS_ALBUM_MERGE
    private static ErkS.AlbumMerge.AlbumMergeComponent ToMerge(LocalMergeComponent component) => new()
    {
        Code = component.Code,
        Label = component.Label,
        Order = component.Order,
        PageNumbers = component.PageNumbers.ToList(),
        Status = component.Status,
        OwnerEmail = component.OwnerEmail,
        SourceKey = component.SourceKey,
        ComponentKind = component.ComponentKind,
        SectionKey = component.SectionKey,
        SequenceKey = component.SequenceKey,
        Pages = component.Pages.Select(ToMerge).ToList(),
    };

    private static ErkS.AlbumMerge.AlbumMergePage ToMerge(LocalMergePage page) => new()
    {
        PageNumber = page.PageNumber,
        PageKey = page.PageKey,
        Title = page.Title,
        SortKey = page.SortKey,
        SectionKey = page.SectionKey,
        SequenceKey = page.SequenceKey,
    };

    private static ErkS.AlbumMerge.AlbumMergeReplacement ToMerge(LocalMergeReplacement replacement) => new(
        replacement.Code,
        replacement.Label,
        replacement.Order,
        replacement.PdfPath,
        replacement.Remove,
        replacement.OwnerEmail,
        replacement.SourceKey,
        replacement.ComponentKind,
        replacement.SectionKey,
        replacement.SequenceKey,
        replacement.Pages?.Select(ToMerge).ToList());
#endif
}
