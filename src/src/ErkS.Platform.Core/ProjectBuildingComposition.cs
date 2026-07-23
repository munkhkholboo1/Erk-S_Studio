namespace ErkS.Platform.Core;

/// <summary>
/// A Studio-owned building group. Sheets from several native sources can point
/// to the same group and are then composed as one building drawing set.
/// </summary>
public sealed class ProjectBuildingGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>
    /// One-based position after the fixed general-plan pages.
    /// </summary>
    public int Order { get; set; } = 1;

    public ProjectBuildingGroup Clone() => new()
    {
        Id = Id,
        Name = Name,
        Order = Order,
    };
}

public static class ProjectBuildingComposition
{
    public static List<ProjectBuildingGroup> NormalizeGroups(
        IEnumerable<ProjectBuildingGroup>? groups)
    {
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = (groups ?? [])
            .OfType<ProjectBuildingGroup>()
            .Select((group, index) => new
            {
                Group = group,
                Index = index,
                RequestedOrder = group.Order > 0 ? group.Order : int.MaxValue,
            })
            .OrderBy(item => item.RequestedOrder)
            .ThenBy(item => item.Index)
            .ToList();

        var result = new List<ProjectBuildingGroup>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var id = candidate.Group.Id?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(id) || !usedIds.Add(id))
            {
                do
                {
                    id = Guid.NewGuid().ToString("N");
                }
                while (!usedIds.Add(id));
            }

            result.Add(new ProjectBuildingGroup
            {
                Id = id,
                Name = candidate.Group.Name?.Trim() ?? "",
                Order = result.Count + 1,
            });
        }

        for (var index = 0; index < result.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(result[index].Name))
            {
                result[index].Name = $"Барилга {index + 1}";
            }
        }

        return result;
    }

    public static Dictionary<string, string> NormalizeAssignments(
        IReadOnlyDictionary<string, string>? assignments,
        IReadOnlyList<ProjectBuildingGroup> groups)
    {
        var validGroupIds = groups
            .Select(group => group.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (assignments is null)
        {
            return result;
        }

        foreach (var assignment in assignments)
        {
            var sheetKey = assignment.Key?.Trim() ?? "";
            var groupId = assignment.Value?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(sheetKey) &&
                validGroupIds.Contains(groupId))
            {
                result[sheetKey] = groupId;
            }
        }

        return result;
    }

    public static string ResolveAssignedGroupName(
        string sheetKey,
        IReadOnlyList<ProjectBuildingGroup>? groups,
        IReadOnlyDictionary<string, string>? assignments)
    {
        if (string.IsNullOrWhiteSpace(sheetKey) ||
            assignments is null ||
            !assignments.TryGetValue(sheetKey, out var groupId))
        {
            return "";
        }

        return groups?.FirstOrDefault(group =>
                group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase))
            ?.Name ?? "";
    }
}
