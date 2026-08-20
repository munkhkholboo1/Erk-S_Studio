using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioAlbumPageRoleSelectionState(
    bool HasTargets,
    bool IsMixed,
    string? ParticipantId);

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

        List<string?> participantIds = selected
            .Select(target => target.RoleAssignments
                .FirstOrDefault(assignment => AlbumPageRoleCodes.Normalize(
                    assignment.RoleCode).Equals(
                        normalizedRole,
                        StringComparison.OrdinalIgnoreCase))
                ?.ParticipantId)
            .Select(value => string.IsNullOrWhiteSpace(value) ? null : value.Trim())
            .ToList();
        string? first = participantIds[0];
        bool mixed = participantIds.Skip(1).Any(value => !string.Equals(
            value,
            first,
            StringComparison.OrdinalIgnoreCase));
        return new StudioAlbumPageRoleSelectionState(
            true,
            mixed,
            mixed ? null : first);
    }
}
