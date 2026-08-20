using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioAlbumSectionGroup(
    string Key,
    string Title,
    IReadOnlyList<AlbumCompositionItem> Components);

internal static class StudioAlbumSectionGrouping
{
    public static IReadOnlyList<StudioAlbumSectionGroup> ResolvePopulatedSourceSlots(
        AlbumDefinition album,
        IEnumerable<string?> populatedSlotIds)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(populatedSlotIds);
        var populatedIds = populatedSlotIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<AlbumCompositionItem> sourceSlots = (album.Composition ?? [])
            .Where(item =>
                item.Kind == AlbumCompositionKind.SourceSlot &&
                populatedIds.Contains(item.Id))
            .OrderBy(item => item.Order)
            .ToList();
        var groups = new List<StudioAlbumSectionGroup>();
        var assigned = new HashSet<AlbumCompositionItem>(ReferenceEqualityComparer.Instance);
        foreach (AlbumSection section in album.Sections ?? [])
        {
            List<AlbumCompositionItem> components = sourceSlots
                .Where(item => item.SectionTitle.Equals(
                    section.Title,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (components.Count == 0)
                continue;
            foreach (AlbumCompositionItem component in components)
                assigned.Add(component);
            groups.Add(new StudioAlbumSectionGroup(
                $"section:{section.Id:N}",
                section.Title,
                components));
        }

        foreach (IGrouping<string, AlbumCompositionItem> remaining in sourceSlots
                     .Where(item => !assigned.Contains(item))
                     .GroupBy(
                         item => string.IsNullOrWhiteSpace(item.SectionTitle)
                             ? "Бүлэггүй"
                             : item.SectionTitle,
                         StringComparer.OrdinalIgnoreCase))
        {
            groups.Add(new StudioAlbumSectionGroup(
                "section:" + remaining.Key,
                remaining.Key,
                remaining.ToList()));
        }
        return groups;
    }
}
