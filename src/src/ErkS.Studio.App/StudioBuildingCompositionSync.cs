using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class StudioBuildingCompositionConflictException :
    InvalidOperationException
{
    public const string ConflictReasonCode = "building_composition_conflict";

    public StudioBuildingCompositionConflictException(
        IEnumerable<string> conflicts)
        : this(Normalize(conflicts))
    {
    }

    private StudioBuildingCompositionConflictException(
        IReadOnlyList<string> conflicts)
        : base(
            "Building composition Cloud Sync stopped because local and server " +
            "changes overlap: " +
            string.Join(", ", conflicts) +
            $". Refresh the project and resolve these fields explicitly. [reason: {ConflictReasonCode}]")
    {
        Conflicts = conflicts;
    }

    public string ReasonCode => ConflictReasonCode;

    public IReadOnlyList<string> Conflicts { get; }

    private static IReadOnlyList<string> Normalize(
        IEnumerable<string> conflicts) =>
        (conflicts ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>
/// Translates Studio's device-local sheet keys to the portable Cloud ERA
/// identity (immutable source owner + source key + native sheet id). Native
/// RVT/DWG paths and rendered PDFs never enter this contract.
/// </summary>
internal static class StudioBuildingCompositionSync
{
    public static void RecordLocalGroupSet(
        ProjectWorkspace project,
        IEnumerable<ProjectBuildingGroup> nextGroups)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectCloudSyncMetadata.CaptureBuildingCompositionEditBase(project);
        HashSet<string> nextIds = (nextGroups ?? [])
            .Where(group => group is not null)
            .Select(group => group.Id?.Trim() ?? "")
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        project.Cloud.PendingBuildingGroupDeletionIds ??= [];
        foreach (string removedId in (project.BuildingGroups ?? [])
                     .Select(group => group.Id?.Trim() ?? "")
                     .Where(id =>
                         !string.IsNullOrWhiteSpace(id) &&
                         !nextIds.Contains(id)))
        {
            if (!project.Cloud.PendingBuildingGroupDeletionIds.Contains(
                    removedId,
                    StringComparer.OrdinalIgnoreCase))
            {
                project.Cloud.PendingBuildingGroupDeletionIds.Add(removedId);
            }
        }
        project.Cloud.PendingBuildingGroupDeletionIds.RemoveAll(nextIds.Contains);
    }

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

        project.Cloud.PendingBuildingGroupDeletionIds = [];
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
        SheetLibrary library) =>
        CreateUpdate(
            project,
            library,
            project?.Sources ?? []);

    public static StudioCloudBuildingCompositionUpdateRequest CreateUpdate(
        ProjectWorkspace project,
        SheetLibrary library,
        IEnumerable<ProjectDesignSource> locallyAuthoritativeSources)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(library);

        List<ProjectDesignSource> authoritativeSources =
            (locallyAuthoritativeSources ?? [])
            .Where(source => source is not null)
            .ToList();
        List<ProjectBuildingGroup> groups = MergeLocalAndSharedGroups(project);
        HashSet<string> validGroupIds = groups
            .Select(group => group.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> localSourceKeys = authoritativeSources
            .Select(ProjectCloudSyncMetadata.CloudSourceKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> localSourceIdentities = authoritativeSources
            .Select(source => PortableSourceKey(
                ProjectCloudSyncMetadata.CloudOwnerEmail(source),
                ProjectCloudSyncMetadata.CloudSourceKey(source)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Foreign members' assignments remain in the canonical union. For a
        // locally linked source, this device's current sheet list is
        // authoritative so removed sheets do not survive as stale slots.
        var merged = new Dictionary<string, StudioCloudBuildingSheetAssignment>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ProjectCloudBuildingSheetAssignmentReference shared in
                 project.Cloud.SharedBuildingSheetAssignments ?? [])
        {
            bool belongsToLocalSource = string.IsNullOrWhiteSpace(shared.SourceOwnerEmail)
                ? localSourceKeys.Contains(shared.SourceKey)
                : localSourceIdentities.Contains(
                    PortableSourceKey(shared.SourceOwnerEmail, shared.SourceKey));
            if (belongsToLocalSource ||
                !validGroupIds.Contains(shared.BuildingGroupId))
            {
                continue;
            }

            AddAssignment(
                merged,
                shared.SourceOwnerEmail,
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
                !authoritativeSources.Any(source =>
                    RecordBelongsToSource(record, source)) ||
                !TryPortableIdentity(
                    project,
                    record,
                    out string sourceOwnerEmail,
                    out string sourceKey,
                    out string sheetId))
            {
                continue;
            }

            AddAssignment(
                merged,
                sourceOwnerEmail,
                sourceKey,
                sheetId,
                local.Value);
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
                .OrderBy(item => item.SourceOwnerEmail, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SheetId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static bool RecordBelongsToSource(
        SheetRecord record,
        ProjectDesignSource source)
    {
        if (record.SourceId.Equals(
                source.Id,
                StringComparison.OrdinalIgnoreCase) ||
            record.SourceIdentity.Equals(
                source.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return source.UseLegacySheetKeys &&
            string.IsNullOrWhiteSpace(record.SourceId) &&
            !string.IsNullOrWhiteSpace(source.InboxFolder) &&
            ProjectWorkspacePaths.IsInside(
                source.InboxFolder,
                record.ManifestPath);
    }

    private static List<ProjectBuildingGroup> MergeLocalAndSharedGroups(
        ProjectWorkspace project)
    {
        // The whole-state server endpoint is protected by the newest project
        // CAS token. That token alone cannot prove that an older local mirror
        // is allowed to replace a same-ID group, so merge group fields only
        // against the canonical snapshot captured when local editing started.
        List<ProjectBuildingGroup> localGroups =
            ProjectBuildingComposition.NormalizeGroups(project.BuildingGroups);
        List<ProjectBuildingGroup> sharedGroups = NormalizeCloudGroups(
            project.Cloud.SharedBuildingGroups);
        List<ProjectBuildingGroup> editBaseGroups = NormalizeCloudGroups(
            project.Cloud.BuildingCompositionEditBaseGroups);
        bool hasEditBase = project.Cloud.BuildingCompositionEditBaseCaptured;
        if (hasEditBase &&
            project.Cloud.SharedBuildingCompositionVersion <
            project.Cloud.BuildingCompositionEditBaseVersion)
        {
            throw new StudioBuildingCompositionConflictException(
                ["composition:version-regression"]);
        }
        HashSet<string> deletedIds =
            (project.Cloud.PendingBuildingGroupDeletionIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ProjectBuildingGroup> localById = localGroups
            .Where(group => !string.IsNullOrWhiteSpace(group.Id))
            .GroupBy(group => group.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ProjectBuildingGroup> sharedById =
            GroupsById(sharedGroups);
        Dictionary<string, ProjectBuildingGroup> editBaseById =
            GroupsById(editBaseGroups);
        var emittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedIds = new List<string>();
        foreach (ProjectBuildingGroup group in sharedGroups.Concat(localGroups))
        {
            string id = group.Id?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(id) && emittedIds.Add(id))
                orderedIds.Add(id);
        }
        foreach (string id in deletedIds.OrderBy(
                     value => value,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (emittedIds.Add(id))
                orderedIds.Add(id);
        }

        var conflicts = new List<string>();
        var candidates = new List<ProjectBuildingGroup>();
        foreach (string groupId in orderedIds)
        {
            bool hasLocal = localById.TryGetValue(
                groupId,
                out ProjectBuildingGroup? local);
            bool hasShared = sharedById.TryGetValue(
                groupId,
                out ProjectBuildingGroup? shared);
            bool existedAtEditBase = editBaseById.TryGetValue(
                groupId,
                out ProjectBuildingGroup? editBase);

            if (deletedIds.Contains(groupId))
            {
                if (hasShared &&
                    (!hasEditBase ||
                     !existedAtEditBase ||
                     !GroupValuesEqual(shared!, editBase!)))
                {
                    conflicts.Add(groupId + ":delete");
                }
                continue;
            }

            if (hasLocal && hasShared)
            {
                if (!hasEditBase)
                {
                    if (!GroupValuesEqual(local!, shared!))
                        conflicts.Add(groupId + ":unbased");
                    candidates.Add(shared!.Clone());
                    continue;
                }

                if (!existedAtEditBase)
                {
                    if (!GroupValuesEqual(local!, shared!))
                        conflicts.Add(groupId + ":concurrent-add");
                    candidates.Add(shared!.Clone());
                    continue;
                }

                candidates.Add(new ProjectBuildingGroup
                {
                    Id = groupId,
                    Name = MergeField(
                        groupId,
                        "name",
                        editBase!.Name,
                        local!.Name,
                        shared!.Name,
                        StringComparer.Ordinal.Equals,
                        conflicts),
                    Order = MergeField(
                        groupId,
                        "order",
                        editBase.Order,
                        local.Order,
                        shared.Order,
                        static (left, right) => left == right,
                        conflicts),
                });
                continue;
            }

            if (hasShared)
            {
                candidates.Add(shared!.Clone());
                continue;
            }

            if (!hasLocal)
                continue;

            if (hasEditBase && existedAtEditBase)
            {
                if (!GroupValuesEqual(local!, editBase!))
                    conflicts.Add(groupId + ":remote-delete");
                continue;
            }

            candidates.Add(local!.Clone());
        }

        if (conflicts.Count > 0)
            throw new StudioBuildingCompositionConflictException(conflicts);

        return ProjectBuildingComposition.NormalizeGroups(candidates);
    }

    private static List<ProjectBuildingGroup> NormalizeCloudGroups(
        IEnumerable<ProjectCloudBuildingGroupReference>? groups) =>
        ProjectBuildingComposition.NormalizeGroups(
            (groups ?? [])
                .OfType<ProjectCloudBuildingGroupReference>()
                .Select(group => new ProjectBuildingGroup
                {
                    Id = group.Id,
                    Name = group.Name,
                    Order = group.Order,
                }));

    private static Dictionary<string, ProjectBuildingGroup> GroupsById(
        IEnumerable<ProjectBuildingGroup> groups) =>
        groups
            .Where(group => !string.IsNullOrWhiteSpace(group.Id))
            .GroupBy(
                group => group.Id.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

    private static T MergeField<T>(
        string groupId,
        string fieldName,
        T editBase,
        T local,
        T shared,
        Func<T, T, bool> equals,
        ICollection<string> conflicts)
    {
        bool localChanged = !equals(local, editBase);
        bool sharedChanged = !equals(shared, editBase);
        if (localChanged && sharedChanged && !equals(local, shared))
        {
            conflicts.Add(groupId + ":" + fieldName);
            return shared;
        }
        return localChanged ? local : shared;
    }

    private static bool GroupValuesEqual(
        ProjectBuildingGroup left,
        ProjectBuildingGroup right) =>
        left.Name.Equals(right.Name, StringComparison.Ordinal) &&
        left.Order == right.Order;

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
                item => PortableKey(
                    item.SourceOwnerEmail,
                    item.SourceKey,
                    item.SheetId),
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
            if (!TryPortableIdentity(
                    project,
                    record,
                    out string sourceOwnerEmail,
                    out string sourceKey,
                    out string sheetId) ||
                !TryGetAssignment(
                    shared,
                    sourceOwnerEmail,
                    sourceKey,
                    sheetId,
                    out string groupId))
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
        string sourceOwnerEmail = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
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
            item =>
                item.SourceKey.Equals(sourceKey, StringComparison.OrdinalIgnoreCase) &&
                (NormalizeOwner(item.SourceOwnerEmail).Equals(
                     sourceOwnerEmail,
                     StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrWhiteSpace(sourceOwnerEmail) &&
                  string.IsNullOrWhiteSpace(item.SourceOwnerEmail))));
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
                item => PortableKey(
                    item.SourceOwnerEmail,
                    item.SourceKey,
                    item.SheetId),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().BuildingGroupId,
                StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SheetRecord record in library.Snapshot())
        {
            if (TryPortableIdentity(
                    project,
                    record,
                    out string sourceOwnerEmail,
                    out string sourceKey,
                    out string sheetId) &&
                TryGetAssignment(
                    portableAssignments,
                    sourceOwnerEmail,
                    sourceKey,
                    sheetId,
                    out string groupId))
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
                SourceOwnerEmail = NormalizeOwner(item.SourceOwnerEmail),
                SourceKey = item.SourceKey?.Trim() ?? "",
                SheetId = item.SheetId?.Trim() ?? "",
                BuildingGroupId = item.BuildingGroupId?.Trim() ?? "",
            })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.SourceKey) &&
                !string.IsNullOrWhiteSpace(item.SheetId) &&
                validGroupIds.Contains(item.BuildingGroupId))
            .GroupBy(
                item => PortableKey(
                    item.SourceOwnerEmail,
                    item.SourceKey,
                    item.SheetId),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.SourceOwnerEmail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SheetId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryPortableIdentity(
        ProjectWorkspace project,
        SheetRecord record,
        out string sourceOwnerEmail,
        out string sourceKey,
        out string sheetId)
    {
        ProjectDesignSource? source = project.Sources.FirstOrDefault(item =>
            item.Id.Equals(record.SourceId, StringComparison.OrdinalIgnoreCase) ||
            item.Id.Equals(record.SourceIdentity, StringComparison.OrdinalIgnoreCase));
        sourceOwnerEmail = source is null
            ? ""
            : ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        sourceKey = source is null
            ? record.SourceId?.Trim() ?? ""
            : ProjectCloudSyncMetadata.CloudSourceKey(source);
        sheetId = record.Entry.SheetId?.Trim() ?? "";
        return !string.IsNullOrWhiteSpace(sourceKey) &&
            !string.IsNullOrWhiteSpace(sheetId);
    }

    private static void AddAssignment(
        IDictionary<string, StudioCloudBuildingSheetAssignment> assignments,
        string sourceOwnerEmail,
        string sourceKey,
        string sheetId,
        string buildingGroupId)
    {
        string normalizedSourceOwnerEmail = NormalizeOwner(sourceOwnerEmail);
        string normalizedSourceKey = sourceKey?.Trim() ?? "";
        string normalizedSheetId = sheetId?.Trim() ?? "";
        string normalizedGroupId = buildingGroupId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedSourceKey) ||
            string.IsNullOrWhiteSpace(normalizedSheetId) ||
            string.IsNullOrWhiteSpace(normalizedGroupId))
        {
            return;
        }

        assignments[PortableKey(
                normalizedSourceOwnerEmail,
                normalizedSourceKey,
                normalizedSheetId)] =
            new StudioCloudBuildingSheetAssignment
            {
                SourceOwnerEmail = normalizedSourceOwnerEmail,
                SourceKey = normalizedSourceKey,
                SheetId = normalizedSheetId,
                BuildingGroupId = normalizedGroupId,
            };
    }

    private static bool TryGetAssignment(
        IReadOnlyDictionary<string, string> assignments,
        string sourceOwnerEmail,
        string sourceKey,
        string sheetId,
        out string buildingGroupId)
    {
        if (assignments.TryGetValue(
                PortableKey(sourceOwnerEmail, sourceKey, sheetId),
                out string? exactGroupId) &&
            exactGroupId is not null)
        {
            buildingGroupId = exactGroupId;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(sourceOwnerEmail) &&
            assignments.TryGetValue(
                PortableKey("", sourceKey, sheetId),
                out string? legacyGroupId) &&
            legacyGroupId is not null)
        {
            buildingGroupId = legacyGroupId;
            return true;
        }

        buildingGroupId = "";
        return false;
    }

    private static string PortableSourceKey(
        string sourceOwnerEmail,
        string sourceKey) =>
        $"{NormalizeOwner(sourceOwnerEmail)}\u001f{sourceKey?.Trim() ?? ""}";

    private static string PortableKey(
        string sourceOwnerEmail,
        string sourceKey,
        string sheetId) =>
        $"{PortableSourceKey(sourceOwnerEmail, sourceKey)}\u001f{sheetId?.Trim() ?? ""}";

    private static string NormalizeOwner(string? sourceOwnerEmail) =>
        sourceOwnerEmail?.Trim().ToLowerInvariant() ?? "";

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
                item => PortableKey(
                    item.SourceOwnerEmail,
                    item.SourceKey,
                    item.SheetId),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().BuildingGroupId,
                StringComparer.OrdinalIgnoreCase);
        return right.All(item =>
            leftMap.TryGetValue(
                PortableKey(
                    item.SourceOwnerEmail,
                    item.SourceKey,
                    item.SheetId),
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
