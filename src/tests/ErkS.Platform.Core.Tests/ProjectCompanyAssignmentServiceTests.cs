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
    public void RefreshAssignedSnapshotPropagatesRegistryAndDesignRepresentativeFields()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("ORG-REGISTRY-001", "Organization registry sync");
        project.Foundation.DesignCompany.OrganizationId = "org-design-registry";
        project.Foundation.DesignCompany.OrganizationName = "Design company";
        project.Foundation.DesignCompany.OrganizationSnapshot = new CompanyProfile
        {
            OrganizationId = "org-design-registry",
            Name = "Design company",
        };
        var current = new CompanyProfile
        {
            OrganizationId = "org-design-registry",
            Name = "Official Design LLC",
            RegistrationNumber = "1234567",
            LegalEntityType = "Хуулийн этгээд",
            LegalForm = "ХХК",
            ActivityDirections = ["Архитектур", "Зураг төсөл"],
            RegisteredAtUtc = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
            OfficialRepresentativeName = "Б.Бат",
            RegistrySource = "OfficialRegistry",
            RegistryCheckedAtUtc = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            DesignRepresentativeTitle = "Зураг төсөл хариуцсан захирал",
            DesignRepresentativeName = "Э.Мөнххолбоо",
        };

        bool changed = ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, current);

        Assert.True(changed);
        CompanyProfile snapshot = project.Foundation.DesignCompany.OrganizationSnapshot;
        Assert.Equal("Хуулийн этгээд", snapshot.LegalEntityType);
        Assert.Equal(["Архитектур", "Зураг төсөл"], snapshot.ActivityDirections);
        Assert.Equal("Б.Бат", snapshot.OfficialRepresentativeName);
        Assert.Equal("OfficialRegistry", snapshot.RegistrySource);
        Assert.Equal("Зураг төсөл хариуцсан захирал", snapshot.DesignRepresentativeTitle);
        Assert.Equal("Э.Мөнххолбоо", snapshot.DesignRepresentativeName);
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
    public void RefreshAssignedSnapshotPropagatesDocumentAvailability()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("ATD-003", "Document source update");
        project.Foundation.DesignCompany.OrganizationId = "org-design-1";
        project.Foundation.DesignCompany.OrganizationName = "Design company";
        project.Foundation.DesignCompany.OrganizationSnapshot = new CompanyProfile
        {
            OrganizationId = "org-design-1",
            Name = "Design company",
            RegistrationCertificateDocuments =
            [
                new ProjectFileReference
                {
                    Id = "certificate-1",
                    Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                    RelativePath = "certificate.pdf",
                    Sha256 = "abc",
                    IsAvailable = true,
                },
            ],
        };
        CompanyProfile current = project.Foundation.DesignCompany.OrganizationSnapshot.Clone();
        Assert.Single(current.RegistrationCertificateDocuments).IsAvailable = false;

        bool changed = ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, current);

        Assert.True(changed);
        Assert.False(Assert.Single(project.Foundation.DesignCompany.OrganizationSnapshot
            .RegistrationCertificateDocuments).IsAvailable);
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

    [Fact]
    public void MergeCloudAssignmentDoesNotClearConfirmedCompanyWhenServerOmitsAssignment()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "CLOUD-003",
            Name = "Cloud project",
            InitiatorType = ProjectInitiatorTypes.DesignOrganization,
            InitiatorOrganizationId = "org-design-1",
            InitiatorOrganizationName = "Erk-S Design",
        });
        project.Foundation.DesignCompany.AssignmentSource = "StudioCloudSelected";

        bool changed = ProjectCompanyAssignmentService.MergeCloudAssignment(
            project,
            "",
            "",
            null);

        Assert.False(changed);
        Assert.Equal("org-design-1", project.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("Erk-S Design", project.Foundation.DesignCompany.OrganizationName);
        Assert.Equal("StudioCloudSelected", project.Foundation.DesignCompany.AssignmentSource);
    }

    [Fact]
    public void MergeCloudAssignmentConfirmsPendingCompanyWithoutReselection()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "CLOUD-004",
            Name = "Cloud project",
        });
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project-1";
        var company = new CompanyProfile
        {
            OrganizationId = "org-design-2",
            OrganizationType = "DesignCompany",
            Name = "Another company",
            DisplayName = "Another Design",
        };
        ProjectCompanyAssignmentService.AssignToProject(project, company, DateTimeOffset.UtcNow);

        bool changed = ProjectCompanyAssignmentService.MergeCloudAssignment(
            project,
            "org-design-2",
            "Another company",
            company);

        Assert.True(changed);
        Assert.Equal("org-design-2", project.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("StudioCloudSelected", project.Foundation.DesignCompany.AssignmentSource);
        Assert.Empty(project.Foundation.DesignCompany.History);
    }

    [Fact]
    public void MergeCloudAssignmentAcceptsCanonicalCompanyChangeAndKeepsHistory()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "CLOUD-005",
            Name = "Cloud project",
            InitiatorType = ProjectInitiatorTypes.DesignOrganization,
            InitiatorOrganizationId = "org-design-1",
            InitiatorOrganizationName = "Original company",
        });

        bool changed = ProjectCompanyAssignmentService.MergeCloudAssignment(
            project,
            "org-design-2",
            "Replacement company",
            new CompanyProfile
            {
                OrganizationId = "org-design-2",
                OrganizationType = "DesignCompany",
                Name = "Replacement company",
            });

        Assert.True(changed);
        Assert.Equal("org-design-2", project.Foundation.DesignCompany.OrganizationId);
        Assert.Equal("CloudERA", project.Foundation.DesignCompany.AssignmentSource);
        Assert.Equal("org-design-1", Assert.Single(project.Foundation.DesignCompany.History).OrganizationId);
    }

    [Fact]
    public void MergeCloudAssignmentPreservesLocalDocumentAssetsForTheSameCompany()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create(new ProjectCreationRequest
        {
            Code = "CLOUD-DOC-001",
            Name = "Cloud project",
            InitiatorType = ProjectInitiatorTypes.DesignOrganization,
            InitiatorOrganizationId = "org-design-1",
            InitiatorOrganizationName = "Erk-S Design",
        });
        project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments =
        [
            new ProjectFileReference
            {
                Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                RelativePath = @"C:\local-cache\registration.pdf",
                Sha256 = "registration-hash",
                PageCount = 2,
            },
        ];
        project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments =
        [
            new ProjectFileReference
            {
                Category = ProjectDocumentCategories.CompanyDesignLicense,
                RelativePath = @"C:\local-cache\license.pdf",
                Sha256 = "license-hash",
                PageCount = 1,
            },
        ];

        bool changed = ProjectCompanyAssignmentService.MergeCloudAssignment(
            project,
            "org-design-1",
            "Erk-S Design updated",
            new CompanyProfile
            {
                OrganizationId = "org-design-1",
                Name = "Erk-S Design updated",
            });

        Assert.True(changed);
        CompanyProfile snapshot = project.Foundation.DesignCompany.OrganizationSnapshot;
        Assert.Equal("registration-hash", Assert.Single(snapshot.RegistrationCertificateDocuments).Sha256);
        Assert.Equal("license-hash", Assert.Single(snapshot.DesignLicenseDocuments).Sha256);
    }
}
