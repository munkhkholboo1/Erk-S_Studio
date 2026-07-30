namespace ErkS.Studio;

internal static class StudioAlbumComponentAcknowledgementPolicy
{
    public static StudioMissingAlbumComponentResolution ResolveMissingComponents(
        IReadOnlyList<string> missingCodes,
        Func<string, bool> mustRemainPending)
    {
        ArgumentNullException.ThrowIfNull(missingCodes);
        ArgumentNullException.ThrowIfNull(mustRemainPending);

        string[] deferred = missingCodes
            .Where(mustRemainPending)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HashSet<string> deferredSet =
            deferred.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] removals = missingCodes
            .Where(code => !deferredSet.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new StudioMissingAlbumComponentResolution(
            removals,
            deferred);
    }

    public static IReadOnlyList<string> ConfirmedPendingCodes(
        IReadOnlyDictionary<string, string> pendingCodeMap,
        IReadOnlyList<StudioCloudAlbumSection> verifiedManifest,
        IReadOnlyList<StudioAlbumComponentUpload> submittedUploads,
        IReadOnlyCollection<string>? deferredCodes = null)
    {
        ArgumentNullException.ThrowIfNull(pendingCodeMap);
        ArgumentNullException.ThrowIfNull(verifiedManifest);
        ArgumentNullException.ThrowIfNull(submittedUploads);

        HashSet<string> deferred = deferredCodes?
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
        var confirmed = new List<string>();
        foreach (KeyValuePair<string, string> pending in pendingCodeMap)
        {
            if (deferred.Contains(pending.Value))
                continue;

            bool present = verifiedManifest.Any(component =>
                MatchesRequestedComponent(component, pending.Value));
            bool removalSubmitted = submittedUploads.Any(upload =>
                upload.Remove &&
                upload.Code.Equals(
                    pending.Value,
                    StringComparison.OrdinalIgnoreCase));
            if (removalSubmitted ? !present : present)
                confirmed.Add(pending.Key);
        }
        return confirmed;
    }

    private static bool MatchesRequestedComponent(
        StudioCloudAlbumSection component,
        string requestedCode)
    {
        string requested = (requestedCode ?? "").Trim();
        if (component.Code.Equals(
                requested,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!StudioAlbumComponentIdentity.IsSourceComponent(component) ||
            !StudioAlbumComponentIdentity.IsOwnedSourceCode(requested) ||
            StudioAlbumComponentIdentity.TryGetSourceSlice(
                requested,
                out _,
                out _))
        {
            return false;
        }

        return StudioAlbumComponentIdentity.BaseSourceCode(component.Code)
            .Equals(
                StudioAlbumComponentIdentity.BaseSourceCode(requested),
                StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record StudioMissingAlbumComponentResolution(
    IReadOnlyList<string> RemovalCodes,
    IReadOnlyList<string> DeferredCodes);
