namespace ErkS.Platform.Core;

public static class AppointedArchitectResolver
{
    public static string ForDocument(IEnumerable<ProjectParticipant>? participants)
    {
        ProjectParticipant? architect = (participants ?? Array.Empty<ProjectParticipant>())
            .Where(participant => ProjectRoleSemantics.IsAppointedArchitect(participant.Role))
            .FirstOrDefault(participant =>
                !string.IsNullOrWhiteSpace(participant.FamilyName) ||
                !string.IsNullOrWhiteSpace(participant.GivenName) ||
                !string.IsNullOrWhiteSpace(participant.FullName));
        return architect is null
            ? ""
            : string.IsNullOrWhiteSpace(architect.FamilyName) &&
              string.IsNullOrWhiteSpace(architect.GivenName)
                ? MongolianPersonNameFormatter.ForDocument(architect.FullName)
                : MongolianPersonNameFormatter.ForDocument(
                    architect.FamilyName,
                    architect.GivenName,
                    architect.FullName);
    }
}
