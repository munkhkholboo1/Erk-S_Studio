using System.Diagnostics;
using System.IO;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    /// <summary>
    /// Makes the merged album here instead of downloading it: the server's merge is compiled in
    /// (ErkS.AlbumMerge), so the same inputs give the same bytes. The copy is kept only when its
    /// SHA-256 is the revision's; anything else - another merge version, no cached current
    /// album, a refused merge, a different hash - leaves the download to happen as before
    /// (Мастер, 2026-10-31: GET /files was 23.6 s of every Erin sync, 127 MB).
    /// </summary>
    private void AdoptLocalMerge(
        StudioCloudAlbum album,
        StudioCloudAlbumRevision current,
        StudioCloudAlbumRevision merged,
        IReadOnlyList<StudioAlbumComponentUpload> uploads,
        string ownerEmail)
    {
        var watch = Stopwatch.StartNew();
        string outcome = AdoptLocalMergeCore(album, current, merged, uploads, ownerEmail);
        RecordDiagnosticOperation(
            StudioOperationDiagnosticLog.CreateOperationId(),
            "cloud_album_local_merge",
            outcome == "adopted" ? "completed" : "skipped",
            "local_merge_" + outcome,
            $"R{merged.RevisionNumber}: {outcome} ({watch.Elapsed.TotalSeconds:0.0} с).");
    }

    private string AdoptLocalMergeCore(
        StudioCloudAlbum album,
        StudioCloudAlbumRevision current,
        StudioCloudAlbumRevision merged,
        IReadOnlyList<StudioAlbumComponentUpload> uploads,
        string ownerEmail)
    {
        if (LocalAlbumMerge.AlgorithmVersion == 0)
            return "unavailable";
        if (merged.MergeAlgorithmVersion != LocalAlbumMerge.AlgorithmVersion)
            return "version_differs";
        if (uploads.Count == 0 || string.IsNullOrWhiteSpace(merged.PdfSha256))
            return "nothing_to_repeat";

        string currentPath = CloudAlbumPreviewPath(album, current);
        if (!File.Exists(currentPath) ||
            !ComputeFileSha256(currentPath).Equals(current.PdfSha256, StringComparison.OrdinalIgnoreCase))
        {
            return "no_current_album";
        }

        string target = CloudAlbumPreviewPath(album, merged);
        if (File.Exists(target))
            return "already_present";

        var orders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (StudioCloudAlbumSection component in merged.SectionManifest)
            orders.TryAdd(component.Code, component.Order);

        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string? hash = LocalAlbumMerge.TryWrite(
                currentPath,
                current.PageCount,
                current.SectionManifest.Select(ToLocalMerge).ToList(),
                uploads.Select(upload => new LocalMergeReplacement(
                        upload.Code,
                        upload.Label,
                        orders.TryGetValue(upload.Code, out int order) ? order : upload.Order,
                        upload.PdfPath,
                        upload.Remove,
                        owner,
                        upload.SourceKey,
                        upload.ComponentKind,
                        upload.SectionKey,
                        upload.SequenceKey,
                        upload.Pages?.Select(ToLocalMerge).ToList()))
                    .ToList(),
                orders,
                temporary);
            if (hash is null)
                return "merge_refused";
            if (!hash.Equals(merged.PdfSha256, StringComparison.OrdinalIgnoreCase))
                return "hash_differs";

            File.Move(temporary, target, overwrite: false);
            return "adopted";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "io_failed";
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static LocalMergeComponent ToLocalMerge(StudioCloudAlbumSection component) => new(
        component.Code,
        component.Label,
        component.Order,
        component.PageNumbers,
        component.Status,
        component.OwnerEmail,
        component.SourceKey,
        component.ComponentKind,
        component.SectionKey,
        component.SequenceKey,
        component.Pages.Select(ToLocalMerge).ToList());

    private static LocalMergePage ToLocalMerge(StudioCloudAlbumComponentPage page) => new(
        page.PageNumber,
        page.PageKey,
        page.Title,
        page.SortKey,
        page.SectionKey,
        page.SequenceKey);
}
