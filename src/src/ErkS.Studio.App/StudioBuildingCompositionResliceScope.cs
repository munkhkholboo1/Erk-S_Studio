using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Which source components a building-composition edit moves in the CLOUD.
///
/// 🔴 THE BUILDING IS INSIDE THE COMPONENT CODE. A source's pages are uploaded
/// as slices whose code carries the building they belong to:
///
///     source:&lt;owner&gt;:&lt;sourceId&gt;|album-slice|&lt;studio-building:GROUP&gt;.&lt;sequence&gt;
///
/// So reassigning a sheet does not edit a component - it makes the old code
/// wrong and a NEW code correct. MarkBuildingCompositionPending marks only the
/// building sub-covers, which are the pages that carry a group's TITLE; the
/// slices that carry its DRAWINGS were never marked by anything. The result was
/// measured on a real project: the cloud kept
/// «e9cb…|album-slice|&lt;Орон сууц-2&gt;.sections» after the user had moved that
/// source to Орон сууц-1, and the source that really belonged to Орон сууц-2 had
/// no component at all. Locally the correction took effect; in the cloud album -
/// which is what Studio SHOWS while a canonical album exists - it never could,
/// because nothing asked for those slices to be sent again.
///
/// Requesting the BASE code (no slice) is the whole fix, and the machinery for
/// it already existed on both sides: MatchesRequestedComponentCode selects every
/// slice of that base, so the pages go up under whatever building they belong to
/// NOW, and StaleSourceComponents removes the cloud slices of the same base that
/// this build no longer produces. Neither could ever fire, because the base code
/// was never requested.
///
/// ORDER COUNTS AS A MOVE. Reordering the groups leaves every slice code
/// identical and changes only where they sit, so an edit that only reorders
/// still has to re-send the sources - otherwise the cloud album keeps the old
/// sequence with perfectly valid codes.
///
/// A source is skipped when this device has no album page for it. That is not
/// an optimisation: an unrenderable requested code is resolved as a REMOVAL, so
/// marking a source this build cannot draw would ask the cloud to delete pages
/// instead of replacing them.
/// </summary>
internal static class StudioBuildingCompositionResliceScope
{
    public static IReadOnlyList<string> SourceComponentCodes(
        ProjectWorkspace project,
        AlbumDefinition album,
        IEnumerable<ProjectBuildingGroup>? previousGroups,
        IReadOnlyDictionary<string, string>? previousAssignments,
        string fallbackOwnerEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(album);

        bool groupsMoved = !SameGroupSequence(previousGroups, project.BuildingGroups);
        IReadOnlyDictionary<string, string> before =
            previousAssignments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> after =
            project.SheetBuildingAssignments ?? [];
        string fallbackOwner = (fallbackOwnerEmail ?? "").Trim().ToLowerInvariant();

        var codes = new List<string>();
        foreach (ProjectSourceSyncCandidate candidate in
                 ProjectCloudSyncMetadata.SourcePackages(project))
        {
            string prefix = candidate.Source.Id.Trim().ToLowerInvariant() + "|";
            if (string.IsNullOrWhiteSpace(candidate.Source.Id))
                continue;

            string[] sheetKeys = before.Keys
                .Concat(after.Keys)
                .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sheetKeys.Length == 0)
                continue;

            bool assignmentsMoved = sheetKeys.Any(key =>
                !Assigned(before, key).Equals(Assigned(after, key), StringComparison.OrdinalIgnoreCase));
            if (!groupsMoved && !assignmentsMoved)
                continue;

            if (!album.Pages.Any(page =>
                    (page.SheetKey ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string owner = ProjectCloudSyncMetadata.CloudOwnerEmail(candidate.Source);
            if (string.IsNullOrWhiteSpace(owner))
                owner = fallbackOwner;
            if (string.IsNullOrWhiteSpace(owner) ||
                string.IsNullOrWhiteSpace(candidate.SourceKey))
            {
                continue;
            }

            codes.Add(StudioAlbumComponentIdentity.SourceCode(owner, candidate.SourceKey));
        }

        return codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The projects the fix above arrives too late for, and how they recover.
    ///
    /// 🔴 PREVENTING IS NOT ENOUGH, FOR THE SECOND TIME IN THIS FILE'S SUBJECT.
    /// Marking the sources on a composition EDIT keeps new work consistent and
    /// leaves every album already uploaded with the slice it had - and reopening
    /// the dialog does not cure it, because pressing OK on assignments that are
    /// already correct moves nothing and therefore sends nothing. The person
    /// would have to make the composition wrong and right again to shake it
    /// loose, which nobody would ever guess.
    ///
    /// So the cloud manifest is compared against the local truth and the sources
    /// that disagree are named. Two disagreements, both measured on one project:
    /// a component filed under a building none of that source's sheets belong to,
    /// and a source with album pages and no component at all.
    ///
    /// This only MARKS. Nothing leaves the device until the person syncs, so a
    /// wrong answer here costs an unnecessary upload, never a silent write.
    ///
    /// Restricted to this device's own sources, which the edit path is not: this
    /// one runs by itself whenever the album is shown, so a component it can
    /// never be authorised to send would sit pending forever and report the
    /// project as having unauthorised changes on every visit.
    /// </summary>
    public static IReadOnlyList<string> StaleCloudSourceComponentCodes(
        ProjectWorkspace project,
        AlbumDefinition album,
        string currentOwnerEmail)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(album);

        string owner = (currentOwnerEmail ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(owner))
            return [];

        List<ProjectCloudAlbumComponentReference> manifest =
            (project.Cloud.SharedAlbumComponents ?? [])
                .Where(component => !string.IsNullOrWhiteSpace(component.Code))
                .ToList();

        // An album that has never been shared has nothing to disagree with, and
        // reading "no component" as "it is missing" there would mark every
        // source on a project that simply has not been synced yet.
        if (!manifest.Any(component =>
                StudioAlbumComponentIdentity.IsOwnedSourceCode(
                    StudioAlbumComponentIdentity.BaseSourceCode(component.Code.Trim()))))
        {
            return [];
        }

        IReadOnlyDictionary<string, string> assignments =
            project.SheetBuildingAssignments ?? [];
        var codes = new List<string>();
        foreach (ProjectSourceSyncCandidate candidate in
                 ProjectCloudSyncMetadata.SourcePackages(project))
        {
            if (string.IsNullOrWhiteSpace(candidate.Source.Id) ||
                string.IsNullOrWhiteSpace(candidate.SourceKey))
            {
                continue;
            }

            string sourceOwner = ProjectCloudSyncMetadata.CloudOwnerEmail(candidate.Source);
            if (!string.IsNullOrWhiteSpace(sourceOwner) &&
                !sourceOwner.Equals(owner, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string prefix = candidate.Source.Id.Trim().ToLowerInvariant() + "|";
            HashSet<string> groups = assignments
                .Where(entry => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(entry => (entry.Value ?? "").Trim())
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (groups.Count == 0)
                continue;

            if (!album.Pages.Any(page =>
                    (page.SheetKey ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string baseCode = StudioAlbumComponentIdentity.SourceCode(owner, candidate.SourceKey);
            List<string> published = manifest
                .Select(component => component.Code.Trim())
                .Where(code => StudioAlbumComponentIdentity.BaseSourceCode(code)
                    .Equals(baseCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (published.Count == 0)
            {
                codes.Add(baseCode);
                continue;
            }

            bool filedElsewhere = published.Any(code =>
                StudioAlbumComponentIdentity.TryGetBuildingSectionKey(
                    code,
                    out string sectionKey) &&
                !groups.Contains(GroupOf(sectionKey)));
            if (filedElsewhere)
                codes.Add(baseCode);
        }

        return codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GroupOf(string sectionKey)
    {
        string identity = (sectionKey ?? "").Trim();
        int separator = identity.IndexOf(':');
        return separator < 0 ? identity : identity[(separator + 1)..].Trim();
    }

    private static string Assigned(
        IReadOnlyDictionary<string, string> assignments,
        string sheetKey)
    {
        foreach (KeyValuePair<string, string> entry in assignments)
        {
            if (entry.Key.Equals(sheetKey, StringComparison.OrdinalIgnoreCase))
                return (entry.Value ?? "").Trim();
        }
        return "";
    }

    /// <summary>
    /// Whether the groups occupy the same identities in the same order. Names
    /// are deliberately ignored: a renamed group keeps its id, so its slice
    /// codes and their positions are untouched and nothing has to be sent.
    /// </summary>
    private static bool SameGroupSequence(
        IEnumerable<ProjectBuildingGroup>? previous,
        IEnumerable<ProjectBuildingGroup>? current)
    {
        string[] before = GroupSequence(previous);
        string[] after = GroupSequence(current);
        return before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] GroupSequence(IEnumerable<ProjectBuildingGroup>? groups) =>
        (groups ?? [])
            .OrderBy(group => group.Order)
            .ThenBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
}
