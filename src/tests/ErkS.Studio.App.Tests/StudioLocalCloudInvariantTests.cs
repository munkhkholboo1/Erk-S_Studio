using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioLocalCloudInvariantTests
{
    private const string OwnerA = "owner-a@erks.local";
    private const string TeammateB = "teammate-b@erks.local";
    private const string Unrelated = "unrelated@erks.local";
    private const string DeviceOne = "device-fingerprint-1";
    private const string DeviceTwo = "device-fingerprint-2";
    private const string SourceKey = "shared-source";

    [Fact]
    public void SameMachineAccountSwitch_TeammateBCannotUploadReplaceOrRemoveOwnerASource()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource(project, OwnerA);
        project.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];
        StudioLocalSourceBindingPolicy.Bind(source, OwnerA, DeviceOne);
        RecordPackage(project, source);
        string componentCode =
            StudioAlbumComponentIdentity.SourceCode(OwnerA, SourceKey);
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [componentCode]);

        ProjectSourceSyncCandidate candidate =
            Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));
        CloudSyncPreviewPlan ownerPlan = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "WORKSTATION · device-1",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            _ => true);
        CloudSyncPreviewPlan teammatePlan = CloudSyncPreviewPlanner.Build(
            project,
            TeammateB,
            "WORKSTATION · device-1",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            _ => true);

        Assert.True(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            OwnerA,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.True(ownerPlan.IsSourceAuthorized(candidate));
        Assert.True(ownerPlan.IsComponentAuthorized(componentCode));

        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            TeammateB,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.False(teammatePlan.IsSourceAuthorized(candidate));
        Assert.False(teammatePlan.IsComponentAuthorized(componentCode));
        Assert.False(StudioSourceRefreshScope.CanRefresh(
            project,
            source,
            TeammateB)); // replace
        Assert.False(StudioSourceRefreshScope.CanRefresh(
            project,
            source,
            TeammateB)); // remove
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(project, TeammateB));
        Assert.Single(StudioSourceRefreshScope.OwnedSources(
            project,
            OwnerA,
            DeviceOne,
            _ => true));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            project,
            TeammateB,
            DeviceOne,
            _ => true));
        Assert.Single(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            OwnerA,
            DeviceOne,
            _ => true));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            TeammateB,
            DeviceOne,
            _ => true));
    }

    [Fact]
    public void UnrelatedUserWithoutProjectAccess_CannotAdoptLocalOrRegisteredBySource()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource(project, OwnerA);
        project.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];
        StudioLocalSourceBindingPolicy.Bind(source, OwnerA, DeviceOne);
        RecordPackage(project, source);
        ProjectSourceSyncCandidate candidate =
            Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            Unrelated,
            "UNRELATED · device-1",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            _ => true);

        Assert.Empty(project.Cloud.CurrentUserRoles);
        Assert.Empty(project.Cloud.CurrentUserScopes);
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            Unrelated,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.False(StudioSourceRefreshScope.CanRefresh(
            project,
            source,
            Unrelated));
        Assert.False(plan.IsSourceAuthorized(candidate));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            project,
            Unrelated,
            DeviceOne,
            _ => true));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            Unrelated,
            DeviceOne,
            _ => true));
        StudioCloudSourcePackage cloudOnly = Assert.Single(
            StudioSharedSourceProjection.Create(project.Cloud.SharedSources));
        Assert.Equal(OwnerA, cloudOnly.RegisteredBy);
    }

    [Fact]
    public void OwnerASecondDeviceWithoutPayload_StaysCloudUntilExplicitRelink()
    {
        ProjectWorkspace firstDevice = CloudProject();
        ProjectDesignSource copiedSource = LocalSource(firstDevice, OwnerA);
        StudioLocalSourceBindingPolicy.Bind(copiedSource, OwnerA, DeviceOne);
        RecordPackage(firstDevice, copiedSource);

        ProjectWorkspace freshSecondDevice = CloudProject();
        freshSecondDevice.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];

        Assert.Empty(freshSecondDevice.Sources);
        Assert.Empty(ProjectCloudSyncMetadata.SourcePackages(freshSecondDevice));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            freshSecondDevice,
            OwnerA));
        Assert.Single(StudioSharedSourceProjection.Create(
            freshSecondDevice.Cloud.SharedSources));
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            copiedSource,
            OwnerA,
            DeviceTwo,
            hasVerifiedPayload: false));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            firstDevice,
            OwnerA,
            DeviceTwo,
            _ => true));
        CloudSyncPreviewPlan secondDevicePlan = CloudSyncPreviewPlanner.Build(
            firstDevice,
            OwnerA,
            "SECOND-DEVICE",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceTwo,
            _ => true);
        ProjectSourceSyncCandidate candidate =
            Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(
                firstDevice));
        Assert.False(secondDevicePlan.IsSourceAuthorized(candidate));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            firstDevice,
            [candidate],
            OwnerA,
            DeviceTwo,
            _ => true));

        Assert.True(StudioLocalSourceBindingPolicy.TryExplicitRelink(
            copiedSource,
            authorizedControllerEmail: OwnerA,
            currentAccountEmail: OwnerA,
            currentDeviceFingerprint: DeviceTwo,
            hasVerifiedPayload: true));
        Assert.True(StudioLocalSourceBindingPolicy.IsLocal(
            copiedSource,
            OwnerA,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.Single(StudioSourceRefreshScope.OwnedSources(
            firstDevice,
            OwnerA,
            DeviceTwo,
            _ => true));
        CloudSyncPreviewPlan relinkedPlan = CloudSyncPreviewPlanner.Build(
            firstDevice,
            OwnerA,
            "SECOND-DEVICE",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceTwo,
            _ => true);
        Assert.True(relinkedPlan.IsSourceAuthorized(candidate));
        Assert.Single(StudioSourceUploadScope.AuthorizedLocal(
            firstDevice,
            [candidate],
            OwnerA,
            DeviceTwo,
            _ => true));
    }

    [Fact]
    public void CopiedMirrorBinding_NeverMakesMissingOrDifferentDevicePayloadLocal()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource copiedSource = LocalSource(project, OwnerA);
        StudioLocalSourceBindingPolicy.Bind(copiedSource, OwnerA, DeviceOne);
        RecordPackage(project, copiedSource);

        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            copiedSource,
            OwnerA,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            copiedSource,
            OwnerA,
            DeviceOne,
            hasVerifiedPayload: false));
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            copiedSource,
            TeammateB,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            project,
            OwnerA,
            DeviceTwo,
            _ => true));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            project,
            OwnerA,
            DeviceOne,
            _ => false));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            project,
            TeammateB,
            DeviceOne,
            _ => true));
        ProjectSourceSyncCandidate candidate =
            Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            OwnerA,
            DeviceTwo,
            _ => true));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            OwnerA,
            DeviceOne,
            _ => false));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            TeammateB,
            DeviceOne,
            _ => true));
    }

    [Fact]
    public void CustodianTransferAlone_IsCloudUntilExplicitRelinkOnCustodianDevice()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource(project, OwnerA);
        StudioLocalSourceBindingPolicy.Bind(source, OwnerA, DeviceOne);
        project.Cloud.SharedSources =
        [
            SharedSource(OwnerA, TeammateB),
        ];
        RecordPackage(project, source);
        ProjectSourceSyncCandidate candidate =
            Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));

        Assert.True(StudioSourceRefreshScope.CanRefresh(
            project,
            source,
            TeammateB));
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            TeammateB,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            project,
            TeammateB,
            DeviceTwo,
            _ => true));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            TeammateB,
            DeviceTwo,
            _ => true));
        CloudSyncPreviewPlan transferredWithoutRelink =
            CloudSyncPreviewPlanner.Build(
                project,
                TeammateB,
                "CUSTODIAN-DEVICE",
                new StudioCloudProjectRefreshResult(false, null),
                DeviceTwo,
                _ => true);
        Assert.False(transferredWithoutRelink.IsSourceAuthorized(candidate));

        Assert.True(StudioLocalSourceBindingPolicy.TryExplicitRelink(
            source,
            authorizedControllerEmail: TeammateB,
            currentAccountEmail: TeammateB,
            currentDeviceFingerprint: DeviceTwo,
            hasVerifiedPayload: true));
        Assert.True(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            TeammateB,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            OwnerA,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.Single(StudioSourceRefreshScope.OwnedSources(
            project,
            TeammateB,
            DeviceTwo,
            _ => true));
        Assert.Single(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            TeammateB,
            DeviceTwo,
            _ => true));
        CloudSyncPreviewPlan transferredAndRelinked =
            CloudSyncPreviewPlanner.Build(
                project,
                TeammateB,
                "CUSTODIAN-DEVICE",
                new StudioCloudProjectRefreshResult(false, null),
                DeviceTwo,
                _ => true);
        Assert.True(transferredAndRelinked.IsSourceAuthorized(candidate));
    }

    [Fact]
    public void MissingLegacyBinding_RemainsCloudUntilExplicitRelink()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource legacy = LocalSource(project, OwnerA);
        project.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];
        RecordPackage(project, legacy);
        ProjectSourceSyncCandidate candidate =
            Assert.Single(ProjectCloudSyncMetadata.PendingSourcePackages(project));

        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            legacy,
            OwnerA,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.Empty(StudioSourceRefreshScope.OwnedSources(
            project,
            OwnerA,
            DeviceOne,
            _ => true));
        Assert.Empty(StudioSourceUploadScope.AuthorizedLocal(
            project,
            [candidate],
            OwnerA,
            DeviceOne,
            _ => true));

        Assert.True(StudioLocalSourceBindingPolicy.TryExplicitRelink(
            legacy,
            authorizedControllerEmail: OwnerA,
            currentAccountEmail: OwnerA,
            currentDeviceFingerprint: DeviceOne,
            hasVerifiedPayload: true));
        Assert.True(StudioLocalSourceBindingPolicy.IsLocal(
            legacy,
            OwnerA,
            DeviceOne,
            hasVerifiedPayload: true));
    }

    [Fact]
    public void ComponentOnlySourceChange_RequiresExactLocalBinding()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource(project, OwnerA);
        project.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];
        StudioLocalSourceBindingPolicy.Bind(source, OwnerA, DeviceOne);
        string componentCode =
            StudioAlbumComponentIdentity.SourceCode(OwnerA, SourceKey);
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [componentCode]);

        CloudSyncPreviewPlan local = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "DEVICE-ONE",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            _ => true);
        CloudSyncPreviewPlan copiedMirror = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "DEVICE-TWO",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceTwo,
            _ => true);

        Assert.Empty(ProjectCloudSyncMetadata.PendingSourcePackages(project));
        Assert.True(local.IsComponentAuthorized(componentCode));
        Assert.False(copiedMirror.IsComponentAuthorized(componentCode));
        Assert.Contains(
            copiedMirror.Blocked,
            item => item.Code == componentCode);
    }

    [Fact]
    public void SharedSourceComponentWithoutLocalSource_IsAlwaysReadOnly()
    {
        ProjectWorkspace project = CloudProject();
        string componentCode =
            StudioAlbumComponentIdentity.SourceCode(OwnerA, SourceKey);
        project.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];
        project.Cloud.SharedAlbumComponents =
        [
            new ProjectCloudAlbumComponentReference
            {
                Code = componentCode,
                Label = "Cloud building",
                ComponentKind =
                    StudioAlbumComponentIdentity.SourceComponentKind,
                OwnerEmail = OwnerA,
                SourceKey = SourceKey,
            },
        ];
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [componentCode]);

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "DEVICE-ONE",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            _ => true);

        Assert.False(plan.IsComponentAuthorized(componentCode));
        Assert.Contains(plan.Blocked, item =>
            item.Code == componentCode &&
            item.Detail.Contains("read-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SiteContextComponent_AClearedContextMayBeSentFromAnyDeviceThatMayEdit()
    {
        // The positive half of the rule below. A cleared site context (no
        // boundary, no snapshots) carries nothing device-bound, so a member
        // who may edit sends it even without the exact local source - the
        // clearing would otherwise be stuck on the machine that did it.
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource(project, OwnerA);
        source.Kind = DesignSourceKind.CityGen;
        project.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];
        StudioLocalSourceBindingPolicy.Bind(source, OwnerA, DeviceOne);
        project.SiteContext.Boundary = new ProjectSiteBoundary { SourceId = source.Id };
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [ProjectCloudSyncMetadata.SiteContextComponentCode]);

        CloudSyncPreviewPlan otherDevice = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "DEVICE-TWO",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceTwo,
            _ => true);

        Assert.True(otherDevice.IsComponentAuthorized(
            ProjectCloudSyncMetadata.SiteContextComponentCode));
    }

    [Fact]
    public void SiteContextComponent_RequiresControllingSourceOnExactDevice()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource(project, OwnerA);
        source.Kind = DesignSourceKind.CityGen;
        project.Cloud.SharedSources = [SharedSource(OwnerA, OwnerA)];
        StudioLocalSourceBindingPolicy.Bind(source, OwnerA, DeviceOne);
        project.SiteContext.Boundary = new ProjectSiteBoundary
        {
            SourceId = source.Id,
            Ring =
            [
                new ProjectGeoCoordinate { Longitude = 106.90, Latitude = 47.90 },
                new ProjectGeoCoordinate { Longitude = 106.91, Latitude = 47.90 },
                new ProjectGeoCoordinate { Longitude = 106.91, Latitude = 47.91 },
                new ProjectGeoCoordinate { Longitude = 106.90, Latitude = 47.90 },
            ],
        };
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [ProjectCloudSyncMetadata.SiteContextComponentCode]);

        CloudSyncPreviewPlan local = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "DEVICE-ONE",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceOne,
            _ => true);
        CloudSyncPreviewPlan copiedMirror = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "DEVICE-TWO",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceTwo,
            _ => true);

        Assert.True(local.IsComponentAuthorized(
            ProjectCloudSyncMetadata.SiteContextComponentCode));
        Assert.False(copiedMirror.IsComponentAuthorized(
            ProjectCloudSyncMetadata.SiteContextComponentCode));
    }

    private static ProjectWorkspace CloudProject() => new()
    {
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project-1",
        },
    };

    private static ProjectDesignSource LocalSource(
        ProjectWorkspace project,
        string owner)
    {
        var source = new ProjectDesignSource
        {
            Id = "local-source",
            Kind = DesignSourceKind.Revit,
            NativeDocumentPath = @"C:\verified\building.rvt",
            InboxFolder = @"C:\verified\deliveries",
        };
        project.Sources = [source];
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, SourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        return source;
    }

    private static ProjectCloudSourceReference SharedSource(
        string registeredBy,
        string custodian) => new()
    {
        SourceId = "cloud-source",
        SourceKey = SourceKey,
        SourceApplication = "Revit",
        SourceDocumentReference = "building.rvt",
        ManifestId = "manifest-1",
        ContentHash = "hash-1",
        SheetCount = 1,
        Status = "Registered",
        RegisteredBy = registeredBy,
        CustodianEmail = custodian,
        OwnerEmail = custodian,
    };

    private static void RecordPackage(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            new SheetPackageManifest
            {
                SchemaVersion = 4,
                PackageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ExportedAtUtc =
                    new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero),
                Source = new SheetPackageSource
                {
                    SourceId = source.Id,
                    Application = SheetSourceApplication.Revit,
                    DocumentTitle = "building.rvt",
                },
                Sheets =
                [
                    new SheetPackageEntry
                    {
                        SheetId = "sheet-1",
                        Sha256 = "sheet-hash",
                    },
                ],
            },
            "manifest-hash");
    }
}
