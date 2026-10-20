using System.IO;

namespace ErkS.Studio;

/// <summary>
/// The last check before a merge: a sync may not remove from the shared album
/// a component this device's own build still draws.
/// </summary>
/// <remarks>
/// 🔴 BATNASAN R12, 2026-10-20: a sync sent «remove» for the Revit source's
/// 53 working sheets while the local build drew all 53, and the shared album
/// fell from 61 pages to 8. Whatever rule asked for it, a removal of rendered
/// work is a defect - the sync stops and says so rather than sending it.
/// </remarks>
internal static class StudioComponentRemovalGuard
{
    public static void Require(
        IEnumerable<StudioAlbumComponentUpload> uploads,
        IReadOnlyList<StudioCloudAlbumSection> rendered)
    {
        HashSet<string> renderedCodes = (rendered ?? [])
            .Select(component => component.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] removed = (uploads ?? [])
            .Where(upload => upload.Remove && renderedCodes.Contains(upload.Code))
            .Select(upload => upload.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (removed.Length > 0)
        {
            throw new InvalidDataException(
                "Sync зогслоо: энэ төхөөрөмжийн альбум зурж буй бүрдлийг Cloud-оос хасах гэж байна - " +
                string.Join(", ", removed) +
                " [reason: removal_of_rendered_component]");
        }
    }
}
