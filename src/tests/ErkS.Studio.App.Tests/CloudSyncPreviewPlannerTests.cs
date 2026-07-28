using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class CloudSyncPreviewPlannerTests
{
    [Fact]
    public void AdminPendingCanonicalMetadataIsShownAsUpload()
    {
        ProjectWorkspace project = CloudProject("ProjectAdmin");
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Updated project",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };
        project.Foundation.DesignCompany.AssignmentSource = "StudioCloudPending";

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "admin@example.com",
            "WORKSTATION · abc12345",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.True(plan.AuthorizeProjectInformation);
        Assert.True(plan.AuthorizeCompanyAssignment);
        Assert.Contains(plan.Uploads, item => item.Code == "project-information");
        Assert.Contains(plan.Uploads, item => item.Code == "company-assignment");
        Assert.Empty(plan.Blocked);
    }

    [Fact]
    public void NonAdminCanonicalMetadataRemainsBlockedAndPending()
    {
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            Name = "Unauthorized local edit",
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };
        project.Cloud.CanonicalTitleBlockPending = true;

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · def67890",
            new StudioCloudProjectRefreshResult(false, null));

        Assert.False(plan.AuthorizeProjectInformation);
        Assert.False(plan.AuthorizeCanonicalTitleBlock);
        Assert.DoesNotContain(plan.Uploads, item =>
            item.Code is "project-information" or "canonical-title-block");
        Assert.Contains(plan.Blocked, item => item.Code == "project-information");
        Assert.Contains(plan.Blocked, item => item.Code == "canonical-title-block");
        Assert.True(plan.HasBlockedPendingChanges);
    }

    [Fact]
    public void ModifiedCloudProjectIsShownAsDownloadWithoutCreatingUpload()
    {
        ProjectWorkspace project = CloudProject("Architect");

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · def67890",
            new StudioCloudProjectRefreshResult(true, null));

        Assert.False(plan.HasUploads);
        Assert.True(plan.HasDownloads);
        Assert.Contains(plan.Downloads, item => item.Code == "remote-project");
    }

    [Fact]
    public void ChangedRemoteSourceIsListedByStreamOwnerAndPageCount()
    {
        ProjectWorkspace project = CloudProject("Architect");
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceId = "source-1",
            SourceKey = "building-a-revit",
            SourceApplication = "Revit",
            SourceDocumentReference = "Building A.rvt",
            ManifestId = "manifest-old",
            ContentHash = "hash-old",
            SheetCount = 3,
            Status = "Registered",
            RegisteredBy = "designer@example.com",
            OwnerEmail = "designer@example.com",
        });
        var remoteDetail = new StudioCloudProjectDetail
        {
            DesignPackages =
            [
                new StudioCloudDesignPackage
                {
                    SourcePackages =
                    [
                        new StudioCloudSourcePackage
                        {
                            SourceId = "source-2",
                            SourceKey = "building-a-revit",
                            SourceApplication = "Revit",
                            SourceDocumentReference = @"D:\Models\Building A.rvt",
                            ManifestId = "manifest-new",
                            ContentHash = "hash-new",
                            SheetCount = 5,
                            Status = "Registered",
                            RegisteredBy = "designer@example.com",
                            CustodianEmail = "designer@example.com",
                        },
                    ],
                },
            ],
        };

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            "architect@example.com",
            "LAPTOP · def67890",
            new StudioCloudProjectRefreshResult(true, remoteDetail));

        CloudSyncChangeItem sourceChange = Assert.Single(
            plan.Downloads,
            item => item.Code.StartsWith("remote-source:", StringComparison.Ordinal));
        Assert.Contains("Source шинэчлэгдсэн", sourceChange.Title);
        Assert.Contains("Building A.rvt", sourceChange.Title);
        Assert.Contains("designer@example.com", sourceChange.Detail);
        Assert.Contains("5 хуудас", sourceChange.Detail);
        Assert.Contains("native файл татахгүй", sourceChange.Detail);
    }

    private static ProjectWorkspace CloudProject(params string[] roles)
    {
        var project = new ProjectWorkspace();
        project.Identity.Code = "TEST-001";
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project-1";
        project.Cloud.CurrentUserRoles = [.. roles];
        return project;
    }
}
