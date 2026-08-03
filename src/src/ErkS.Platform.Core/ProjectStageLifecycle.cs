namespace ErkS.Platform.Core;

public static class ProjectStageStatuses
{
    public const string AwaitingOrganization = "AwaitingOrganization";
    public const string Active = "Active";
    public const string Completed = "Completed";
}

public static class ProjectStageAssignmentStatuses
{
    public const string Proposed = "Proposed";
    public const string Active = "Active";
    public const string Ended = "Ended";
}

public sealed class ProjectStageInstance
{
    public string StageInstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string StageType { get; set; } = "";
    public string StageName { get; set; } = "";
    public int Sequence { get; set; }
    public string PreviousStageInstanceId { get; set; } = "";
    public string BasisAlbumRevisionId { get; set; } = "";
    public string Status { get; set; } = ProjectStageStatuses.AwaitingOrganization;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class ProjectStageOrganizationAssignment
{
    public string AssignmentId { get; set; } = Guid.NewGuid().ToString("N");
    public string StageInstanceId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public CompanyProfile OrganizationSnapshot { get; set; } = new();
    public string Role { get; set; } = "LeadDesigner";
    public string Status { get; set; } = ProjectStageAssignmentStatuses.Proposed;
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string EndReason { get; set; } = "";
}

public static class ProjectStageLifecycle
{
    public static ProjectStageInstance EnsureLegacyStage(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Stages.Count > 0)
            return project.Stages.OrderBy(item => item.Sequence).First();

        DateTimeOffset createdAt = project.CreatedAtUtc == default
            ? DateTimeOffset.UtcNow
            : project.CreatedAtUtc;
        ProjectStageInstance stage = new()
        {
            StageInstanceId = Guid.NewGuid().ToString("N"),
            StageType = project.Identity.StageCode,
            StageName = project.Identity.StageName,
            Sequence = 1,
            Status = ProjectStageStatuses.Active,
            CreatedAtUtc = createdAt,
        };
        project.Stages.Add(stage);

        ProjectCompanyAssignment legacy = project.Foundation.DesignCompany;
        if (!string.IsNullOrWhiteSpace(legacy.OrganizationId) ||
            !string.IsNullOrWhiteSpace(legacy.OrganizationName))
        {
            CompanyProfile snapshot = legacy.OrganizationSnapshot.Clone();
            if (string.IsNullOrWhiteSpace(snapshot.OrganizationId))
                snapshot.OrganizationId = legacy.OrganizationId;
            if (string.IsNullOrWhiteSpace(snapshot.Name))
                snapshot.Name = legacy.OrganizationName;
            project.StageAssignments.Add(new ProjectStageOrganizationAssignment
            {
                StageInstanceId = stage.StageInstanceId,
                OrganizationId = snapshot.OrganizationId,
                OrganizationSnapshot = snapshot,
                Status = ProjectStageAssignmentStatuses.Active,
                AcceptedAtUtc = legacy.AssignedAtUtc ?? createdAt,
            });
        }
        return stage;
    }

    public static CompanyProfile ResolveAlbumOrganization(
        ProjectWorkspace project,
        string stageInstanceId,
        DateTimeOffset? revisionCreatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        DateTimeOffset at = revisionCreatedAtUtc ?? DateTimeOffset.MaxValue;
        ProjectStageOrganizationAssignment? assignment = project.StageAssignments
            .Where(item =>
                item.StageInstanceId.Equals(stageInstanceId, StringComparison.OrdinalIgnoreCase) &&
                item.AcceptedAtUtc.GetValueOrDefault(DateTimeOffset.MinValue) <= at &&
                (!item.EndedAtUtc.HasValue || item.EndedAtUtc > at))
            .OrderByDescending(item => item.AcceptedAtUtc)
            .FirstOrDefault();
        return assignment?.OrganizationSnapshot.Clone() ?? new CompanyProfile();
    }
}
