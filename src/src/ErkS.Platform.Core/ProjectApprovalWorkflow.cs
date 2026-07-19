namespace ErkS.Platform.Core;

/// <summary>
/// Project-document approval metadata. These entries describe what is printed
/// on an album; they are deliberately separate from Cloud ERA team membership.
/// </summary>
public sealed class ProjectApprovalWorkflow
{
    /// <summary>Загвар зургийн шатны БАТЛАВ болон ЗӨВШӨӨРӨЛЦСӨН хэсэг.</summary>
    public ConceptDesignApprovalRoster ConceptDesign { get; set; } = new();

    /// <summary>
    /// Ажлын зургийн шатны ЗӨВШИЛЦСӨН талууд. Загвар зургийн нүүр хуудсанд
    /// эдгээр мөрийг дүрслэхгүй.
    /// </summary>
    public List<ProjectApprovalEntry> WorkingDrawingConsultedBy { get; set; } = [];

    public ProjectApprovalWorkflow Clone() => new()
    {
        ConceptDesign = ConceptDesign.Clone(),
        WorkingDrawingConsultedBy = WorkingDrawingConsultedBy
            .Select(entry => entry.Clone())
            .ToList(),
    };

    public void Normalize()
    {
        ConceptDesign ??= new ConceptDesignApprovalRoster();
        WorkingDrawingConsultedBy ??= [];
        ConceptDesign.Normalize();
        NormalizeEntries(WorkingDrawingConsultedBy);
    }

    internal static void NormalizeEntries(List<ProjectApprovalEntry> entries)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (entries[index] is null)
            {
                entries.RemoveAt(index);
                continue;
            }

            entries[index].Normalize();
        }
    }
}

public sealed class ConceptDesignApprovalRoster
{
    /// <summary>
    /// False means an older project has not configured this roster yet and the
    /// cover may derive a compatibility view from PlanningTask.AuthorityMembers.
    /// </summary>
    public bool IsConfigured { get; set; }

    /// <summary>БАТЛАВ: one or more chief architects, in printed order.</summary>
    public List<ProjectApprovalEntry> ApprovedBy { get; set; } = [];

    /// <summary>ЗӨВШӨӨРӨЛЦСӨН: concept-stage officials, in printed order.</summary>
    public List<ProjectApprovalEntry> EndorsedBy { get; set; } = [];

    public ConceptDesignApprovalRoster Clone() => new()
    {
        IsConfigured = IsConfigured,
        ApprovedBy = ApprovedBy.Select(entry => entry.Clone()).ToList(),
        EndorsedBy = EndorsedBy.Select(entry => entry.Clone()).ToList(),
    };

    public void Normalize()
    {
        ApprovedBy ??= [];
        EndorsedBy ??= [];
        ProjectApprovalWorkflow.NormalizeEntries(ApprovedBy);
        ProjectApprovalWorkflow.NormalizeEntries(EndorsedBy);
    }
}

public sealed class ProjectApprovalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationName { get; set; } = "";
    public string PositionTitle { get; set; } = "";
    public string PersonName { get; set; } = "";

    /// <summary>
    /// When true, this concept-stage endorsed official is printed in the
    /// facade-sheet HYaNAV block. BATLAV rows are resolved from ApprovedBy and
    /// do not need this flag.
    /// </summary>
    public bool IncludeInElevationHeader { get; set; }

    public ProjectApprovalEntry Clone() => new()
    {
        Id = Id,
        OrganizationName = OrganizationName,
        PositionTitle = PositionTitle,
        PersonName = PersonName,
        IncludeInElevationHeader = IncludeInElevationHeader,
    };

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");
        OrganizationName = OrganizationName?.Trim() ?? "";
        PositionTitle = PositionTitle?.Trim() ?? "";
        PersonName = PersonName?.Trim() ?? "";
    }
}

public static class ProjectApprovalRosterLimits
{
    public const int MinApprovedBy = 1;
    public const int MaxApprovedBy = 3;
    public const int MinEndorsedBy = 2;
    public const int MaxEndorsedBy = 6;
}

public sealed record ConceptCoverApprovalSnapshot(
    IReadOnlyList<ProjectApprovalEntry> ApprovedBy,
    IReadOnlyList<ProjectApprovalEntry> EndorsedBy);

public sealed record ConceptElevationHeaderSnapshot(
    IReadOnlyList<ProjectApprovalEntry> ApprovedBy,
    IReadOnlyList<ProjectApprovalEntry> ReviewedBy);

/// <summary>
/// Resolves facade-sheet officials from the ATD roster. The cover's BATLAV
/// officials are reused verbatim; HYaNAV contains only endorsed officials the
/// user explicitly selected in the ATD editor.
/// </summary>
public static class ConceptElevationHeaderResolver
{
    public static ConceptElevationHeaderSnapshot Resolve(
        ProjectApprovalWorkflow? workflow,
        PlanningTaskInformation? planningTask)
    {
        ConceptCoverApprovalSnapshot cover = ConceptCoverApprovalResolver.Resolve(
            workflow,
            planningTask);
        return new ConceptElevationHeaderSnapshot(
            cover.ApprovedBy,
            cover.EndorsedBy
                .Where(entry => entry.IncludeInElevationHeader)
                .Select(entry => entry.Clone())
                .ToList());
    }
}

/// <summary>
/// Resolves the exact concept-cover rows while preserving legacy projects.
/// </summary>
public static class ConceptCoverApprovalResolver
{
    public static ConceptCoverApprovalSnapshot Resolve(
        ProjectApprovalWorkflow? workflow,
        PlanningTaskInformation? planningTask)
    {
        ConceptDesignApprovalRoster? configured = workflow?.ConceptDesign;
        if (configured?.IsConfigured == true)
        {
            return new ConceptCoverApprovalSnapshot(
                NormalizeCount(
                    configured.ApprovedBy,
                    ProjectApprovalRosterLimits.MinApprovedBy,
                    ProjectApprovalRosterLimits.MaxApprovedBy,
                    defaultPosition: "Ерөнхий архитектор"),
                NormalizeCount(
                    configured.EndorsedBy,
                    ProjectApprovalRosterLimits.MinEndorsedBy,
                    ProjectApprovalRosterLimits.MaxEndorsedBy));
        }

        List<ProjectMember> members = planningTask?.AuthorityMembers ?? [];
        List<ProjectMember> approvedMembers = members
            .Where(IsChiefArchitect)
            .Take(ProjectApprovalRosterLimits.MaxApprovedBy)
            .ToList();
        IReadOnlyList<ProjectApprovalEntry> approvedBy = NormalizeCount(
            approvedMembers.Select(ToLegacyEntry),
            ProjectApprovalRosterLimits.MinApprovedBy,
            ProjectApprovalRosterLimits.MaxApprovedBy,
            defaultPosition: "Ерөнхий архитектор");

        HashSet<string> approvedIds = approvedMembers
            .Select(member => member.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ProjectApprovalEntry> endorsedBy = NormalizeCount(
            members
                .Where(member => !approvedMembers.Contains(member) && !approvedIds.Contains(member.Id))
                .Where(member => member.Roles.Count > 0 || !string.IsNullOrWhiteSpace(member.FullName))
                .Select(ToLegacyEntry),
            ProjectApprovalRosterLimits.MinEndorsedBy,
            ProjectApprovalRosterLimits.MaxEndorsedBy);

        return new ConceptCoverApprovalSnapshot(approvedBy, endorsedBy);
    }

    public static string DisplayPosition(ProjectApprovalEntry? entry)
    {
        if (entry is null)
            return "";

        string organization = entry.OrganizationName?.Trim() ?? "";
        string position = entry.PositionTitle?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(organization))
            return position;
        if (string.IsNullOrWhiteSpace(position))
            return organization;
        if (organization.Contains(position, StringComparison.OrdinalIgnoreCase) ||
            position.Contains(organization, StringComparison.OrdinalIgnoreCase))
        {
            return organization.Length >= position.Length ? organization : position;
        }

        return organization + Environment.NewLine + position;
    }

    private static IReadOnlyList<ProjectApprovalEntry> NormalizeCount(
        IEnumerable<ProjectApprovalEntry>? source,
        int minimum,
        int maximum,
        string defaultPosition = "")
    {
        List<ProjectApprovalEntry> entries = (source ?? [])
            .Where(entry => entry is not null)
            .Take(maximum)
            .Select(entry => entry.Clone())
            .ToList();
        foreach (ProjectApprovalEntry entry in entries)
            entry.Normalize();

        while (entries.Count < minimum)
        {
            entries.Add(new ProjectApprovalEntry
            {
                PositionTitle = entries.Count == 0 ? defaultPosition : "",
            });
        }

        return entries;
    }

    private static bool IsChiefArchitect(ProjectMember member) => member.Roles.Any(role =>
        role.Contains("Chief Architect", StringComparison.OrdinalIgnoreCase) ||
        role.Contains("Major Architect", StringComparison.OrdinalIgnoreCase) ||
        role.Contains("Ерөнхий архитектор", StringComparison.OrdinalIgnoreCase));

    private static ProjectApprovalEntry ToLegacyEntry(ProjectMember member) => new()
    {
        Id = string.IsNullOrWhiteSpace(member.Id) ? Guid.NewGuid().ToString("N") : member.Id,
        PositionTitle = string.Join(", ", member.Roles
            .Select(DisplayLegacyRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)),
        PersonName = MongolianPersonNameFormatter.ForDisplay(
            member.FamilyName,
            member.GivenName,
            member.FullName),
    };

    private static string DisplayLegacyRole(string role)
    {
        if (role.Contains("Chief Architect", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("Major Architect", StringComparison.OrdinalIgnoreCase))
            return "Ерөнхий архитектор";
        if (role.Contains("Department Head", StringComparison.OrdinalIgnoreCase))
            return "Хэлтсийн дарга";
        if (role.Contains("Authority Specialist", StringComparison.OrdinalIgnoreCase))
            return "Хот байгуулалтын мэргэжилтэн";
        return role;
    }
}
