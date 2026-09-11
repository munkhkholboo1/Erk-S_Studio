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
    /// Whether this account may rewrite the album's CANONICAL generated pages -
    /// the cover, the drawing list, a building's sub-cover, the organisation's
    /// certificate and licence. Studio draws those from project and company
    /// information on any device, so the question is authority rather than
    /// whether a source is present. Without this they were skipped on every
    /// device, and a generated page drawn by an older build could never be
    /// replaced.
    ///
    /// It is NOT the gate on every generated page. The ones Studio draws from a
    /// person's own material are answered by
    /// <see cref="StudioGeneratedComponentAuthority"/> instead.
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

        // Read once. A generated page is judged partly by what ELSE the album
        // carries, and walking a caller's enumerable twice is how a lazy one
        // gives two different answers to the same question.
        List<ProjectCloudAlbumComponentReference> components = (manifest ?? []).ToList();

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectCloudAlbumComponentReference component in components)
        {
            if (string.IsNullOrWhiteSpace(component.Code))
                continue;

            if (!IsSourceComponent(component))
            {
                // A generated page needs no source on this device, only the
                // right to rewrite it - and that right has TWO sources, not one.
                //
                // 🔴 IT USED TO HAVE ONE, AND IT WAS THE ADMINISTRATOR'S. Studio
                // draws some of these pages from a person's own files: the
                // approved planning task they uploaded, the renders they added,
                // the location scheme cut from the general plan they control.
                // Gating those on canonical-metadata authority told the owner of
                // the licence - an architect, not an administrator - that their
                // own 26 renders could not be sent. They named the line
                // themselves: no right over project or company information, full
                // right over their own material.
                StudioGeneratedComponentMaterial material =
                    StudioGeneratedComponentAuthority.Resolve(
                        project,
                        component.Code,
                        currentOwnerEmail,
                        hasOwnedAtd,
                        hasVisualizations);
                // The same owner test the source branch below applies, for the
                // same reason: a component the cloud has already filed under
                // somebody else is not this account's to redraw.
                bool ownMaterial = material.IsHeldLocally &&
                    OwnerMatches(
                        component.OwnerEmail?.Trim().ToLowerInvariant() ?? "",
                        fallbackOwner) &&
                    !SomebodyElseAlreadyOwnsThisMaterial(
                        components,
                        material.SourceKey,
                        fallbackOwner);
                // The caller still trims the ones with their own owner test,
                // such as the location scheme.
                if (canManageCanonicalMetadata || ownMaterial)
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

    /// <summary>
    /// Whether the album already carries this same material under SOMEBODY
    /// ELSE'S name.
    ///
    /// 🔴 THE ONE THING THIS CHANGE WOULD OTHERWISE HAVE OPENED. A generated code
    /// is not keyed by owner - «generated:visualizations» is one component for the
    /// whole album - so a page an older build filed there carries no name, and the
    /// owner test above lets anybody past an empty name. Widening that from
    /// administrators to every contributor holding renders of their own would let
    /// the second contributor replace the first one's pages, which then leave the
    /// shared album entirely when the legacy code is retired.
    ///
    /// Their local files are never touched, and they get their pages back by
    /// rendering again - but they would not have been asked. So the moment the
    /// album shows the material under a name that is not this account's, the
    /// unnamed one is left alone. An administrator may still take it.
    /// </summary>
    private static bool SomebodyElseAlreadyOwnsThisMaterial(
        IEnumerable<ProjectCloudAlbumComponentReference> manifest,
        string sourceKey,
        string currentOwnerEmail) =>
        (manifest ?? []).Any(other =>
            (other.SourceKey?.Trim() ?? "").Equals(
                sourceKey,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(other.OwnerEmail) &&
            !other.OwnerEmail.Trim().Equals(
                currentOwnerEmail,
                StringComparison.OrdinalIgnoreCase));

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
