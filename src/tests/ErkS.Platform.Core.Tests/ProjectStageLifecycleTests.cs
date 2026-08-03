using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectStageLifecycleTests
{
    [Fact]
    public void Legacy_project_is_migrated_to_stage_scoped_assignment()
    {
        ProjectWorkspace project = new()
        {
            Identity = new ProjectIdentity
            {
                StageCode = "model-design",
                StageName = "Загвар зураг",
            },
            Foundation = new ProjectFoundation
            {
                DesignCompany = new ProjectCompanyAssignment
                {
                    OrganizationId = "org-a",
                    OrganizationName = "А байгууллага",
                    OrganizationSnapshot = new CompanyProfile
                    {
                        OrganizationId = "org-a",
                        Name = "А байгууллага",
                    },
                },
            },
        };

        ProjectStageInstance stage = ProjectStageLifecycle.EnsureLegacyStage(project);

        Assert.Equal("model-design", stage.StageType);
        ProjectStageOrganizationAssignment assignment = Assert.Single(project.StageAssignments);
        Assert.Equal(stage.StageInstanceId, assignment.StageInstanceId);
        Assert.Equal("org-a", assignment.OrganizationId);
    }

    [Fact]
    public void Organization_snapshot_is_resolved_at_revision_time_across_mid_stage_replacement()
    {
        DateTimeOffset changedAt = DateTimeOffset.Parse("2026-08-01T10:00:00Z");
        ProjectWorkspace project = new();
        project.StageAssignments =
        [
            new ProjectStageOrganizationAssignment
            {
                StageInstanceId = "working",
                OrganizationId = "org-a",
                OrganizationSnapshot = new CompanyProfile { OrganizationId = "org-a", Name = "А байгууллага" },
                Status = ProjectStageAssignmentStatuses.Ended,
                AcceptedAtUtc = changedAt.AddDays(-10),
                EndedAtUtc = changedAt,
            },
            new ProjectStageOrganizationAssignment
            {
                StageInstanceId = "working",
                OrganizationId = "org-b",
                OrganizationSnapshot = new CompanyProfile { OrganizationId = "org-b", Name = "Б байгууллага" },
                Status = ProjectStageAssignmentStatuses.Active,
                AcceptedAtUtc = changedAt,
            },
        ];

        CompanyProfile earlier = ProjectStageLifecycle.ResolveAlbumOrganization(
            project,
            "working",
            changedAt.AddMinutes(-1));
        CompanyProfile later = ProjectStageLifecycle.ResolveAlbumOrganization(
            project,
            "working",
            changedAt.AddMinutes(1));

        Assert.Equal("org-a", earlier.OrganizationId);
        Assert.Equal("org-b", later.OrganizationId);
    }
}
