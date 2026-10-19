using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// The album components that carry a working-drawing album's front matter -
/// the drawing list, the explanatory note, the composition and the ТЭЗҮ - and
/// the rule that sends them to the shared album with every change.
/// </summary>
/// <remarks>
/// 🔴 BATNASAN, 2026-10-20: the general plan was renumbered (01-04 → 02-05);
/// the corner tables, restamped from the local numbering, said 02-05, while
/// the drawing list, a component the Cloud already held, still said 01-04 -
/// nothing marked it, because nothing in it had been "edited". The list is
/// drawn from every page of the album, so any change that reaches the shared
/// album can change it; and an edit of the note itself must reach it even
/// when nothing else changed.
///
/// The carriers are Studio's own list page (<see cref="GeneratedCode"/>) and,
/// once Revit reserves the frames in its 01, the source component holding
/// that 01 (its sequence key is <see cref="WorkingDrawingFrontMatterSlot.SlotId"/>).
/// </remarks>
internal static class StudioFrontMatterComponentScope
{
    public static readonly string GeneratedCode =
        $"generated:{WorkingDrawingFrontMatterSlot.SlotId}:{AlbumGeneratedPageKind.None}";

    public static bool IsGeneratedCarrier(StudioCloudAlbumSection component) =>
        component.Code.Equals(GeneratedCode, StringComparison.OrdinalIgnoreCase);

    public static bool IsSourceCarrier(StudioCloudAlbumSection component) =>
        !component.Code.StartsWith("generated:", StringComparison.OrdinalIgnoreCase) &&
        component.SequenceKey.Trim().Equals(WorkingDrawingFrontMatterSlot.SlotId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The selection with the front-matter carriers added: for a working-drawing
    /// album, whenever anything else goes to the shared album or the front
    /// matter itself was edited. A source carrier is added only when this
    /// account owns it; the generated list only when it may rewrite the
    /// album's canonical pages.
    /// </summary>
    public static List<StudioCloudAlbumSection> IncludeCarriers(
        AlbumDefinition album,
        IReadOnlyList<StudioCloudAlbumSection> rendered,
        IReadOnlyList<StudioCloudAlbumSection> selected,
        string ownerEmail,
        bool canManageCanonicalMetadata,
        bool frontMatterRequested)
    {
        ArgumentNullException.ThrowIfNull(album);
        var result = (selected ?? []).ToList();
        if (!string.Equals(album.TemplateId, BuildingWorkingDrawingAlbumTemplate.TemplateId, StringComparison.OrdinalIgnoreCase) ||
            (result.Count == 0 && !frontMatterRequested))
        {
            return result;
        }

        string owner = (ownerEmail ?? "").Trim();
        foreach (StudioCloudAlbumSection component in rendered ?? [])
        {
            bool carrier =
                (IsGeneratedCarrier(component) && canManageCanonicalMetadata) ||
                (IsSourceCarrier(component) &&
                 (string.IsNullOrWhiteSpace(component.OwnerEmail) ||
                  component.OwnerEmail.Trim().Equals(owner, StringComparison.OrdinalIgnoreCase)));
            if (carrier && result.All(item => !item.Code.Equals(component.Code, StringComparison.OrdinalIgnoreCase)))
                result.Add(component);
        }

        return result;
    }
}
