using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using ErkS.Studio;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Security.Cryptography;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAuxiliarySourceLocalityPolicyTests : IDisposable
{
    private const string Owner = "owner@erks.local";
    private const string Teammate = "teammate@erks.local";
    private const string DeviceOne = "device-one";
    private const string DeviceTwo = "device-two";

    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-studio-aux-source-locality-tests",
        Guid.NewGuid().ToString("N"));

    public StudioAuxiliarySourceLocalityPolicyTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void CloudDocument_RequiresExactAccountDeviceAndVerifiedPayload()
    {
        ProjectWorkspace project = CloudProject();
        var document = new ProjectFileReference
        {
            CloudOwnerEmail = Owner,
            LocalBindingAccountEmail = Owner,
            LocalBindingDeviceFingerprint = DeviceOne,
        };

        Assert.True(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            Owner,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.False(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            Teammate,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.False(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            Owner,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.False(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            Owner,
            DeviceOne,
            hasVerifiedPayload: false));
    }

    [Fact]
    public void LegacyBlankCloudBinding_IsNotAdoptedUntilExplicitRelink()
    {
        ProjectWorkspace project = CloudProject();
        var document = new ProjectFileReference();

        Assert.False(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            Owner,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.True(StudioAuxiliarySourceLocalityPolicy.CanExplicitlyBind(
            project,
            document,
            Owner));

        StudioAuxiliarySourceLocalityPolicy.Bind(
            project,
            document,
            Owner,
            DeviceOne);

        Assert.Equal(Owner, document.CloudOwnerEmail);
        Assert.Equal(Owner, document.LocalBindingAccountEmail);
        Assert.Equal(DeviceOne, document.LocalBindingDeviceFingerprint);
        Assert.True(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            Owner,
            DeviceOne,
            hasVerifiedPayload: true));
    }

    [Fact]
    public void SameOwnerOtherDevice_RemainsCloudUntilExplicitRelink()
    {
        ProjectWorkspace project = CloudProject();
        var image = new ProjectVisualizationImage
        {
            OwnerProjectId = project.ProjectId,
            CloudOwnerEmail = Owner,
            LocalBindingAccountEmail = Owner,
            LocalBindingDeviceFingerprint = DeviceOne,
        };

        Assert.False(StudioAuxiliarySourceLocalityPolicy.IsLocalVisualizationImage(
            project,
            image,
            Owner,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.True(StudioAuxiliarySourceLocalityPolicy.CanExplicitlyBind(
            project,
            image,
            Owner));

        StudioAuxiliarySourceLocalityPolicy.Bind(
            project,
            image,
            Owner,
            DeviceTwo);

        Assert.True(StudioAuxiliarySourceLocalityPolicy.IsLocalVisualizationImage(
            project,
            image,
            Owner,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.False(StudioAuxiliarySourceLocalityPolicy.IsLocalVisualizationImage(
            project,
            image,
            Owner,
            DeviceOne,
            hasVerifiedPayload: true));
    }

    [Fact]
    public void VisualizationSnapshot_ContainsOnlyCurrentParticipantDevicePayload()
    {
        ProjectWorkspace project = CloudProject();
        project.Visualizations.ConfigureForProject(project.ProjectId);
        project.Visualizations.Images =
        [
            Image(project, "owner-local", Owner, DeviceOne),
            Image(project, "owner-other-device", Owner, DeviceTwo),
            Image(project, "teammate", Teammate, DeviceOne),
            new ProjectVisualizationImage
            {
                Id = "legacy-blank",
                OwnerProjectId = project.ProjectId,
            },
        ];

        ProjectVisualizationSource snapshot =
            StudioAuxiliarySourceLocalityPolicy.CreateLocalVisualizationSnapshot(
                project,
                Owner,
                DeviceOne,
                image => image.Id != "owner-local-missing");

        ProjectVisualizationImage selected = Assert.Single(snapshot.Images);
        Assert.Equal("owner-local", selected.Id);
        Assert.True(snapshot.IsConfigured);
    }

    [Fact]
    public void OfflineProject_PreservesLegacyUnboundAssets()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("LOCAL-001", "Local");
        var document = new ProjectFileReference();
        var image = new ProjectVisualizationImage
        {
            OwnerProjectId = project.ProjectId,
        };

        Assert.True(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            currentAccountEmail: null,
            currentDeviceFingerprint: null,
            hasVerifiedPayload: true));
        Assert.True(StudioAuxiliarySourceLocalityPolicy.IsLocalVisualizationImage(
            project,
            image,
            currentAccountEmail: null,
            currentDeviceFingerprint: null,
            hasVerifiedPayload: true));
    }

    [Fact]
    public void AppState_WatchersAndAlbumBuildFollowExactRuntimeAccountAndDevice()
    {
        ProjectWorkspace project = CloudProject();
        string ownerDocumentPath = WritePayload("owner-atd.pdf");
        string teammateDocumentPath = WritePayload("teammate-atd.pdf");
        string ownerImagePath = WritePayload("owner.png");
        string teammateImagePath = WritePayload("teammate.png");
        project.Foundation.PlanningTask.Documents =
        [
            Document("owner-atd", Owner, DeviceOne, ownerDocumentPath),
            Document("teammate-atd", Teammate, DeviceOne, teammateDocumentPath),
        ];
        project.Visualizations.ConfigureForProject(project.ProjectId);
        project.Visualizations.Images =
        [
            Image(project, "owner-image", Owner, DeviceOne, ownerImagePath),
            Image(project, "teammate-image", Teammate, DeviceOne, teammateImagePath),
        ];
        string projectPath = SaveProject(project);
        using var state = new AppState();
        state.ConfigureSourceRuntimeContext(Owner, DeviceOne);

        state.OpenProject(projectPath);

        Assert.Equal(
            [Path.GetFullPath(ownerDocumentPath), Path.GetFullPath(ownerImagePath)],
            state.WatchedAssetPathsSnapshot().Order().ToArray());
        AlbumProject ownerBuild = state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false);
        Assert.Equal("owner-atd", Assert.Single(ownerBuild.PlanningTask.Documents).Id);
        Assert.Equal("owner-image", Assert.Single(ownerBuild.Visualizations.Images).Id);

        state.ConfigureSourceRuntimeContext(Owner, DeviceTwo);

        Assert.Empty(state.WatchedAssetPathsSnapshot());
        Assert.Empty(state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false).PlanningTask.Documents);

        state.ConfigureSourceRuntimeContext(Teammate, DeviceOne);

        Assert.Equal(
            [Path.GetFullPath(teammateDocumentPath), Path.GetFullPath(teammateImagePath)],
            state.WatchedAssetPathsSnapshot().Order().ToArray());
        AlbumProject teammateBuild = state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false);
        Assert.Equal("teammate-atd", Assert.Single(teammateBuild.PlanningTask.Documents).Id);
        Assert.Equal("teammate-image", Assert.Single(teammateBuild.Visualizations.Images).Id);

        state.ConfigureSourceRuntimeContext("unrelated@erks.local", DeviceOne);

        Assert.Empty(state.WatchedAssetPathsSnapshot());
        AlbumProject unrelatedBuild = state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false);
        Assert.Empty(unrelatedBuild.PlanningTask.Documents);
        Assert.Empty(unrelatedBuild.Visualizations.Images);

        // A seated machine keeps working for its seat while someone signs in
        // with their own account. Without this, a person opening their own
        // project on an organization's machine silently stops it receiving.
        state.ConfigureSourceRuntimeContext(Owner, DeviceOne);
        state.ConfigureDeviceSeat(Owner);
        state.ConfigureSourceRuntimeContext(Teammate, DeviceOne);

        Assert.Equal(
            [Path.GetFullPath(ownerDocumentPath), Path.GetFullPath(ownerImagePath)],
            state.WatchedAssetPathsSnapshot().Order().ToArray());
        Assert.Equal(
            "owner-atd",
            Assert.Single(state.CreateAlbumBuildProject(
                reconcileLinkedProjectAssets: false).PlanningTask.Documents).Id);

        // Handing the seat back restores the ordinary rule.
        state.ConfigureDeviceSeat(null);

        Assert.Equal(
            [Path.GetFullPath(teammateDocumentPath), Path.GetFullPath(teammateImagePath)],
            state.WatchedAssetPathsSnapshot().Order().ToArray());
    }

    [Fact]
    public void TamperedOwnedPayload_IsNotLocalOrUploadable()
    {
        ProjectWorkspace project = CloudProject();
        string documentPath = WritePayload("verified-atd.bin");
        string imagePath = WritePayload("verified-image.bin");
        string documentHash = Sha256(documentPath);
        string imageHash = Sha256(imagePath);
        ProjectFileReference document =
            Document("verified-atd", Owner, DeviceOne, documentPath);
        document.Sha256 = documentHash;
        ProjectVisualizationImage image =
            Image(project, "verified-image", Owner, DeviceOne, imagePath);
        image.Sha256 = imageHash;
        project.Visualizations.ConfigureForProject(project.ProjectId);
        project.Visualizations.Images.Add(image);

        Assert.True(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
            Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName),
            document));
        Assert.True(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
            Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName),
            image));

        File.WriteAllBytes(documentPath, [9, 9, 9]);
        File.WriteAllBytes(imagePath, [8, 8, 8]);

        Assert.False(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
            Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName),
            document));
        Assert.False(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
            Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName),
            image));
        Assert.False(StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            project,
            document,
            Owner,
            DeviceOne,
            StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
                Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName),
                document)));
        Assert.Empty(
            StudioAuxiliarySourceLocalityPolicy.CreateLocalVisualizationSnapshot(
                project,
                Owner,
                DeviceOne,
                candidate => StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
                    Path.Combine(workDirectory, ProjectWorkspace.DefaultFileName),
                    candidate))
                .Images);
    }

    [Fact]
    public void CloudSyncPlanner_AdminCannotUploadAnotherAccountsPendingAuxiliaryComponent()
    {
        ProjectWorkspace project = CloudProject();
        project.Foundation.PlanningTask.Documents =
        [
            Document("owner-atd", Owner, DeviceOne, "owner-atd.pdf"),
        ];
        project.Visualizations.ConfigureForProject(project.ProjectId);
        project.Visualizations.Images =
        [
            Image(project, "owner-image", Owner, DeviceOne),
        ];
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
            ]);
        ConfigureAdmin(project, Teammate);

        CloudSyncPreviewPlan teammate = CloudSyncPreviewPlanner.Build(
            project,
            Teammate,
            "same-pc",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            hasVerifiedDocumentPayload: _ => true,
            hasVerifiedVisualizationPayload: _ => true);

        Assert.False(teammate.IsComponentAuthorized(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode));
        Assert.False(teammate.IsComponentAuthorized(
            ProjectCloudSyncMetadata.VisualizationsComponentCode));
        Assert.Equal(2, teammate.Blocked.Count(item =>
            item.Code.Equals(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            item.Code.Equals(
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
                StringComparison.OrdinalIgnoreCase)));

        ConfigureAdmin(project, Owner);
        CloudSyncPreviewPlan owner = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "owner-device",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            hasVerifiedDocumentPayload: _ => true,
            hasVerifiedVisualizationPayload: _ => true);
        CloudSyncPreviewPlan otherDevice = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "other-device",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceTwo,
            hasVerifiedDocumentPayload: _ => true,
            hasVerifiedVisualizationPayload: _ => true);
        CloudSyncPreviewPlan tamperedPayload = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "owner-device",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            hasVerifiedDocumentPayload: _ => false,
            hasVerifiedVisualizationPayload: _ => false);

        Assert.True(owner.IsComponentAuthorized(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode));
        Assert.True(owner.IsComponentAuthorized(
            ProjectCloudSyncMetadata.VisualizationsComponentCode));
        Assert.False(otherDevice.IsComponentAuthorized(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode));
        Assert.False(otherDevice.IsComponentAuthorized(
            ProjectCloudSyncMetadata.VisualizationsComponentCode));
        Assert.False(tamperedPayload.IsComponentAuthorized(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode));
        Assert.False(tamperedPayload.IsComponentAuthorized(
            ProjectCloudSyncMetadata.VisualizationsComponentCode));
    }

    [Fact]
    public void LastAtdRemoval_IsAuthorizedOnlyForClaimingAccountAndDevice()
    {
        ProjectWorkspace project = CloudProject();
        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
            Owner,
            DeviceOne,
            isRemoval: true,
            claimedAtUtc: DateTimeOffset.Parse("2026-07-29T00:00:00Z"));
        ConfigureAdmin(project, Owner);

        CloudSyncPreviewPlan claimingDevice = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "claiming-device",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne);
        CloudSyncPreviewPlan sameOwnerOtherDevice = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "other-device",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceTwo);
        ConfigureAdmin(project, Teammate);
        CloudSyncPreviewPlan teammateSamePc = CloudSyncPreviewPlanner.Build(
            project,
            Teammate,
            "same-pc",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne);

        Assert.True(claimingDevice.IsComponentAuthorized(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode));
        Assert.False(sameOwnerOtherDevice.IsComponentAuthorized(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode));
        Assert.False(teammateSamePc.IsComponentAuthorized(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode));
    }

    [Fact]
    public void ScopedClaimAcknowledgement_DoesNotClearAnotherParticipantsPendingChange()
    {
        ProjectWorkspace project = CloudProject();
        string code = ProjectCloudSyncMetadata.VisualizationsComponentCode;
        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            code,
            Owner,
            DeviceOne,
            isRemoval: true);
        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            code,
            Teammate,
            DeviceOne,
            isRemoval: false);

        ProjectCloudSyncMetadata.MarkAlbumComponentsSyncedForBinding(
            project,
            [code],
            Teammate,
            DeviceOne);

        Assert.Contains(code, ProjectCloudSyncMetadata.PendingAlbumComponents(project));
        ProjectLocalAlbumComponentClaim remaining = Assert.Single(
            project.Cloud.PendingAlbumComponentClaims);
        Assert.Equal(Owner, remaining.OwnerEmail);
        Assert.True(remaining.IsRemoval);

        ProjectCloudSyncMetadata.MarkAlbumComponentsSyncedForBinding(
            project,
            [code],
            Owner,
            DeviceOne);

        Assert.DoesNotContain(
            code,
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
        Assert.Empty(project.Cloud.PendingAlbumComponentClaims);
    }

    [Fact]
    public void ScopedClaimAcknowledgement_ClearsOnlyCapturedMutationToken()
    {
        ProjectWorkspace project = CloudProject();
        string code = ProjectCloudSyncMetadata.VisualizationsComponentCode;
        ConfigureAdmin(project, Owner);
        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            code,
            Owner,
            DeviceOne,
            isRemoval: true,
            claimedAtUtc: DateTimeOffset.Parse("2026-07-29T00:00:00Z"));
        CloudSyncPreviewPlan firstPlan = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "device-one",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne);
        ProjectAlbumComponentClaimAcknowledgement first =
            Assert.Single(firstPlan.ComponentClaimAcknowledgements([code]));

        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            code,
            Owner,
            DeviceOne,
            isRemoval: true,
            claimedAtUtc: DateTimeOffset.Parse("2026-07-29T00:01:00Z"));
        CloudSyncPreviewPlan secondPlan = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "device-one",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne);
        ProjectAlbumComponentClaimAcknowledgement second =
            Assert.Single(secondPlan.ComponentClaimAcknowledgements([code]));

        Assert.NotEqual(first.ClaimToken, second.ClaimToken);
        Assert.False(firstPlan.HasCompatibleComponentClaim(code, secondPlan));

        ProjectCloudSyncMetadata.MarkAlbumComponentsSyncedForBinding(
            project,
            [code],
            Owner,
            DeviceOne,
            [first]);

        ProjectLocalAlbumComponentClaim pending = Assert.Single(
            project.Cloud.PendingAlbumComponentClaims);
        Assert.Equal(second.ClaimToken, pending.ClaimToken);
        Assert.Contains(code, ProjectCloudSyncMetadata.PendingAlbumComponents(project));

        ProjectCloudSyncMetadata.MarkAlbumComponentsSyncedForBinding(
            project,
            [code],
            Owner,
            DeviceOne,
            [second]);

        Assert.Empty(project.Cloud.PendingAlbumComponentClaims);
        Assert.DoesNotContain(
            code,
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    [Fact]
    public void LinkedAtdAndVisualizationReconciliation_CreatesScopedRemovalClaims()
    {
        ProjectWorkspace project = CloudProject();
        string atdPath = WritePdf("watched-atd.pdf", pageCount: 1);
        string imagePath = WriteTinyPng("watched-image.png");
        project.Foundation.PlanningTask.Documents =
        [
            Document("watched-atd", Owner, DeviceOne, atdPath),
        ];
        project.Visualizations.ConfigureForProject(project.ProjectId);
        project.Visualizations.Images =
        [
            Image(project, "watched-image", Owner, DeviceOne, imagePath),
        ];
        string projectPath = SaveProject(project);
        using var state = new AppState();
        state.ConfigureSourceRuntimeContext(Owner, DeviceOne);
        state.OpenProject(projectPath);
        Assert.True(state.ReconcileProjectAssetSources().Changed);
        ProjectCloudSyncMetadata.MarkAlbumComponentsSynced(
            state.Project,
            [
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
            ]);

        File.Delete(atdPath);
        File.Delete(imagePath);
        ProjectAssetSourceReconciliationResult missing =
            state.ReconcileProjectAssetSources();

        // The two events are not the same and must not be reported the same.
        // An approved planning task whose source is gone leaves the album, and
        // the cloud is told so. A visualization whose watched original is gone
        // keeps its own copy, so nothing was removed and nothing is claimed as
        // removed — only the link is marked broken.
        Assert.Equal(1, missing.MissingDocumentCount);
        Assert.Equal(0, missing.MissingVisualizationCount);
        Assert.Equal(1, missing.BrokenLinkCount);
        ProjectLocalAlbumComponentClaim atdClaim = Assert.Single(
            state.Project.Cloud.PendingAlbumComponentClaims,
            claim => claim.ComponentCode.Equals(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StringComparison.OrdinalIgnoreCase));
        Assert.True(atdClaim.IsRemoval);
        Assert.DoesNotContain(
            state.Project.Cloud.PendingAlbumComponentClaims,
            claim => claim.IsRemoval &&
                claim.ComponentCode.Equals(
                    ProjectCloudSyncMetadata.VisualizationsComponentCode,
                    StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Owner, atdClaim.OwnerEmail);
        Assert.Equal(DeviceOne, atdClaim.DeviceFingerprint);

        ProjectCloudSyncMetadata.MarkAlbumComponentsSynced(
            state.Project,
            [
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
            ]);
        WritePdf("watched-atd.pdf", pageCount: 2);
        WriteTinyPng("watched-image.png");

        ProjectAssetSourceReconciliationResult restored =
            state.ReconcileProjectAssetSources();

        Assert.Equal(1, restored.RestoredDocumentCount);
        // The visualization never left, so there is nothing to restore — the
        // broken-link mark is what clears.
        Assert.Equal(0, restored.RestoredVisualizationCount);
        Assert.All(
            state.Project.Visualizations.ImagesForProject(state.Project.ProjectId),
            image => Assert.False(image.LinkedSourceMissing));
        Assert.All(
            state.Project.Cloud.PendingAlbumComponentClaims,
            claim => Assert.False(claim.IsRemoval));
    }

    [Fact]
    public void CompanyDocumentReconciliation_DoesNotDirtyAtdComponent()
    {
        ProjectWorkspace project = CloudProject();
        string companyDocumentPath = WritePdf("company-certificate.pdf", pageCount: 1);
        ProjectFileReference companyDocument = Document(
            "company-certificate",
            Owner,
            DeviceOne,
            companyDocumentPath);
        companyDocument.Category =
            ProjectDocumentCategories.CompanyRegistrationCertificate;
        project.Foundation.DesignCompany.OrganizationSnapshot
            .RegistrationCertificateDocuments = [companyDocument];
        string projectPath = SaveProject(project);
        using var state = new AppState();
        state.ConfigureSourceRuntimeContext(Owner, DeviceOne);
        state.OpenProject(projectPath);

        ProjectAssetSourceReconciliationResult result =
            state.ReconcileProjectAssetSources();

        Assert.Contains(
            ProjectDocumentCategories.CompanyRegistrationCertificate,
            result.ChangedDocumentCategories);
        Assert.DoesNotContain(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project));
        Assert.Empty(state.Project.Cloud.PendingAlbumComponentClaims);
    }

    [Fact]
    public void UnrelatedFoundationDirty_DoesNotMarkAtdComponentPending()
    {
        ProjectWorkspace project = CloudProject();
        string projectPath = SaveProject(project);
        using var state = new AppState();
        state.ConfigureSourceRuntimeContext(Owner, DeviceOne);
        state.OpenProject(projectPath);

        state.MarkFoundationContentChanged();

        Assert.DoesNotContain(
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project));
        Assert.Contains(
            ProjectCloudSyncMetadata.CoverComponentCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project));
    }

    [Fact]
    public void BindingAndRemovalClaim_RoundTripWithoutLegacyAdoption()
    {
        ProjectWorkspace project = CloudProject();
        var document = new ProjectFileReference
        {
            Id = "bound-document",
            Category = ProjectDocumentCategories.ApprovedPlanningTask,
        };
        var image = new ProjectVisualizationImage
        {
            Id = "bound-image",
            OwnerProjectId = project.ProjectId,
        };
        StudioAuxiliarySourceLocalityPolicy.Bind(
            project,
            document,
            Owner,
            DeviceOne);
        StudioAuxiliarySourceLocalityPolicy.Bind(
            project,
            image,
            Owner,
            DeviceOne);
        project.Foundation.PlanningTask.Documents.Add(document);
        project.Visualizations.ConfigureForProject(project.ProjectId);
        project.Visualizations.Images.Add(image);
        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
            Owner,
            DeviceOne,
            isRemoval: true);
        string projectPath = SaveProject(project);

        ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

        ProjectFileReference loadedDocument = Assert.Single(
            loaded.Foundation.PlanningTask.Documents);
        ProjectVisualizationImage loadedImage = Assert.Single(
            loaded.Visualizations.Images);
        Assert.Equal(Owner, loadedDocument.LocalBindingAccountEmail);
        Assert.Equal(DeviceOne, loadedDocument.LocalBindingDeviceFingerprint);
        Assert.Equal(Owner, loadedImage.CloudOwnerEmail);
        Assert.Equal(DeviceOne, loadedImage.LocalBindingDeviceFingerprint);
        ProjectLocalAlbumComponentClaim claim = Assert.Single(
            loaded.Cloud.PendingAlbumComponentClaims);
        Assert.Equal(Owner, claim.OwnerEmail);
        Assert.Equal(DeviceOne, claim.DeviceFingerprint);
        Assert.False(string.IsNullOrWhiteSpace(claim.ClaimToken));
        Assert.True(claim.IsRemoval);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private static ProjectWorkspace CloudProject()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("CLOUD-001", "Cloud");
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project";
        project.Cloud.ServerUrl = "https://cloud.erks.local";
        return project;
    }

    private static ProjectVisualizationImage Image(
        ProjectWorkspace project,
        string id,
        string owner,
        string device,
        string linkedSourcePath = "") => new()
        {
            Id = id,
            OwnerProjectId = project.ProjectId,
            CloudOwnerEmail = owner,
            LocalBindingAccountEmail = owner,
            LocalBindingDeviceFingerprint = device,
            LinkedSourcePath = linkedSourcePath,
            IsAvailable = true,
            IsIncludedInAlbum = true,
        };

    private static ProjectFileReference Document(
        string id,
        string owner,
        string device,
        string linkedSourcePath) => new()
        {
            Id = id,
            Category = ProjectDocumentCategories.ApprovedPlanningTask,
            CloudOwnerEmail = owner,
            LocalBindingAccountEmail = owner,
            LocalBindingDeviceFingerprint = device,
            LinkedSourcePath = linkedSourcePath,
            IsAvailable = true,
        };

    private string WritePayload(string fileName)
    {
        string path = Path.Combine(workDirectory, fileName);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private string WritePdf(string fileName, int pageCount)
    {
        string path = Path.Combine(workDirectory, fileName);
        if (File.Exists(path))
            File.Delete(path);
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(297);
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(
                new XPen(XColors.Black, 0.5),
                20,
                20,
                100 + index,
                40);
        }
        document.Save(path);
        return path;
    }

    private string WriteTinyPng(string fileName)
    {
        string path = Path.Combine(workDirectory, fileName);
        File.WriteAllBytes(
            path,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        return path;
    }

    private string SaveProject(ProjectWorkspace project)
    {
        string projectPath = Path.Combine(
            workDirectory,
            "project",
            ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(project, projectPath);
        string albumPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            project.PrimaryAlbum.DocumentPath);
        StudioAlbumDocumentStore.Save(new StudioAlbumDocument
        {
            ProjectId = project.ProjectId,
            AlbumId = project.PrimaryAlbum.Id,
            Definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition(
                project.PrimaryAlbum.Title),
        }, albumPath);
        return projectPath;
    }

    private static void ConfigureAdmin(
        ProjectWorkspace project,
        string accountEmail)
    {
        project.Cloud.PermissionSnapshotAccountEmail = accountEmail;
        project.Cloud.CurrentUserRoles = ["ProjectAdmin"];
        project.Cloud.CurrentUserScopes = ["concept.write"];
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
