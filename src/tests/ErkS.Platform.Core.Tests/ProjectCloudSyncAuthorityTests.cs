using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectCloudSyncAuthorityTests
{
    [Fact]
    public void CanonicalMetadataRequiresAdminRoleOrScope()
    {
        var cloud = new ProjectCloudLink
        {
            CurrentUserRoles = ["Architect"],
            CurrentUserScopes = ["concept.write"],
        };

        Assert.False(ProjectCloudSyncAuthority.CanManageCanonicalMetadata(cloud));

        cloud.CurrentUserRoles.Add("DesignCompanyAdmin");
        Assert.True(ProjectCloudSyncAuthority.CanManageCanonicalMetadata(cloud));

        cloud.CurrentUserRoles.Clear();
        cloud.CurrentUserScopes.Add("team.manage");
        Assert.True(ProjectCloudSyncAuthority.CanManageCanonicalMetadata(cloud));
    }

    [Fact]
    public void SourceCustodianCanEditButAnotherMemberCannot()
    {
        ProjectWorkspace project = CloudProject();
        var source = new ProjectDesignSource { Id = "source-a" };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "stream-a");
        project.Sources.Add(source);
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceKey = "stream-a",
            RegisteredBy = "owner@example.com",
            CustodianEmail = "custodian@example.com",
            Status = "Active",
        });

        ProjectSourceEditAuthority allowed =
            ProjectCloudSyncAuthority.ResolveSource(
                project,
                source,
                "custodian@example.com");
        ProjectSourceEditAuthority denied =
            ProjectCloudSyncAuthority.ResolveSource(
                project,
                source,
                "other@example.com");

        Assert.True(allowed.CanEdit);
        Assert.False(denied.CanEdit);
        Assert.Equal("custodian@example.com", denied.OwnerEmail);
    }

    [Fact]
    public void SameAccountCanOwnIndependentSourceKeysOnDifferentDevices()
    {
        ProjectWorkspace project = CloudProject();
        var first = new ProjectDesignSource { Id = "source-a" };
        var second = new ProjectDesignSource { Id = "source-b" };
        ProjectCloudSyncMetadata.BindToCloudSource(project, first, "device-a-stream");
        ProjectCloudSyncMetadata.BindToCloudSource(project, second, "device-b-stream");
        project.Sources.AddRange([first, second]);
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceKey = "device-a-stream",
            RegisteredBy = "member@example.com",
            OwnerEmail = "member@example.com",
            Status = "Active",
        });

        Assert.True(ProjectCloudSyncAuthority.ResolveSource(
            project,
            first,
            "member@example.com").CanEdit);
        Assert.True(ProjectCloudSyncAuthority.ResolveSource(
            project,
            second,
            "member@example.com").CanEdit);
    }

    [Fact]
    public void ThirdUserCannotClaimOccupiedLegacySourceKey()
    {
        ProjectWorkspace project = CloudProject();
        var source = new ProjectDesignSource { Id = "source-c" };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "shared-key");
        project.Sources.Add(source);
        project.Cloud.SharedSources.AddRange(
        [
            new ProjectCloudSourceReference
            {
                SourceKey = "shared-key",
                RegisteredBy = "first@example.com",
                OwnerEmail = "first@example.com",
                Status = "Active",
            },
            new ProjectCloudSourceReference
            {
                SourceKey = "shared-key",
                RegisteredBy = "second@example.com",
                OwnerEmail = "second@example.com",
                Status = "Active",
            },
        ]);

        ProjectSourceEditAuthority result =
            ProjectCloudSyncAuthority.ResolveSource(
                project,
                source,
                "third@example.com");

        Assert.False(result.CanEdit);
        Assert.Equal("first@example.com", result.OwnerEmail);
    }

    private static ProjectWorkspace CloudProject()
    {
        var project = new ProjectWorkspace();
        project.Identity.Name = "Test";
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "project-1";
        return project;
    }
}
