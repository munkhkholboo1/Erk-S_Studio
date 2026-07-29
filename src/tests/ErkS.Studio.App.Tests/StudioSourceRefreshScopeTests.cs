using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSourceRefreshScopeTests
{
    [Fact]
    public void SameSourceKey_RefreshesOnlyTheCurrentUsersStream()
    {
        const string sourceKey = "shared-key";
        const string ownerA = "a@erks.local";
        const string ownerB = "b@erks.local";
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource sourceA = Local("source-a", sourceKey, ownerA);
        ProjectDesignSource sourceB = Local("source-b", sourceKey, ownerB);
        project.Sources = [sourceA, sourceB];
        project.Cloud.SharedSources =
        [
            Shared(sourceKey, ownerA, ownerA),
            Shared(sourceKey, ownerB, ownerB),
        ];

        IReadOnlyList<ProjectDesignSource> owned =
            StudioSourceRefreshScope.OwnedSources(project, ownerA);

        Assert.Equal([sourceA], owned);
    }

    [Fact]
    public void ApprovedCustodian_CanRefreshWithoutChangingImmutableRegistrant()
    {
        const string sourceKey = "transferred-source";
        const string registrant = "original@erks.local";
        const string custodian = "custodian@erks.local";
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource local = Local("source-a", sourceKey, registrant);
        project.Sources = [local];
        project.Cloud.SharedSources =
        [
            Shared(sourceKey, registrant, custodian),
        ];

        ProjectDesignSource owned = Assert.Single(
            StudioSourceRefreshScope.OwnedSources(project, custodian));

        Assert.Same(local, owned);
        Assert.Equal(
            registrant,
            ProjectCloudSyncMetadata.CloudOwnerEmail(owned));
    }

    private static ProjectWorkspace CloudProject() => new()
    {
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project",
        },
    };

    private static ProjectDesignSource Local(
        string id,
        string sourceKey,
        string owner)
    {
        var source = new ProjectDesignSource { Id = id };
        var project = new ProjectWorkspace { Sources = [source] };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, sourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        return source;
    }

    private static ProjectCloudSourceReference Shared(
        string sourceKey,
        string registeredBy,
        string custodian) => new()
    {
        SourceId = Guid.NewGuid().ToString("N"),
        SourceKey = sourceKey,
        Status = "Registered",
        RegisteredBy = registeredBy,
        CustodianEmail = custodian,
        OwnerEmail = custodian,
    };
}
