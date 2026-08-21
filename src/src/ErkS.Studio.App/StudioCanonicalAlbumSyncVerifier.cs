using System.IO;

namespace ErkS.Studio;

/// <summary>
/// Defines the point at which Studio may truthfully report a completed album
/// sync. The server's current pointer, PDF hash and complete component manifest
/// must all describe the exact revision acknowledged by the write operation.
/// </summary>
internal static class StudioCanonicalAlbumSyncVerifier
{
    public static StudioCloudAlbumRevision Verify(
        IReadOnlyList<StudioCloudAlbum> albums,
        string albumId,
        string expectedRevisionId,
        string expectedPdfSha256)
    {
        string expectedAlbum = (albumId ?? "").Trim();
        string expectedRevision = (expectedRevisionId ?? "").Trim();
        string expectedHash = NormalizeHash(expectedPdfSha256);
        if (string.IsNullOrWhiteSpace(expectedAlbum) ||
            string.IsNullOrWhiteSpace(expectedRevision) ||
            string.IsNullOrWhiteSpace(expectedHash))
        {
            // Naming the missing value matters: each one fails for a different
            // reason. No album id means the album was never resolved; no
            // revision or hash means the sync wrote nothing and had nothing to
            // acknowledge, which a sync with no work to do can reach.
            string[] missing =
            [
                .. string.IsNullOrWhiteSpace(expectedAlbum) ? new[] { "album id" } : [],
                .. string.IsNullOrWhiteSpace(expectedRevision) ? new[] { "revision id" } : [],
                .. string.IsNullOrWhiteSpace(expectedHash) ? new[] { "PDF hash" } : [],
            ];
            throw new InvalidDataException(
                "Canonical album acknowledgement is incomplete: " +
                string.Join(", ", missing) +
                $" missing (albums returned: {(albums ?? []).Count}).");
        }

        StudioCloudAlbum album = (albums ?? [])
            .FirstOrDefault(item => item.AlbumId.Equals(
                expectedAlbum,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "Canonical album was not returned by the server after sync.");
        if (!album.CurrentRevisionId.Equals(
                expectedRevision,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Canonical album advanced to a different revision before sync verification.");
        }

        StudioCloudAlbumRevision revision = album.Revisions
            .SingleOrDefault(item => item.RevisionId.Equals(
                album.CurrentRevisionId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "Canonical album current revision metadata is missing or duplicated.");
        if (!NormalizeHash(revision.PdfSha256).Equals(
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Canonical album PDF hash does not match the acknowledged sync result.");
        }
        if (revision.PageCount < 1 ||
            !StudioAlbumComponentIdentity.IsMergeReady(
                revision.SectionManifest ?? [],
                revision.PageCount))
        {
            throw new InvalidDataException(
                "Canonical album component manifest is incomplete; sync was not committed.");
        }

        return revision;
    }

    private static string NormalizeHash(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
