using ErkS.Platform.Core;

namespace ErkS.Studio;

internal static class StudioCanonicalAlbumComponentSlotPolicy
{
    private const int OpenEndStep = 100;

    /// <summary>
    /// Gives each component the shared album does not have yet an order between
    /// the components it stands between in this device's build - the build is
    /// the album in print order.
    /// </summary>
    /// <remarks>
    /// 🔴 BATNASAN, 2026-10-20: the shared album's components are numbered
    /// 0, 100, 200 once the server reflows them, while a new component got the
    /// policy's absolute order (5 000 for the 01, 800 000 for a source) - so the
    /// Revit 01, which prints second, came after the general plan's 200. A new
    /// component's place is read off its neighbours, not off a scale the shared
    /// album no longer uses.
    /// </remarks>
    /// <param name="localOrder">Every rendered component, in page order.</param>
    /// <param name="isExisting">Whether the shared album already holds it (its order stands).</param>
    public static void PlaceNewComponents(
        IReadOnlyList<StudioCloudAlbumSection> localOrder,
        Func<StudioCloudAlbumSection, bool> isExisting)
    {
        ArgumentNullException.ThrowIfNull(localOrder);
        ArgumentNullException.ThrowIfNull(isExisting);
        if (!localOrder.Any(isExisting))
            return;

        int index = 0;
        while (index < localOrder.Count)
        {
            if (isExisting(localOrder[index]))
            {
                index++;
                continue;
            }

            int start = index;
            while (index < localOrder.Count && !isExisting(localOrder[index]))
                index++;
            int count = index - start;
            int? previous = start > 0 ? localOrder[start - 1].Order : null;
            int? next = index < localOrder.Count ? localOrder[index].Order : null;
            for (int offset = 0; offset < count; offset++)
            {
                localOrder[start + offset].Order = (previous, next) switch
                {
                    (int p, int q) when (q - p) / (count + 1) >= 1 => p + (q - p) / (count + 1) * (offset + 1),
                    (int p, int) => p,
                    (int p, null) => p + OpenEndStep * (offset + 1),
                    (null, int q) => q - OpenEndStep * (count - offset),
                    _ => localOrder[start + offset].Order,
                };
            }
        }
    }

    public static int ResolveOrder(
        ProjectWorkspace project,
        StudioCloudAlbumSection rendered,
        IEnumerable<StudioCloudAlbumSection> serverManifest)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(rendered);
        StudioCloudAlbumSection? existing = (serverManifest ?? [])
            .Where(component => component is not null)
            .OrderBy(component =>
                (component.PageNumbers ?? [])
                    .DefaultIfEmpty(int.MaxValue)
                    .Min())
            .ThenBy(component => component.Order)
            .FirstOrDefault(component =>
                SameCanonicalCode(project, rendered, component) ||
                SameOwnedSlice(rendered, component));
        return existing?.Order ?? rendered.Order;
    }

    private static bool SameCanonicalCode(
        ProjectWorkspace project,
        StudioCloudAlbumSection left,
        StudioCloudAlbumSection right)
    {
        string leftCode = StudioAlbumComponentIdentity.CanonicalComponentCode(
            project,
            left.Code);
        string rightCode = StudioAlbumComponentIdentity.CanonicalComponentCode(
            project,
            right.Code);
        return !string.IsNullOrWhiteSpace(leftCode) &&
            leftCode.Equals(rightCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameOwnedSlice(
        StudioCloudAlbumSection left,
        StudioCloudAlbumSection right)
    {
        if (string.IsNullOrWhiteSpace(left.OwnerEmail) ||
            string.IsNullOrWhiteSpace(left.SourceKey) ||
            !left.OwnerEmail.Equals(
                right.OwnerEmail,
                StringComparison.OrdinalIgnoreCase) ||
            !left.SourceKey.Equals(
                right.SourceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        (string leftSection, string leftSequence) = SemanticKeys(left);
        (string rightSection, string rightSequence) = SemanticKeys(right);
        return leftSection.Equals(
                   rightSection,
                   StringComparison.OrdinalIgnoreCase) &&
               leftSequence.Equals(
                   rightSequence,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static (string SectionKey, string SequenceKey) SemanticKeys(
        StudioCloudAlbumSection component)
    {
        string sectionKey = (component.SectionKey ?? "").Trim();
        string sequenceKey = (component.SequenceKey ?? "").Trim();
        if (!StudioAlbumComponentIdentity.TryGetSourceSlice(
                component.Code,
                out string decodedSectionKey,
                out string decodedSequenceKey))
        {
            return (sectionKey, sequenceKey);
        }

        return (
            string.IsNullOrWhiteSpace(sectionKey)
                ? decodedSectionKey.Trim()
                : sectionKey,
            string.IsNullOrWhiteSpace(sequenceKey)
                ? decodedSequenceKey.Trim()
                : sequenceKey);
    }
}
