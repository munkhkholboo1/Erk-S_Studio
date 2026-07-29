using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCloudSourceBindingPolicyTests
{
    private const string OwnerA = "owner-a@erks.local";
    private const string OwnerB = "owner-b@erks.local";
    private const string SourceKey = "portable-source-key";

    [Fact]
    public void DifferentImmutableOwnerWithSameSourceKey_RemainsEligible()
    {
        ProjectWorkspace project = Project();
        ProjectDesignSource bindingTarget = Local("target", "target-key", "");
        project.Sources =
        [
            bindingTarget,
            Local("local-a", SourceKey, OwnerA),
        ];
        StudioCloudSourcePackage cloudB = Cloud("cloud-b", SourceKey, OwnerB);

        StudioCloudSourcePackage eligible = Assert.Single(
            StudioCloudSourceBindingPolicy.EligibleSources(
                project,
                bindingTarget,
                [cloudB],
                OwnerB));

        Assert.Same(cloudB, eligible);
    }

    [Fact]
    public void SameImmutableOwnerAndSourceKey_IsRejectedAsDuplicate()
    {
        ProjectWorkspace project = Project();
        ProjectDesignSource bindingTarget = Local("target", "target-key", "");
        project.Sources =
        [
            bindingTarget,
            Local("local-b", SourceKey, OwnerB),
        ];

        Assert.Empty(StudioCloudSourceBindingPolicy.EligibleSources(
            project,
            bindingTarget,
            [Cloud("cloud-b", SourceKey, OwnerB)],
            OwnerB));
    }

    [Fact]
    public void LegacyLocalOwner_UsesOnlyUniqueCloudRegistrant()
    {
        ProjectWorkspace project = Project();
        ProjectDesignSource bindingTarget = Local("target", "target-key", "");
        ProjectDesignSource legacy = Local("legacy", SourceKey, "");
        project.Sources = [bindingTarget, legacy];
        project.Cloud.SharedSources =
        [
            Shared("shared-b", SourceKey, OwnerB),
        ];
        StudioCloudSourcePackage cloudB = Cloud("cloud-b", SourceKey, OwnerB);

        Assert.Empty(StudioCloudSourceBindingPolicy.EligibleSources(
            project,
            bindingTarget,
            [cloudB],
            OwnerB));

        project.Cloud.SharedSources.Add(
            Shared("shared-a", SourceKey, OwnerA));

        Assert.Single(StudioCloudSourceBindingPolicy.EligibleSources(
            project,
            bindingTarget,
            [cloudB],
            OwnerB));
    }

    [Fact]
    public void LegacyCloudRegistrant_FallsBackToCurrentCustodianConsistently()
    {
        StudioCloudSourcePackage legacy = Cloud(
            "legacy-cloud",
            SourceKey,
            owner: "");

        Assert.Equal(
            OwnerB,
            StudioCloudSourceBindingPolicy.ImmutableOwner(
                legacy,
                OwnerB));
    }

    private static ProjectWorkspace Project() => new()
    {
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project-1",
        },
    };

    private static ProjectDesignSource Local(
        string id,
        string sourceKey,
        string owner)
    {
        var source = new ProjectDesignSource { Id = id };
        var holder = new ProjectWorkspace { Sources = [source] };
        ProjectCloudSyncMetadata.BindToCloudSource(
            holder,
            source,
            sourceKey);
        if (!string.IsNullOrWhiteSpace(owner))
            ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        return source;
    }

    private static StudioCloudSourcePackage Cloud(
        string id,
        string sourceKey,
        string owner) => new()
    {
        SourceId = id,
        SourceKey = sourceKey,
        RegisteredBy = owner,
        CustodianEmail = string.IsNullOrWhiteSpace(owner)
            ? OwnerB
            : owner,
        Status = "Registered",
    };

    private static ProjectCloudSourceReference Shared(
        string id,
        string sourceKey,
        string owner) => new()
    {
        SourceId = id,
        SourceKey = sourceKey,
        RegisteredBy = owner,
        OwnerEmail = owner,
        Status = "Registered",
    };
}
