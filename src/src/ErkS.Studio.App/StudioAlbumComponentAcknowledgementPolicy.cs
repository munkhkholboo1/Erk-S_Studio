namespace ErkS.Studio;

internal static class StudioAlbumComponentAcknowledgementPolicy
{
    public static IReadOnlyList<string> ConfirmedPendingCodes(
        IReadOnlyDictionary<string, string> pendingCodeMap,
        IReadOnlyList<StudioCloudAlbumSection> verifiedManifest,
        IReadOnlyList<StudioAlbumComponentUpload> submittedUploads)
    {
        ArgumentNullException.ThrowIfNull(pendingCodeMap);
        ArgumentNullException.ThrowIfNull(verifiedManifest);
        ArgumentNullException.ThrowIfNull(submittedUploads);

        var confirmed = new List<string>();
        foreach (KeyValuePair<string, string> pending in pendingCodeMap)
        {
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
