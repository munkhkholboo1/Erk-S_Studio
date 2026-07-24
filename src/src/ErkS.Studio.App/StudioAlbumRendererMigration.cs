using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Selects only Cloud album components that this device can reproduce from
/// local source proxies. Renderer upgrades must never replace or remove a
/// collaborator's component when its source is unavailable on this device.
/// </summary>
internal static class StudioAlbumRendererMigration
{
    public const int CurrentRevision = 4;

    public static IReadOnlyList<string> SelectLocallyRenderableComponents(
        ProjectWorkspace project,
        IEnumerable<ProjectCloudAlbumComponentReference> manifest,
        string currentOwnerEmail,
        bool hasOwnedAtd,
        bool hasVisualizations)
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
            if (string.IsNullOrWhiteSpace(component.Code) ||
                !IsSourceComponent(component))
            {
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
