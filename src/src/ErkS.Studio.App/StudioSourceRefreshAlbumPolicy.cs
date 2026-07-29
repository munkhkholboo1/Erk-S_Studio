namespace ErkS.Studio;

internal enum StudioSourceRefreshAlbumDisposition
{
    Rebuild,
    DeferUntilCloudSync,
}

internal sealed record StudioSourceRefreshAlbumResolution(
    StudioSourceRefreshAlbumDisposition Disposition,
    IReadOnlyList<string> UnavailableCloudSheetKeys,
    string ReasonCode)
{
    public bool ShouldDefer =>
        Disposition == StudioSourceRefreshAlbumDisposition.DeferUntilCloudSync;
}

/// <summary>
/// Distinguishes a genuine local album build failure from the expected case
/// where this device has only its own source packages and needs the canonical
/// Cloud PDF to retain every other participant's component.
/// </summary>
internal static class StudioSourceRefreshAlbumPolicy
{
    public static StudioSourceRefreshAlbumResolution Resolve(
        StudioWorkspaceOperation operation,
        bool isCloudLinked,
        bool hasCachedCanonicalAlbum,
        IEnumerable<string> albumSheetKeys,
        IEnumerable<string> verifiedSheetKeys,
        IEnumerable<string> authorizedLocalSourceIds,
        IEnumerable<string> knownCloudSourceIdentities,
        IEnumerable<string> buildIssues)
    {
        bool partialLocalRebuild = operation is
            StudioWorkspaceOperation.SourceRefresh or
            StudioWorkspaceOperation.LocalPdfPageEdit;
        if (!partialLocalRebuild ||
            !isCloudLinked)
        {
            return Rebuild();
        }

        // The caller reaches this policy only after the cached canonical
        // component merge has already failed. A PDF file can still be present
        // while its legacy/incomplete manifest is unusable, so cache presence
        // alone must not turn proven cloud-only gaps into a local build error.
        _ = hasCachedCanonicalAlbum;

        HashSet<string> verified = Normalize(verifiedSheetKeys);
        string[] missing = Normalize(albumSheetKeys)
            .Where(key => !verified.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length == 0)
            return Rebuild();

        HashSet<string> localSourceIds = Normalize(authorizedLocalSourceIds);
        HashSet<string> cloudSourceIdentities = Normalize(knownCloudSourceIdentities);
        foreach (string key in missing)
        {
            string sourceIdentity = SourceIdentity(key);
            if (sourceIdentity.Length == 0 ||
                localSourceIds.Contains(sourceIdentity) ||
                !cloudSourceIdentities.Contains(sourceIdentity))
            {
                // Unknown and locally owned missing keys are not safe to hide.
                return Rebuild();
            }
        }

        HashSet<string> expectedMissingIssues = missing
            .Select(key => $"Album sheet '{key}' is missing or unverified.")
            .ToHashSet(StringComparer.Ordinal);
        string[] issues = (buildIssues ?? [])
            .Where(issue => !string.IsNullOrWhiteSpace(issue))
            .Select(issue => issue.Trim())
            .ToArray();
        if (issues.Length == 0 ||
            issues.Any(issue => !expectedMissingIssues.Contains(issue)))
        {
            // A package/hash/PDF/rendering failure must remain visible.
            return Rebuild();
        }

        return new StudioSourceRefreshAlbumResolution(
            StudioSourceRefreshAlbumDisposition.DeferUntilCloudSync,
            missing,
            operation == StudioWorkspaceOperation.LocalPdfPageEdit
                ? "pdf_page_edit_cloud_album_deferred"
                : "source_refresh_cloud_album_deferred");
    }

    private static StudioSourceRefreshAlbumResolution Rebuild() =>
        new(
            StudioSourceRefreshAlbumDisposition.Rebuild,
            Array.Empty<string>(),
            "");

    private static HashSet<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string SourceIdentity(string sheetKey)
    {
        int separator = sheetKey.IndexOf('|');
        return separator <= 0
            ? ""
            : sheetKey[..separator].Trim();
    }
}
