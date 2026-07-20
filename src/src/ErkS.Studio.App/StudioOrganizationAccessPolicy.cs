namespace ErkS.Studio;

internal static class StudioOrganizationAccessPolicy
{
    private static readonly HashSet<string> LegacyManagementRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "organizationowner",
        "organizationadmin",
        "designcompanyadmin",
        "owner",
        "admin",
    };

    public static bool CanCreateDesignProject(StudioCloudOrganization? organization)
    {
        if (organization is null || string.IsNullOrWhiteSpace(organization.OrganizationId))
            return false;

        string organizationType = NormalizeToken(organization.OrganizationType);
        if (organizationType is not ("designcompany" or "designorganization"))
            return false;

        return organization.CanManage ||
            LegacyManagementRoles.Contains(NormalizeToken(organization.CurrentUserRole));
    }

    private static string NormalizeToken(string? value) =>
        new((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
}
