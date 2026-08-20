namespace ErkS.Platform.Core;

public static class AlbumPageRoleCodes
{
    public const string Architect = "Architect";
    public const string PreparedBy = "PreparedBy";
    public const string CheckedBy = "CheckedBy";

    public static IReadOnlyList<string> All { get; } =
        [Architect, PreparedBy, CheckedBy];

    public static string Normalize(string? roleCode)
    {
        string normalized = new((roleCode ?? "")
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return All.FirstOrDefault(role => role.Equals(
                   normalized,
                   StringComparison.OrdinalIgnoreCase))
               ?? "";
    }
}

public interface IAlbumPageRoleOwner
{
    List<AlbumPageRoleAssignment> RoleAssignments { get; set; }
}

public sealed class AlbumPageRoleAssignment
{
    public string RoleCode { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";

    public AlbumPageRoleAssignment Clone() => new()
    {
        RoleCode = RoleCode,
        ParticipantId = ParticipantId,
        FamilyName = FamilyName,
        GivenName = GivenName,
        FullName = FullName,
        Email = Email,
    };
}

public static class AlbumPageRoleAssignmentService
{
    public static int Apply(
        IEnumerable<IAlbumPageRoleOwner> targets,
        string roleCode,
        ProjectMember? member)
    {
        ArgumentNullException.ThrowIfNull(targets);
        string normalizedRole = AlbumPageRoleCodes.Normalize(roleCode);
        if (string.IsNullOrWhiteSpace(normalizedRole))
            throw new ArgumentException("Unsupported album page role.", nameof(roleCode));

        int changed = 0;
        var visited = new HashSet<IAlbumPageRoleOwner>(ReferenceEqualityComparer.Instance);
        foreach (IAlbumPageRoleOwner target in targets)
        {
            if (target is null || !visited.Add(target))
                continue;

            target.RoleAssignments ??= [];
            List<AlbumPageRoleAssignment> existing = target.RoleAssignments
                .Where(item => AlbumPageRoleCodes.Normalize(item.RoleCode).Equals(
                    normalizedRole,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (member is null)
            {
                if (existing.Count == 0)
                    continue;
                foreach (AlbumPageRoleAssignment assignment in existing)
                    target.RoleAssignments.Remove(assignment);
                changed++;
                continue;
            }

            var replacement = new AlbumPageRoleAssignment
            {
                RoleCode = normalizedRole,
                ParticipantId = member.Id?.Trim() ?? "",
                FamilyName = member.FamilyName?.Trim() ?? "",
                GivenName = member.GivenName?.Trim() ?? "",
                FullName = member.FullName?.Trim() ?? "",
                Email = member.Email?.Trim() ?? "",
            };
            if (existing.Count == 1 && Equivalent(existing[0], replacement))
                continue;

            foreach (AlbumPageRoleAssignment assignment in existing)
                target.RoleAssignments.Remove(assignment);
            target.RoleAssignments.Add(replacement);
            changed++;
        }
        return changed;
    }

    public static void Normalize(IAlbumPageRoleOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.RoleAssignments ??= [];
        List<AlbumPageRoleAssignment> normalized = owner.RoleAssignments
            .Where(item => item is not null)
            .Select(item => new AlbumPageRoleAssignment
            {
                RoleCode = AlbumPageRoleCodes.Normalize(item.RoleCode),
                ParticipantId = item.ParticipantId?.Trim() ?? "",
                FamilyName = item.FamilyName?.Trim() ?? "",
                GivenName = item.GivenName?.Trim() ?? "",
                FullName = item.FullName?.Trim() ?? "",
                Email = item.Email?.Trim() ?? "",
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.RoleCode))
            .GroupBy(item => item.RoleCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        owner.RoleAssignments.Clear();
        owner.RoleAssignments.AddRange(normalized);
    }

    private static bool Equivalent(
        AlbumPageRoleAssignment left,
        AlbumPageRoleAssignment right) =>
        left.RoleCode.Equals(right.RoleCode, StringComparison.OrdinalIgnoreCase) &&
        left.ParticipantId.Equals(right.ParticipantId, StringComparison.OrdinalIgnoreCase) &&
        left.FamilyName.Equals(right.FamilyName, StringComparison.Ordinal) &&
        left.GivenName.Equals(right.GivenName, StringComparison.Ordinal) &&
        left.FullName.Equals(right.FullName, StringComparison.Ordinal) &&
        left.Email.Equals(right.Email, StringComparison.OrdinalIgnoreCase);
}

public static class AlbumPageRoleAssignmentResolver
{
    public static string? ResolveDocumentName(
        IEnumerable<AlbumPageRoleAssignment>? assignments,
        string roleCode,
        IEnumerable<ProjectParticipant>? participants)
    {
        string normalizedRole = AlbumPageRoleCodes.Normalize(roleCode);
        AlbumPageRoleAssignment? assignment = (assignments ?? [])
            .FirstOrDefault(item => AlbumPageRoleCodes.Normalize(item.RoleCode).Equals(
                normalizedRole,
                StringComparison.OrdinalIgnoreCase));
        if (assignment is null)
            return null;

        ProjectParticipant? current = (participants ?? [])
            .FirstOrDefault(participant => Matches(assignment, participant));
        return current is null
            ? MongolianPersonNameFormatter.ForDocument(
                assignment.FamilyName,
                assignment.GivenName,
                assignment.FullName)
            : MongolianPersonNameFormatter.ForDocument(
                current.FamilyName,
                current.GivenName,
                current.FullName);
    }

    private static bool Matches(
        AlbumPageRoleAssignment assignment,
        ProjectParticipant participant)
    {
        if (!string.IsNullOrWhiteSpace(assignment.ParticipantId) &&
            assignment.ParticipantId.Equals(
                participant.ParticipantId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return string.IsNullOrWhiteSpace(assignment.ParticipantId) &&
               !string.IsNullOrWhiteSpace(assignment.Email) &&
               assignment.Email.Equals(participant.Email, StringComparison.OrdinalIgnoreCase);
    }
}
