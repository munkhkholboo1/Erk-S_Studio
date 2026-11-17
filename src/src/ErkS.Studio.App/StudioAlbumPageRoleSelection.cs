using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioAlbumPageRoleSelectionState(
    bool HasTargets,
    bool IsMixed,
    string? ParticipantId,
    string? ManualName = null);

internal static class StudioAlbumPageRoleSelection
{
    public static StudioAlbumPageRoleSelectionState Resolve(
        IEnumerable<IAlbumPageRoleOwner?> targets,
        string roleCode)
    {
        ArgumentNullException.ThrowIfNull(targets);
        string normalizedRole = AlbumPageRoleCodes.Normalize(roleCode);
        if (string.IsNullOrWhiteSpace(normalizedRole))
            throw new ArgumentException("Unsupported album page role.", nameof(roleCode));

        var selected = new List<IAlbumPageRoleOwner>();
        var seen = new HashSet<IAlbumPageRoleOwner>(ReferenceEqualityComparer.Instance);
        foreach (IAlbumPageRoleOwner target in targets.OfType<IAlbumPageRoleOwner>())
        {
            if (seen.Add(target))
                selected.Add(target);
        }
        if (selected.Count == 0)
            return new StudioAlbumPageRoleSelectionState(false, false, null);

        // A member is keyed by id; a hand-written name (no id) by its text, so
        // two pages signed by the same unregistered person read as one value.
        List<(string? Id, string? Manual)> values = selected
            .Select(target => target.RoleAssignments
                .FirstOrDefault(assignment => AlbumPageRoleCodes.Normalize(
                    assignment.RoleCode).Equals(
                        normalizedRole,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(assignment =>
            {
                string? id = string.IsNullOrWhiteSpace(assignment?.ParticipantId)
                    ? null
                    : assignment!.ParticipantId.Trim();
                string? manual = id is null && !string.IsNullOrWhiteSpace(assignment?.FullName)
                    ? assignment!.FullName.Trim()
                    : null;
                return (id, manual);
            })
            .ToList();
        (string? firstId, string? firstManual) = values[0];
        bool mixed = values.Skip(1).Any(value =>
            !string.Equals(value.Id, firstId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(value.Manual, firstManual, StringComparison.Ordinal));
        return new StudioAlbumPageRoleSelectionState(
            true,
            mixed,
            mixed ? null : firstId,
            mixed ? null : firstManual);
    }
}
