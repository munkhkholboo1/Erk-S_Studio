using ErkS.Platform.Core;

namespace ErkS.Studio;

internal static class StudioCanonicalAlbumComponentSlotPolicy
{
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
