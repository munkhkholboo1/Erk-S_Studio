using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectCompanyAssignmentServiceTests
{
    [Fact]
    public void RefreshAssignedSnapshotUsesOrganizationIdInsteadOfStaleName()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "ATD-001",
            Name = "Authority project",
            InitiatorType = ProjectInitiatorTypes.GovernmentAuthority,
            InitiatorOrganizationName = "Хот байгуулалтын газар",
        });
        project.Foundation.DesignCompany.OrganizationId = "org-design-1";
        project.Foundation.DesignCompany.OrganizationName = "Хуучин нэр";
        project.Foundation.DesignCompany.OrganizationSnapshot = new CompanyProfile
        {
            OrganizationId = "org-design-1",
            Name = "Хуучин нэр",
        };
        var current = new CompanyProfile
        {
            OrganizationId = "org-design-1",
            Name = "Erk-S LLC",
            DisplayName = "Erk-S зураг төслийн компани",
            RegistrationNumber = "250917",
        };

        bool changed = ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, current);

        Assert.True(changed);
        Assert.Equal("Erk-S LLC", project.Foundation.DesignCompany.OrganizationName);
        Assert.Equal("Erk-S зураг төслийн компани", project.Foundation.DesignCompany.OrganizationSnapshot.DisplayName);
        Assert.Equal("250917", project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationNumber);
        Assert.Equal("Хот байгуулалтын газар", project.Creation.InitiatorOrganizationName);
    }

    [Fact]
    public void RefreshAssignedSnapshotRejectsDifferentOrganizationId()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "ATD-002",
            Name = "Authority project",
            InitiatorType = ProjectInitiatorTypes.GovernmentAuthority,
            InitiatorOrganizationName = "Хот байгуулалтын газар",
        });
        project.Foundation.DesignCompany.OrganizationId = "org-design-1";
        project.Foundation.DesignCompany.OrganizationName = "Assigned company";

        bool changed = ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, new CompanyProfile
        {
            OrganizationId = "org-design-2",
            Name = "Another company",
        });

        Assert.False(changed);
        Assert.Equal("org-design-1", project.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("Assigned company", project.Foundation.DesignCompany.OrganizationName);
    }

    [Fact]
    public void LegacySelfCreatedAssignmentReceivesCloudOrganizationIdentity()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "LOCAL-001",
            Name = "Local project",
            InitiatorType = ProjectInitiatorTypes.DesignOrganization,
            InitiatorOrganizationName = "Erk-S Design",
        });
        var current = new CompanyProfile
        {
            OrganizationId = "org-design-1",
            Name = "Erk-S Design",
            DisplayName = "Erk-S Studio Design",
        };

        bool changed = ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, current);

        Assert.True(changed);
        Assert.Equal("org-design-1", project.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("org-design-1", project.Creation.InitiatorOrganizationId);
    }

    [Fact]
    public void AssignToProjectChangesCloudAssignmentAndKeepsHistory()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "CLOUD-001",
            Name = "Cloud project",
        });
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project-1";
        project.Foundation.DesignCompany.OrganizationId = "org-design-1";
        project.Foundation.DesignCompany.OrganizationName = "Original company";
        project.Foundation.DesignCompany.AssignmentSource = "CloudERA";
        project.Foundation.DesignCompany.OrganizationSnapshot = new CompanyProfile
        {
            OrganizationId = "org-design-1",
            OrganizationType = "DesignCompany",
            Name = "Original company",
        };

        ProjectCompanyAssignmentService.AssignToProject(project, new CompanyProfile
        {
            OrganizationId = "org-design-2",
            OrganizationType = "DesignCompany",
            Name = "Another company",
        }, DateTimeOffset.UtcNow);

        Assert.Equal("org-design-2", project.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("StudioCloudPending", project.Foundation.DesignCompany.AssignmentSource);
        Assert.Equal(ProjectSyncStatuses.Pending, project.Cloud.SyncStatus);
        ProjectCompanyAssignmentHistoryEntry previous = Assert.Single(project.Foundation.DesignCompany.History);
        Assert.Equal("org-design-1", previous.OrganizationId);
        Assert.Equal("Original company", previous.OrganizationSnapshot.Name);
    }

    [Fact]
    public void ConfirmCloudAssignmentClearsPendingStatus()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "CLOUD-002",
            Name = "Cloud project",
        });
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project-1";
        var company = new CompanyProfile
        {
            OrganizationId = "org-design-2",
            OrganizationType = "DesignCompany",
            Name = "Another company",
        };
        ProjectCompanyAssignmentService.AssignToProject(project, company, DateTimeOffset.UtcNow);

        ProjectCompanyAssignmentService.ConfirmCloudAssignment(project, company);

        Assert.Equal("StudioCloudSelected", project.Foundation.DesignCompany.AssignmentSource);
        Assert.Equal(ProjectSyncStatuses.Linked, project.Cloud.SyncStatus);
    }
}
