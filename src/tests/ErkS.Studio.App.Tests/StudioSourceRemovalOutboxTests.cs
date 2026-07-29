using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSourceRemovalOutboxTests
{
    private const string Owner = "owner@example.com";
    private const string Other = "other@example.com";
    private const string Device = "device-a";
    private const string SourceKey = "source-key";
    private const string SourceId = "registry-source-id";

    [Fact]
    public void Stage_PersistsExactRegistryOwnerAndDeviceClaim()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();

        ProjectLocalAlbumComponentClaim claim =
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Owner,
                Device,
                hasVerifiedPayload: true,
                requestedAtUtc:
                    DateTimeOffset.Parse("2026-07-29T00:00:00Z"));

        Assert.True(claim.IsRemoval);
        Assert.Equal(SourceId, claim.RegistrySourceId);
        Assert.Equal(Owner, claim.OwnerEmail);
        Assert.Equal(Device, claim.DeviceFingerprint);
        Assert.True(StudioSourceRemovalOutbox.IsSourceStaged(
            project,
            source,
            Owner,
            Device));
        Assert.Single(StudioSourceRemovalOutbox.Pending(
            project,
            Owner,
            Device));
    }

    [Fact]
    public void Pending_IsInvisibleToAnotherAccountOrDevice()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();
        StudioSourceRemovalOutbox.Stage(
            project,
            source,
            registry,
            Owner,
            Device,
            hasVerifiedPayload: true);

        Assert.Empty(StudioSourceRemovalOutbox.Pending(
            project,
            Other,
            Device));
        Assert.Empty(StudioSourceRemovalOutbox.Pending(
            project,
            Owner,
            "device-b"));
    }

    [Fact]
    public void Stage_RejectsOwnerDevicePayloadOrRegistryMismatch()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();

        Assert.Throws<InvalidOperationException>(() =>
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Other,
                Device,
                hasVerifiedPayload: true));
        Assert.Throws<InvalidOperationException>(() =>
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Owner,
                Device,
                hasVerifiedPayload: false));

        registry.RegisteredBy = Other;
        Assert.Throws<InvalidOperationException>(() =>
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Owner,
                Device,
                hasVerifiedPayload: true));
    }

    [Fact]
    public void RetireAcknowledgement_RemovesOnlyExactRegistryMirror()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceId = "other-id",
            SourceKey = SourceKey,
            RegisteredBy = Other,
            OwnerEmail = Other,
        });
        ProjectLocalAlbumComponentClaim claim =
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Owner,
                Device,
                hasVerifiedPayload: true);

        StudioSourceRemovalOutbox.ApplyRegistryRetirement(project, claim);

        ProjectCloudSourceReference remaining =
            Assert.Single(project.Cloud.SharedSources);
        Assert.Equal("other-id", remaining.SourceId);
        Assert.Same(source, StudioSourceRemovalOutbox.ResolveLocalSource(
            project,
            claim));
    }

    [Fact]
    public void TransferredCustodian_CanStageImmutableOwnersComponentRemoval()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();
        registry.CustodianEmail = Other;
        StudioLocalSourceBindingPolicy.Bind(source, Other, Device);

        ProjectLocalAlbumComponentClaim claim =
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Other,
                Device,
                hasVerifiedPayload: true);

        Assert.Equal(Other, claim.OwnerEmail);
        Assert.Equal(
            StudioAlbumComponentIdentity.SourceCode(Owner, SourceKey),
            claim.ComponentCode);
        Assert.Single(StudioSourceRemovalOutbox.Pending(
            project,
            Other,
            Device));
        Assert.Same(source, StudioSourceRemovalOutbox.ResolveLocalSource(
            project,
            claim));
    }

    [Fact]
    public async Task FailedRetire_KeepsRegistryLocalSourceAndTombstonePending()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();
        ProjectLocalAlbumComponentClaim claim =
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Owner,
                Device,
                hasVerifiedPayload: true);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            StudioSourceRemovalOutbox.ConfirmRegistryRetirementAsync(
                project,
                claim,
                Owner,
                Device,
                _ => throw new HttpRequestException("offline")));

        Assert.Contains(registry, project.Cloud.SharedSources);
        Assert.Contains(source, project.Sources);
        Assert.Single(StudioSourceRemovalOutbox.Pending(
            project,
            Owner,
            Device));
    }

    [Fact]
    public async Task StaleOperationContext_DoesNotApplySuccessfulRetireToMirror()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();
        ProjectLocalAlbumComponentClaim claim =
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Owner,
                Device,
                hasVerifiedPayload: true);
        bool serverRetired = false;

        await Assert.ThrowsAsync<StudioOperationContextChangedException>(() =>
            StudioSourceRemovalOutbox.ConfirmRegistryRetirementAsync(
                project,
                claim,
                Owner,
                Device,
                _ =>
                {
                    serverRetired = true;
                    return Task.CompletedTask;
                },
                () => throw new StudioOperationContextChangedException(
                    "source_retire")));

        Assert.True(serverRetired);
        Assert.Contains(registry, project.Cloud.SharedSources);
        Assert.Contains(source, project.Sources);
        Assert.Contains(
            claim,
            project.Cloud.PendingAlbumComponentClaims);
    }

    [Fact]
    public async Task TimeoutAfterCommit_IsSafelyRetriedBeforeLocalRemoval()
    {
        (ProjectWorkspace project, ProjectDesignSource source,
            ProjectCloudSourceReference registry) = Fixture();
        ProjectLocalAlbumComponentClaim claim =
            StudioSourceRemovalOutbox.Stage(
                project,
                source,
                registry,
                Owner,
                Device,
                hasVerifiedPayload: true);
        bool serverCommitted = false;

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            StudioSourceRemovalOutbox.ConfirmRegistryRetirementAsync(
                project,
                claim,
                Owner,
                Device,
                _ =>
                {
                    serverCommitted = true;
                    throw new TaskCanceledException("ambiguous timeout");
                }));
        Assert.Contains(registry, project.Cloud.SharedSources);
        Assert.Contains(source, project.Sources);

        ProjectDesignSource? confirmed =
            await StudioSourceRemovalOutbox.ConfirmRegistryRetirementAsync(
                project,
                claim,
                Owner,
                Device,
                _ =>
                {
                    Assert.True(serverCommitted);
                    return Task.CompletedTask;
                });

        Assert.Same(source, confirmed);
        Assert.DoesNotContain(registry, project.Cloud.SharedSources);
        Assert.Single(StudioSourceRemovalOutbox.Pending(
            project,
            Owner,
            Device));
    }

    private static (
        ProjectWorkspace Project,
        ProjectDesignSource Source,
        ProjectCloudSourceReference Registry) Fixture()
    {
        var project = new ProjectWorkspace
        {
            Cloud =
            {
                Origin = ProjectOrigins.Cloud,
                ServerProjectId = "project-1",
                SharedSources = [],
            },
        };
        var source = new ProjectDesignSource { Id = "local-source" };
        ProjectCloudSyncMetadata.BindToCloudSource(
            project,
            source,
            SourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, Owner);
        StudioLocalSourceBindingPolicy.Bind(source, Owner, Device);
        project.Sources.Add(source);
        var registry = new ProjectCloudSourceReference
        {
            SourceId = SourceId,
            SourceKey = SourceKey,
            RegisteredBy = Owner,
            OwnerEmail = Owner,
            Status = "Active",
        };
        project.Cloud.SharedSources.Add(registry);
        return (project, source, registry);
    }
}
