using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectMultiAdminSyncReliabilityTests
{
    [Fact]
    public void SameBaseTwoAdminEdits_FirstAcceptedSnapshotDoesNotEraseSecondPendingEdit()
    {
        ProjectServerSnapshot sharedBase = Snapshot("Base name", "base-token");
        ProjectWorkspace firstAdmin = new();
        ProjectWorkspace secondAdmin = new();
        ProjectCanonicalSyncService.Apply(firstAdmin, sharedBase);
        ProjectCanonicalSyncService.Apply(secondAdmin, sharedBase);

        var firstPending = new PendingProjectInformationUpdate
        {
            Name = "First admin name",
            Location = "First admin address",
            BuildingPurpose = "First admin purpose",
            BaseConcurrencyToken = sharedBase.ConcurrencyToken,
            QueuedAtUtc = new DateTimeOffset(2026, 7, 29, 1, 0, 0, TimeSpan.Zero),
        };
        var secondPending = new PendingProjectInformationUpdate
        {
            Name = "Second admin name",
            Location = "Second admin address",
            BuildingPurpose = "Second admin purpose",
            BaseConcurrencyToken = sharedBase.ConcurrencyToken,
            QueuedAtUtc = new DateTimeOffset(2026, 7, 29, 1, 0, 1, TimeSpan.Zero),
        };
        firstAdmin.Cloud.PendingProjectInformation = firstPending;
        secondAdmin.Cloud.PendingProjectInformation = secondPending;
        ProjectCanonicalSyncService.Apply(firstAdmin, sharedBase);
        ProjectCanonicalSyncService.Apply(secondAdmin, sharedBase);

        ProjectServerSnapshot firstAccepted =
            Snapshot("First admin name", "winner-token");
        firstAccepted.Information.Location = firstPending.Location;
        firstAccepted.Information.BuildingPurpose = firstPending.BuildingPurpose;
        firstAdmin.Cloud.PendingProjectInformation = null;
        ProjectCanonicalSyncService.Apply(firstAdmin, firstAccepted);

        ProjectCanonicalSyncService.Apply(secondAdmin, firstAccepted);
        ProjectCloudSyncMetadata.MarkConflict(
            secondAdmin,
            secondPending,
            firstAccepted.ConcurrencyToken,
            "The shared base is stale.");

        Assert.Equal("First admin name", firstAdmin.Identity.Name);
        Assert.Equal("winner-token", firstAdmin.Cloud.ServerSnapshot.ConcurrencyToken);
        Assert.Equal("First admin name", secondAdmin.Identity.Name);
        Assert.Equal("First admin address", secondAdmin.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("First admin name", secondAdmin.Cloud.ServerSnapshot.Name);
        Assert.Equal("winner-token", secondAdmin.Cloud.ServerSnapshot.ConcurrencyToken);
        Assert.Same(secondPending, secondAdmin.Cloud.PendingProjectInformation);
        Assert.Equal("Second admin name", secondAdmin.Cloud.PendingProjectInformation.Name);
        Assert.Equal("base-token", secondAdmin.Cloud.PendingProjectInformation.BaseConcurrencyToken);
        Assert.Equal(ProjectSyncStatuses.Conflict, secondAdmin.Cloud.SyncStatus);
        Assert.Null(secondAdmin.Cloud.LastSyncedAtUtc);
    }

    [Fact]
    public void CanonicalMetadataAuthority_AllowsCreatorScopeAndAdminsButDeniesParticipantRoles()
    {
        var creator = new ProjectCloudLink
        {
            PermissionSnapshotAccountEmail = "creator@example.com",
            CurrentUserRoles = ["ProjectCreator"],
            CurrentUserScopes = ["project.metadata.write"],
        };
        var projectAdmin = new ProjectCloudLink
        {
            PermissionSnapshotAccountEmail = "admin@example.com",
            CurrentUserRoles = ["ProjectAdmin"],
        };
        var designAdmin = new ProjectCloudLink
        {
            PermissionSnapshotAccountEmail = "design@example.com",
            CurrentUserRoles = ["DesignCompanyAdmin"],
        };

        Assert.True(ProjectCloudSyncAuthority.CanManageCanonicalMetadata(
            creator,
            "creator@example.com"));
        Assert.True(ProjectCloudSyncAuthority.CanManageCanonicalMetadata(
            projectAdmin,
            "admin@example.com"));
        Assert.True(ProjectCloudSyncAuthority.CanManageCanonicalMetadata(
            designAdmin,
            "design@example.com"));

        foreach (string participantRole in new[]
                 {
                     "Architect",
                     "Client",
                     "AuthoritySpecialist",
                     "ChiefArchitect",
                 })
        {
            Assert.False(ProjectCloudSyncAuthority.CanManageCanonicalMetadata(
                new ProjectCloudLink
                {
                    PermissionSnapshotAccountEmail = "participant@example.com",
                    CurrentUserRoles = [participantRole],
                    CurrentUserScopes = ["concept.read", "concept.write"],
                },
                "participant@example.com"));
        }
    }

    private static ProjectServerSnapshot Snapshot(
        string name,
        string concurrencyToken) => new()
    {
        ProjectId = "project-1",
        ProjectCode = "ERKS-001",
        Name = name,
        ConcurrencyToken = concurrencyToken,
        Information = new ProjectServerInformation
        {
            ProjectId = "project-1",
            ProjectCode = "ERKS-001",
            Name = name,
            Location = "Base address",
            BuildingPurpose = "Base purpose",
        },
    };
}
