namespace ErkS.Studio;

internal static class StudioAlbumRevisionAcknowledgementPolicy
{
    public static string SourceUploadSha256(
        StudioCloudAlbumRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        string sourceHash = Normalize(revision.SourceUploadSha256);
        return string.IsNullOrWhiteSpace(sourceHash)
            ? Normalize(revision.PdfSha256)
            : sourceHash;
    }

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
