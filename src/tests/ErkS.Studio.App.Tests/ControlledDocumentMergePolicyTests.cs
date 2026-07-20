using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class ControlledDocumentMergePolicyTests
{
    [Fact]
    public void PendingLocalFileUploadsWhenItsCloudBaseIsUnchanged()
    {
        PlanningTaskInformation local = Local("local-hash", serverVersion: 4, pending: true);
        StudioCloudControlledDocument cloud = Cloud("cloud-hash", version: 4);

        ControlledDocumentMergeDecision decision = ControlledDocumentMergePolicy.Decide(local, cloud);

        Assert.Equal(ControlledDocumentMergeAction.UploadLocal, decision.Action);
    }

    [Fact]
    public void PendingLocalFileConflictsWhenAnotherMemberChangedTheCloudVersion()
    {
        PlanningTaskInformation local = Local("local-hash", serverVersion: 4, pending: true);
        StudioCloudControlledDocument cloud = Cloud("cloud-hash", version: 5);

        ControlledDocumentMergeDecision decision = ControlledDocumentMergePolicy.Decide(local, cloud);

        Assert.Equal(ControlledDocumentMergeAction.Conflict, decision.Action);
    }

    [Fact]
    public void CleanLocalFileDownloadsAChangedCloudCurrentSet()
    {
        PlanningTaskInformation local = Local("old-hash", serverVersion: 4, pending: false);
        StudioCloudControlledDocument cloud = Cloud("new-hash", version: 5);

        ControlledDocumentMergeDecision decision = ControlledDocumentMergePolicy.Decide(local, cloud);

        Assert.Equal(ControlledDocumentMergeAction.DownloadCloud, decision.Action);
    }

    [Fact]
    public void EqualCurrentSetsDoNothing()
    {
        PlanningTaskInformation local = Local("same-hash", serverVersion: 5, pending: false);
        StudioCloudControlledDocument cloud = Cloud("same-hash", version: 5);

        ControlledDocumentMergeDecision decision = ControlledDocumentMergePolicy.Decide(local, cloud);

        Assert.Equal(ControlledDocumentMergeAction.None, decision.Action);
    }

    private static PlanningTaskInformation Local(string hash, int serverVersion, bool pending) => new()
    {
        ServerDocumentVersion = serverVersion,
        DocumentCloudSyncStatus = pending
            ? ProjectDocumentCloudSyncStatuses.PendingUpload
            : ProjectDocumentCloudSyncStatuses.Synced,
        Documents =
        [
            new ProjectFileReference
            {
                Category = ProjectDocumentCategories.ApprovedPlanningTask,
                IsAvailable = true,
                Sha256 = hash,
                ServerFileRevisionId = "server-file-revision",
                CloudSyncStatus = pending
                    ? ProjectDocumentCloudSyncStatuses.PendingUpload
                    : ProjectDocumentCloudSyncStatuses.Synced,
            },
        ],
    };

    private static StudioCloudControlledDocument Cloud(string hash, int version) => new()
    {
        Version = version,
        CurrentFiles =
        [
            new StudioCloudFile
            {
                FileRevisionId = "cloud-file-revision",
                Sha256 = hash,
            },
        ],
    };
}
