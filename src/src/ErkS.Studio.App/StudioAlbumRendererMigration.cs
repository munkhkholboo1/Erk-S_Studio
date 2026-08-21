using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Selects only Cloud album components that this device can reproduce from
/// local source proxies. Renderer upgrades must never replace or remove a
/// collaborator's component when its source is unavailable on this device.
/// </summary>
internal static class StudioAlbumRendererMigration
{
    /// <summary>
    /// Raised whenever a renderer change makes the pages already in a canonical
    /// album wrong. A device whose album is behind re-renders the components it
    /// owns and merges them, which is the only way a page composed by an older
    /// build leaves the shared album.
    ///
    /// 5: the general plan no longer carries a second, concept-geometry corner
    /// table, an A4 table of contents in the middle of the set, or its sheets in
    /// template-slot order instead of the order they arrive from AutoCAD.
    /// </summary>
    public const int CurrentRevision = 5;

    /// <param name="canManageCanonicalMetadata">
    /// Whether this account may rewrite the album's generated pages - the cover,
    /// the drawing list, the location scheme. Studio draws those from project
    /// data on any device, so the question is authority rather than whether the
    /// source is present. Without this they were skipped on every device, and a
    /// generated page drawn by an older build could never be replaced.
    /// </param>
    public static IReadOnlyList<string> SelectLocallyRenderableComponents(
        ProjectWorkspace project,
        IEnumerable<ProjectCloudAlbumComponentReference> manifest,
        string currentOwnerEmail,
        bool hasOwnedAtd,
        bool hasVisualizations,
        bool canManageCanonicalMetadata = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        string fallbackOwner = (currentOwnerEmail ?? "").Trim().ToLowerInvariant();
        var localSources = ProjectCloudSyncMetadata.SourcePackages(project)
            .Select(candidate => new LocalSourceIdentity(
                candidate.SourceKey,
                FirstNonEmpty(
                    ProjectCloudSyncMetadata.CloudOwnerEmail(candidate.Source),
                    fallbackOwner)))
            .Where(identity =>
                !string.IsNullOrWhiteSpace(identity.SourceKey) &&
                !string.IsNullOrWhiteSpace(identity.OwnerEmail))
            .ToList();

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectCloudAlbumComponentReference component in manifest ?? [])
        {
            if (string.IsNullOrWhiteSpace(component.Code))
                continue;

            if (!IsSourceComponent(component))
            {
                // A generated page needs no source on this device, only the
                // right to rewrite it. The caller still trims the ones with
                // their own owner test, such as the location scheme.
                if (canManageCanonicalMetadata)
                    selected.Add(component.Code.Trim());
                continue;
            }

            string sourceKey = component.SourceKey?.Trim() ?? "";
            string ownerEmail = component.OwnerEmail?.Trim().ToLowerInvariant() ?? "";
            bool localSource = localSources.Any(local =>
                local.SourceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(ownerEmail) ||
                 local.OwnerEmail.Equals(ownerEmail, StringComparison.OrdinalIgnoreCase)));
            bool localAtd = hasOwnedAtd &&
                sourceKey.Equals(
                    StudioAlbumComponentIdentity.AtdSourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                OwnerMatches(ownerEmail, fallbackOwner);
            bool localVisualization = hasVisualizations &&
                sourceKey.Equals(
                    StudioAlbumComponentIdentity.VisualizationSourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                OwnerMatches(ownerEmail, fallbackOwner);
            if (localSource || localAtd || localVisualization)
                selected.Add(component.Code.Trim());
        }

        return selected.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsSourceComponent(ProjectCloudAlbumComponentReference component) =>
        component.ComponentKind.Equals(
            StudioAlbumComponentIdentity.SourceComponentKind,
            StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(component.SourceKey);

    private static bool OwnerMatches(string componentOwner, string currentOwner) =>
        !string.IsNullOrWhiteSpace(currentOwner) &&
        (string.IsNullOrWhiteSpace(componentOwner) ||
         componentOwner.Equals(currentOwner, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim().ToLowerInvariant() ?? "";

    private sealed record LocalSourceIdentity(string SourceKey, string OwnerEmail);
}
