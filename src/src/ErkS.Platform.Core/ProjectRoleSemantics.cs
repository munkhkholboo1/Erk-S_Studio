namespace ErkS.Platform.Core;

public static class ProjectRoleSemantics
{
    public static bool IsAppointedArchitect(string? role)
    {
        string normalized = new((role ?? "")
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return normalized.Equals("MajorArchitect", StringComparison.OrdinalIgnoreCase);
    }
}
