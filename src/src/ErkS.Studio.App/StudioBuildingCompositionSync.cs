using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Translates Studio's device-local sheet keys to the portable Cloud ERA
/// identity (source key + native sheet id). Native RVT/DWG paths and rendered
/// PDFs never enter this contract.
/// </summary>
internal static class StudioBuildingCompositionSync
{
    public static bool ApplyCanonical(
        ProjectWorkspace project,
        SheetLibrary library,
        StudioCloudBuildingComposition? canonical,
        bool preserveLocalEdits)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(library);
        if (canonical is null)
            return false;

        List<ProjectBuildingGroup> groups = ProjectBuildingComposition.NormalizeGroups(
            (canonical.Groups ?? [])
                .OfType<StudioCloudBuildingGroup>()
                .Select(group => new ProjectBuildingGroup
                {
                    Id = group.Id,
                    Name = group.Name,
                    Order = group.Order,
                }));
        List<ProjectCloudBuildingSheetAssignmentReference> assignments =
            NormalizeAssignments(canonical.SheetAssignments, groups);

        bool changed =
            project.Cloud.SharedBuildingCompositionVersion != Math.Max(1, canonical.Version) ||
            !GroupsEqual(project.Cloud.SharedBuildingGroups, groups) ||
            !AssignmentsEqual(project.Cloud.SharedBuildingSheetAssignments, assignments);
        project.Cloud.SharedBuildingCompositionVersion = Math.Max(1, canonical.Version);
        project.Cloud.SharedBuildingGroups = groups
            .Select(group => new ProjectCloudBuildingGroupReference
            {
                Id = group.Id,
                Name = group.Name,
                Order = group.Order,
            })
            .ToList();
        project.Cloud.SharedBuildingSheetAssignments = assignments;

        if (preserveLocalEdits)
            return changed;

        Dictionary<string, string> localAssignments =
            MaterializeAssignments(project, library, groups, assignments);
        changed |= !LocalGroupsEqual(project.BuildingGroups, groups) ||
            !DictionaryEqual(project.SheetBuildingAssignments, localAssignments);
        project.BuildingGroups = groups.Select(group => group.Clone()).ToList();
        project.SheetBuildingAssignments = localAssignments;
        return changed;
    }

    public static StudioCloudBuildingCompositionUpdateRequest CreateUpdate(
        ProjectWorkspace project,
        SheetLibrary library)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(library);

        List<ProjectBuildingGroup> groups =
            ProjectBuildingComposition.NormalizeGroups(project.BuildingGroups);
        HashSet<string> validGroupIds = groups
            .Select(group => group.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> localSourceKeys = project.Sources
            .Select(ProjectCloudSyncMetadata.CloudSourceKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Foreign members' assignments remain in the canonical union. For a
        // locally linked source, this device's current sheet list is
        // authoritative so removed sheets do not survive as stale slots.
        var merged = new Dictionary<string, StudioCloudBuildingSheetAssignment>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ProjectCloudBuildingSheetAssignmentReference shared in
                 project.Cloud.SharedBuildingSheetAssignments ?? [])
        {
            if (localSourceKeys.Contains(shared.SourceKey) ||
                !validGroupIds.Contains(shared.BuildingGroupId))
            {
                continue;
            }

            AddAssignment(
                merged,
                shared.SourceKey,
                shared.SheetId,
                shared.BuildingGroupId);
        }

        foreach (KeyValuePair<string, string> local in
                 project.SheetBuildingAssignments ?? new Dictionary<string, string>())
        {
            if (!validGroupIds.Contains(local.Value))
                continue;
            SheetRecord? record = library.FindVerified(local.Key);
            if (record is null ||
                !TryPortableIdentity(project, record, out string sourceKey, out string sheetId))
            {
                continue;
            }

            AddAssignment(merged, sourceKey, sheetId, local.Value);
        }

        return new StudioCloudBuildingCompositionUpdateRequest
        {
            Groups = groups
                .Select(group => new StudioCloudBuildingGroup
                {
                    Id = group.Id,
                    Name = group.Name,
                    Order = group.Order,
                })
                .ToList(),
            SheetAssignments = merged.Values
                .OrderBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SheetId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    public static bool MaterializeSharedAssignments(
        ProjectWorkspace project,
        SheetLibrary library,
        bool onlyUnassigned = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(library);
        List<ProjectBuildingGroup> groups =
            ProjectBuildingComposition.NormalizeGroups(project.BuildingGroups);
        HashSet<string> validGroupIds = groups
            .Select(group => group.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> shared = (project.Cloud.SharedBuildingSheetAssignments ?? [])
            .Where(item =>
                validGroupIds.Contains(item.BuildingGroupId) &&
                !string.IsNullOrWhiteSpace(item.SourceKey) &&
                !string.IsNullOrWhiteSpace(item.SheetId))
            .GroupBy(
                item => PortableKey(item.SourceKey, item.SheetId),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().BuildingGroupId,
                StringComparer.OrdinalIgnoreCase);

        bool changed = false;
        project.SheetBuildingAssignments ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SheetRecord record in library.Snapshot())
        {
            if (onlyUnassigned &&
                project.SheetBuildingAssignments.ContainsKey(record.Key))
            {
                continue;
            }
            if (!TryPortableIdentity(project, record, out string sourceKey, out string sheetId) ||
                !shared.TryGetValue(PortableKey(sourceKey, sheetId), out string? groupId))
            {
                continue;
            }

            if (!project.SheetBuildingAssignments.TryGetValue(record.Key, out string? current) ||
                !current.Equals(groupId, StringComparison.OrdinalIgnoreCase))
            {
                project.SheetBuildingAssignments[record.Key] = groupId;
                changed = true;
            }
        }
        return changed;
    }

    public static bool RemoveSourceAssignments(
        ProjectWorkspace project,
        ProjectDesignSource source,
        IEnumerable<string> localSheetKeys)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
        var keys = (localSheetKeys ?? [])
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (string key in project.SheetBuildingAssignments.Keys.ToList())
        {
            if (keys.Contains(key))
            {
                project.SheetBuildingAssignments.Remove(key);
                changed = true;
            }
        }

        int removedShared = project.Cloud.SharedBuildingSheetAssignments.RemoveAll(
            item => item.SourceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase));
        return changed || removedShared > 0;
    }

    private static Dictionary<string, string> MaterializeAssignments(
        ProjectWorkspace project,
        SheetLibrary library,
        IReadOnlyList<ProjectBuildingGroup> groups,
        IEnumerable<ProjectCloudBuildingSheetAssignmentReference> assignments)
    {
        HashSet<string> validGroupIds = groups
            .Select(group => group.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> portableAssignments = assignments
            .Where(item => validGroupIds.Contains(item.BuildingGroupId))
            .GroupBy(
                item => PortableKey(item.SourceKey, item.SheetId),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().BuildingGroupId,
                StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SheetRecord record in library.Snapshot())
        {
            if (TryPortableIdentity(project, record, out string sourceKey, out string sheetId) &&
                portableAssignments.TryGetValue(
                    PortableKey(sourceKey, sheetId),
                    out string? groupId))
            {
                result[record.Key] = groupId;
            }
        }
        return result;
    }

    private static List<ProjectCloudBuildingSheetAssignmentReference> NormalizeAssignments(
        IEnumerable<StudioCloudBuildingSheetAssignment>? assignments,
        IReadOnlyList<ProjectBuildingGroup> groups)
    {
        HashSet<string> validGroupIds = groups
            .Select(group => group.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (assignments ?? [])
            .OfType<StudioCloudBuildingSheetAssignment>()
            .Select(item => new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceKey = item.SourceKey?.Trim() ?? "",
                SheetId = item.SheetId?.Trim() ?? "",
                BuildingGroupId = item.BuildingGroupId?.Trim() ?? "",
            })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.SourceKey) &&
                !string.IsNullOrWhiteSpace(item.SheetId) &&
                validGroupIds.Contains(item.BuildingGroupId))
            .GroupBy(
                item => PortableKey(item.SourceKey, item.SheetId),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SheetId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryPortableIdentity(
        ProjectWorkspace project,
        SheetRecord record,
        out string sourceKey,
        out string sheetId)
    {
        ProjectDesignSource? source = project.Sources.FirstOrDefault(item =>
            item.Id.Equals(record.SourceId, StringComparison.OrdinalIgnoreCase) ||
            item.Id.Equals(record.SourceIdentity, StringComparison.OrdinalIgnoreCase));
        sourceKey = source is null
            ? record.SourceId?.Trim() ?? ""
            : ProjectCloudSyncMetadata.CloudSourceKey(source);
        sheetId = record.Entry.SheetId?.Trim() ?? "";
        return !string.IsNullOrWhiteSpace(sourceKey) &&
            !string.IsNullOrWhiteSpace(sheetId);
    }

    private static void AddAssignment(
        IDictionary<string, StudioCloudBuildingSheetAssignment> assignments,
        string sourceKey,
        string sheetId,
        string buildingGroupId)
    {
        string normalizedSourceKey = sourceKey?.Trim() ?? "";
        string normalizedSheetId = sheetId?.Trim() ?? "";
        string normalizedGroupId = buildingGroupId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedSourceKey) ||
            string.IsNullOrWhiteSpace(normalizedSheetId) ||
            string.IsNullOrWhiteSpace(normalizedGroupId))
        {
            return;
        }

        assignments[PortableKey(normalizedSourceKey, normalizedSheetId)] =
            new StudioCloudBuildingSheetAssignment
            {
                SourceKey = normalizedSourceKey,
                SheetId = normalizedSheetId,
                BuildingGroupId = normalizedGroupId,
            };
    }

    private static string PortableKey(string sourceKey, string sheetId) =>
        $"{sourceKey.Trim()}\u001f{sheetId.Trim()}";

    private static bool GroupsEqual(
        IReadOnlyList<ProjectCloudBuildingGroupReference>? current,
        IReadOnlyList<ProjectBuildingGroup> canonical) =>
        (current ?? []).Count == canonical.Count &&
        (current ?? []).Zip(canonical).All(pair =>
            pair.First.Id.Equals(pair.Second.Id, StringComparison.OrdinalIgnoreCase) &&
            pair.First.Name.Equals(pair.Second.Name, StringComparison.Ordinal) &&
            pair.First.Order == pair.Second.Order);

    private static bool LocalGroupsEqual(
        IReadOnlyList<ProjectBuildingGroup>? current,
        IReadOnlyList<ProjectBuildingGroup> canonical) =>
        (current ?? []).Count == canonical.Count &&
        (current ?? []).Zip(canonical).All(pair =>
            pair.First.Id.Equals(pair.Second.Id, StringComparison.OrdinalIgnoreCase) &&
            pair.First.Name.Equals(pair.Second.Name, StringComparison.Ordinal) &&
            pair.First.Order == pair.Second.Order);

    private static bool AssignmentsEqual(
        IReadOnlyList<ProjectCloudBuildingSheetAssignmentReference>? left,
        IReadOnlyList<ProjectCloudBuildingSheetAssignmentReference> right)
    {
        if ((left ?? []).Count != right.Count)
            return false;
        Dictionary<string, string> leftMap = (left ?? [])
            .GroupBy(
                item => PortableKey(item.SourceKey, item.SheetId),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().BuildingGroupId,
                StringComparer.OrdinalIgnoreCase);
        return right.All(item =>
            leftMap.TryGetValue(
                PortableKey(item.SourceKey, item.SheetId),
                out string? groupId) &&
            groupId.Equals(item.BuildingGroupId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string> right) =>
        (left ?? new Dictionary<string, string>()).Count == right.Count &&
        right.All(item =>
            left is not null &&
            left.TryGetValue(item.Key, out string? value) &&
            value.Equals(item.Value, StringComparison.OrdinalIgnoreCase));
}
