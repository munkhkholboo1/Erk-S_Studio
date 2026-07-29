namespace ErkS.Studio;

/// <summary>
/// Expands a pending base source removal to every canonical slice currently
/// owned by that immutable source stream. Components from another owner are
/// never selected even when their mutable source keys are identical.
/// </summary>
internal static class StudioAlbumComponentRemovalPlanner
{
    public static IReadOnlyList<StudioCloudAlbumSection> FindMissingSourceComponents(
        IEnumerable<StudioCloudAlbumSection> currentManifest,
        IEnumerable<string> missingRequestedCodes)
    {
        HashSet<string> requested = (missingRequestedCodes ?? [])
            .Where(code => StudioAlbumComponentIdentity.IsOwnedSourceCode(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0)
            return [];

        return (currentManifest ?? [])
            .Where(component =>
                StudioAlbumComponentIdentity.IsOwnedSourceCode(component.Code) &&
                requested.Any(code =>
                    code.Equals(component.Code, StringComparison.OrdinalIgnoreCase) ||
                    (!StudioAlbumComponentIdentity.TryGetSourceSlice(
                         code,
                         out _,
                         out _) &&
                     StudioAlbumComponentIdentity.BaseSourceCode(code).Equals(
                         StudioAlbumComponentIdentity.BaseSourceCode(component.Code),
                         StringComparison.OrdinalIgnoreCase))))
            .GroupBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
