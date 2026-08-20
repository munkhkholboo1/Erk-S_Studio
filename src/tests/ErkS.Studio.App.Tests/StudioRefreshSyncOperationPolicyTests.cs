using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioRefreshSyncOperationPolicyTests
{
    [Fact]
    public void SourceRefresh_AttemptsCloudUnionSoForeignComponentsRemainVisible()
    {
        Assert.True(StudioRefreshSyncOperationPolicy.ShouldAttemptCloudUnionPreview(
            StudioWorkspaceOperation.SourceRefresh));
        Assert.True(StudioRefreshSyncOperationPolicy.ShouldAttemptCloudUnionPreview(
            StudioWorkspaceOperation.ExplicitAlbumEdit));
        Assert.False(StudioRefreshSyncOperationPolicy.ShouldAttemptCloudUnionPreview(
            StudioWorkspaceOperation.CloudSync));
    }

    [Fact]
    public void SourceRefresh_WithoutUsableCloudUnionDefersLocalOnlyAlbumReplacement()
    {
        Assert.True(
            StudioRefreshSyncOperationPolicy.ShouldDeferLocalAlbumReplacement(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                cloudUnionBuilt: false,
                cloudOnlySourceComponentCount: 1));
        Assert.False(
            StudioRefreshSyncOperationPolicy.ShouldDeferLocalAlbumReplacement(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                cloudUnionBuilt: true,
                cloudOnlySourceComponentCount: 1));
        Assert.False(
            StudioRefreshSyncOperationPolicy.ShouldDeferLocalAlbumReplacement(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                cloudUnionBuilt: false,
                cloudOnlySourceComponentCount: 0));
        Assert.False(
            StudioRefreshSyncOperationPolicy.ShouldDeferLocalAlbumReplacement(
                StudioWorkspaceOperation.ExplicitAlbumEdit,
                isCloudLinked: true,
                cloudUnionBuilt: false,
                cloudOnlySourceComponentCount: 1));
    }

    [Fact]
    public void CloudUnionPreviewFailure_FallsBackToLocalBuildForLocalOperations()
    {
        var failure = new NullReferenceException();
        Assert.True(StudioRefreshSyncOperationPolicy.ShouldFallbackToLocalAlbumBuild(
            StudioWorkspaceOperation.SourceRefresh,
            failure));
        Assert.True(StudioRefreshSyncOperationPolicy.ShouldFallbackToLocalAlbumBuild(
            StudioWorkspaceOperation.ExplicitAlbumEdit,
            failure));
        Assert.False(StudioRefreshSyncOperationPolicy.ShouldFallbackToLocalAlbumBuild(
            StudioWorkspaceOperation.CloudSync,
            failure));
    }

    [Fact]
    public void WrappedCloudUnionPreviewFailure_FallsBackToValidLocalBuild()
    {
        var failure = new AlbumBuildException(
            ["Object reference not set to an instance of an object."],
            new NullReferenceException());

        Assert.True(StudioRefreshSyncOperationPolicy.ShouldFallbackToLocalAlbumBuild(
            StudioWorkspaceOperation.SourceRefresh,
            failure));
    }

    [Fact]
    public void CloudUnionBuildRejectionWithoutInnerException_FallsBackToLocalBuild()
    {
        var failure = new AlbumBuildException(
            ["Object reference not set to an instance of an object."]);

        Assert.True(StudioRefreshSyncOperationPolicy.ShouldFallbackToLocalAlbumBuild(
            StudioWorkspaceOperation.SourceRefresh,
            failure));
    }

    [Fact]
    public void SourceRefreshAndCloudSync_AreMutuallyExclusive()
    {
        Assert.False(StudioRefreshSyncOperationPolicy.CanStartSourceRefresh(
            hasOpenProject: true,
            canEditProjectContent: true,
            projectAccessRefreshInProgress: false,
            sourceRefreshInProgress: false,
            cloudSyncInProgress: true));
        Assert.False(StudioRefreshSyncOperationPolicy.CanStartCloudSync(
            hasOpenProject: true,
            projectAccessRefreshInProgress: false,
            sourceRefreshInProgress: true,
            cloudSyncInProgress: false));
    }

    [Fact]
    public void IdleWorkspace_AllowsEachOperation()
    {
        Assert.True(StudioRefreshSyncOperationPolicy.CanStartSourceRefresh(
            hasOpenProject: true,
            canEditProjectContent: true,
            projectAccessRefreshInProgress: false,
            sourceRefreshInProgress: false,
            cloudSyncInProgress: false));
        Assert.True(StudioRefreshSyncOperationPolicy.CanStartCloudSync(
            hasOpenProject: true,
            projectAccessRefreshInProgress: false,
            sourceRefreshInProgress: false,
            cloudSyncInProgress: false));
    }

    [Fact]
    public void CloudSyncUi_RemainsBusyUntilSourceCallbacksComplete()
    {
        Assert.True(StudioRefreshSyncOperationPolicy.IsBusy(
            projectAccessRefreshInProgress: false,
            sourceRefreshInProgress: true,
            cloudSyncInProgress: false));
        Assert.False(StudioRefreshSyncOperationPolicy.IsBusy(
            projectAccessRefreshInProgress: false,
            sourceRefreshInProgress: false,
            cloudSyncInProgress: false));
    }

    [Fact]
    public void SourceRefresh_RejectsAccessRefreshAndStaleDispatcherContinuation()
    {
        Assert.False(StudioRefreshSyncOperationPolicy.CanStartSourceRefresh(
            hasOpenProject: true,
            canEditProjectContent: true,
            projectAccessRefreshInProgress: true,
            sourceRefreshInProgress: false,
            cloudSyncInProgress: false));

        ProjectWorkspace project = Project("project-a", "server-project");
        StudioAccountSession account = Account("owner@example.com");
        StudioOperationContext context = StudioOperationContext.Capture(
            true,
            project,
            @"C:\projects\a\project.erkstudio",
            account,
            workspaceEpoch: 3,
            accountEpoch: 7);

        Assert.True(StudioRefreshSyncOperationPolicy.CanContinueSourceRefresh(
            context,
            projectAccessRefreshInProgress: false,
            hasOpenProject: true,
            project,
            @"C:\projects\a\project.erkstudio",
            account,
            workspaceEpoch: 3,
            accountEpoch: 7));
        Assert.False(StudioRefreshSyncOperationPolicy.CanContinueSourceRefresh(
            context,
            projectAccessRefreshInProgress: true,
            hasOpenProject: true,
            project,
            @"C:\projects\a\project.erkstudio",
            account,
            workspaceEpoch: 3,
            accountEpoch: 7));
        Assert.False(StudioRefreshSyncOperationPolicy.CanContinueSourceRefresh(
            context,
            projectAccessRefreshInProgress: false,
            hasOpenProject: true,
            project,
            @"C:\projects\a\project.erkstudio",
            Account("owner@example.com"),
            workspaceEpoch: 3,
            accountEpoch: 8));
        Assert.False(StudioRefreshSyncOperationPolicy.CanContinueSourceRefresh(
            context,
            projectAccessRefreshInProgress: false,
            hasOpenProject: true,
            Project("project-b", "server-project"),
            @"C:\projects\b\project.erkstudio",
            account,
            workspaceEpoch: 4,
            accountEpoch: 7));
    }

    [Fact]
    public void SourceRefreshBuild_DoesNotCollectProjectOrOrganizationUi()
    {
        Assert.False(StudioRefreshSyncOperationPolicy.ShouldCollectProjectUi(
            StudioWorkspaceOperation.SourceRefresh));
        Assert.False(StudioRefreshSyncOperationPolicy.ShouldCollectProjectUi(
            StudioWorkspaceOperation.LocalPdfPageEdit));
        Assert.True(StudioRefreshSyncOperationPolicy.ShouldCollectProjectUi(
            StudioWorkspaceOperation.ExplicitAlbumEdit));
        Assert.False(StudioRefreshSyncOperationPolicy.ShouldCollectProjectUi(
            StudioWorkspaceOperation.CloudSync));
    }

    [Fact]
    public void CloudSyncPayload_ExcludesCanonicalProjectAndOrganizationMirrors()
    {
        Assert.False(StudioRefreshSyncOperationPolicy.CanUploadPersistedPayload(
            StudioCloudSyncPayload.ProjectInformation));
        Assert.False(StudioRefreshSyncOperationPolicy.CanUploadPersistedPayload(
            StudioCloudSyncPayload.OrganizationAssignment));
        Assert.True(StudioRefreshSyncOperationPolicy.CanUploadPersistedPayload(
            StudioCloudSyncPayload.SourcePackage));
        Assert.True(StudioRefreshSyncOperationPolicy.CanUploadPersistedPayload(
            StudioCloudSyncPayload.AlbumComponent));
        Assert.True(StudioRefreshSyncOperationPolicy.CanUploadPersistedPayload(
            StudioCloudSyncPayload.BuildingComposition));
    }

    private static ProjectWorkspace Project(
        string projectId,
        string serverProjectId) => new()
    {
        ProjectId = projectId,
        Cloud =
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = serverProjectId,
        },
    };

    private static StudioAccountSession Account(string email) => new(
        "https://cloud.example",
        email,
        email,
        "",
        "",
        "",
        "Dev",
        DateTimeOffset.UtcNow.AddDays(1),
        DateTimeOffset.UtcNow.AddHours(1),
        "token");
}
