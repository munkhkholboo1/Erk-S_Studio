using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioRuntimeSourceScopeTests : IDisposable
{
    private const string Owner = "owner@erks.local";
    private const string Other = "other@erks.local";
    private const string Unrelated = "unrelated@outside.local";
    private const string DeviceOne = "device-one";
    private const string DeviceTwo = "device-two";
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-runtime-source-scope",
        Guid.NewGuid().ToString("N"));

    public StudioRuntimeSourceScopeTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void AuthorizedSources_RequiresAuthorityExactAccountDeviceAndPayload()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = Source(project, "source-a", "source-key", Owner);
        project.Cloud.SharedSources = [Shared("source-key", Owner, Owner)];
        StudioLocalSourceBindingPolicy.Bind(source, Owner, DeviceOne);

        Assert.Equal(
            [source],
            StudioRuntimeSourceScope.AuthorizedSources(
                project,
                Owner,
                DeviceOne,
                _ => true));
        Assert.Empty(StudioRuntimeSourceScope.AuthorizedSources(
            project,
            Other,
            DeviceOne,
            _ => true));
        Assert.Empty(StudioRuntimeSourceScope.AuthorizedSources(
            project,
            Unrelated,
            DeviceOne,
            _ => true));
        Assert.Empty(StudioRuntimeSourceScope.AuthorizedSources(
            project,
            Owner,
            DeviceTwo,
            _ => true));
        Assert.Empty(StudioRuntimeSourceScope.AuthorizedSources(
            project,
            Owner,
            DeviceOne,
            _ => false));
    }

    [Fact]
    public void ResolvePackageSource_RejectsForeignInboxEvenWhenManifestClaimsLocalSourceId()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = Source(project, "source-a", "source-key", Owner);
        source.InboxFolder = Path.Combine(Path.GetTempPath(), "runtime-scope", "local");
        project.Cloud.SharedSources = [Shared("source-key", Owner, Owner)];
        StudioLocalSourceBindingPolicy.Bind(source, Owner, DeviceOne);
        SheetPackageManifest manifest = Manifest(project, source);

        var foreign = new SheetPackageLoadResult
        {
            ManifestPath = Path.Combine(
                Path.GetTempPath(),
                "runtime-scope",
                "foreign",
                "package.erks-sheets.json"),
            Manifest = manifest,
            ManifestSha256 = "hash",
        };
        var local = new SheetPackageLoadResult
        {
            ManifestPath = Path.Combine(
                source.InboxFolder,
                "package.erks-sheets.json"),
            Manifest = manifest,
            ManifestSha256 = "hash",
        };

        Assert.Null(StudioRuntimeSourceScope.ResolvePackageSource(
            project,
            foreign,
            Owner,
            DeviceOne,
            _ => true));
        Assert.Same(
            source,
            StudioRuntimeSourceScope.ResolvePackageSource(
                project,
                local,
                Owner,
                DeviceOne,
                _ => true));
        Assert.Null(StudioRuntimeSourceScope.ResolvePackageSource(
            project,
            local,
            Other,
            DeviceOne,
            _ => true));
        Assert.Null(StudioRuntimeSourceScope.ResolvePackageSource(
            project,
            local,
            Owner,
            DeviceTwo,
            _ => true));
    }

    [Fact]
    public void LegacyImmutableOwner_ComesOnlyFromStoredOwnerOrUniqueCloudRegistrant()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = Source(
            project,
            "source-a",
            "source-key",
            owner: "");

        Assert.Equal(
            "",
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source));

        project.Cloud.SharedSources =
        [
            Shared("source-key", Owner, Owner),
            Shared("source-key", Other, Other),
        ];
        Assert.Equal(
            "",
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source));

        project.Cloud.SharedSources = [Shared("source-key", Owner, Owner)];
        Assert.Equal(
            Owner,
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source));

        ProjectCloudSyncMetadata.BindCloudOwner(source, Other);
        Assert.Equal(
            Other,
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source));
    }

    [Fact]
    public void CollectUiFalseOperations_NeverReconcileCanonicalLinkedAssets()
    {
        Assert.True(
            StudioRefreshSyncOperationPolicy.ShouldReconcileLinkedProjectAssets(
                StudioWorkspaceOperation.ExplicitAlbumEdit));
        Assert.False(
            StudioRefreshSyncOperationPolicy.ShouldReconcileLinkedProjectAssets(
                StudioWorkspaceOperation.SourceRefresh));
        Assert.False(
            StudioRefreshSyncOperationPolicy.ShouldReconcileLinkedProjectAssets(
                StudioWorkspaceOperation.CloudSync));
    }

    [Fact]
    public void AppStateRuntimeWatchers_FollowExactAccountAndDeviceWithoutPersistingSwitch()
    {
        string projectFolder = Path.Combine(workDirectory, "project");
        string projectPath = Path.Combine(
            projectFolder,
            ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project =
            ProjectWorkspaceStore.Create("RUNTIME-001", "Runtime scope");
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "cloud-project-1";
        ProjectDesignSource local = RuntimeSource(
            projectFolder,
            "source-local",
            "source-local-key",
            Owner,
            DeviceOne);
        ProjectDesignSource foreign = RuntimeSource(
            projectFolder,
            "source-foreign",
            "source-foreign-key",
            Other,
            DeviceTwo);
        project.Sources = [local, foreign];
        project.Cloud.SharedSources =
        [
            Shared("source-local-key", Owner, Owner),
            Shared("source-foreign-key", Other, Other),
        ];
        ProjectWorkspaceStore.Save(project, projectPath);
        string albumPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            project.PrimaryAlbum.DocumentPath);
        StudioAlbumDocumentStore.Save(new StudioAlbumDocument
        {
            ProjectId = project.ProjectId,
            AlbumId = project.PrimaryAlbum.Id,
            Definition =
                BuildingArchitectureConceptAlbumTemplate.CreateDefinition(
                    project.PrimaryAlbum.Title),
        }, albumPath);

        using var state = new AppState();
        state.ConfigureSourceRuntimeContext(Owner, DeviceOne);
        state.OpenProject(projectPath);

        Assert.Equal(
            Path.GetFullPath(local.InboxFolder),
            Assert.Single(state.Intake.WatchedFolders));
        string persistedBeforeSwitch = File.ReadAllText(projectPath);

        state.ConfigureSourceRuntimeContext(Other, DeviceOne);

        Assert.Empty(state.Intake.WatchedFolders);
        Assert.Equal(persistedBeforeSwitch, File.ReadAllText(projectPath));

        state.ConfigureSourceRuntimeContext(Unrelated, DeviceOne);

        Assert.Empty(state.Intake.WatchedFolders);
        Assert.Equal(persistedBeforeSwitch, File.ReadAllText(projectPath));
    }

    [Fact]
    public void AddDesignSource_SamePcAccountSwitchCannotAdoptExistingSourceByNativePath()
    {
        string projectFolder = Path.Combine(workDirectory, "account-switch-project");
        string projectPath = Path.Combine(
            projectFolder,
            ProjectWorkspace.DefaultFileName);
        ProjectWorkspace project =
            ProjectWorkspaceStore.Create("RUNTIME-002", "Account switch scope");
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "cloud-project-2";
        ProjectDesignSource ownerSource = RuntimeSource(
            projectFolder,
            "source-owner",
            "source-owner-key",
            Owner,
            DeviceOne);
        project.Sources = [ownerSource];
        project.Cloud.SharedSources =
        [
            Shared("source-owner-key", Owner, Owner),
        ];
        ProjectWorkspaceStore.Save(project, projectPath);
        string albumPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            project.PrimaryAlbum.DocumentPath);
        StudioAlbumDocumentStore.Save(new StudioAlbumDocument
        {
            ProjectId = project.ProjectId,
            AlbumId = project.PrimaryAlbum.Id,
            Definition =
                BuildingArchitectureConceptAlbumTemplate.CreateDefinition(
                    project.PrimaryAlbum.Title),
        }, albumPath);

        using var state = new AppState();
        state.ConfigureSourceRuntimeContext(Other, DeviceOne);
        state.OpenProject(projectPath);
        string ownerMetadata = string.Join(
            "\n",
            state.Project.Sources.Single().Metadata
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}={item.Value}"));
        var teammateSource = new ProjectDesignSource
        {
            Id = "source-teammate",
            Kind = DesignSourceKind.Revit,
            Name = "Teammate source",
            NativeDocumentPath = ownerSource.NativeDocumentPath,
            NativeDocumentTitle = ownerSource.NativeDocumentTitle,
            InboxFolder = Path.Combine(
                projectFolder,
                "sources",
                "source-teammate",
                "deliveries"),
        };
        ProjectCloudSyncMetadata.BindToCloudSource(
            state.Project,
            teammateSource,
            "source-teammate-key");
        ProjectCloudSyncMetadata.BindCloudOwner(teammateSource, Other);
        StudioLocalSourceBindingPolicy.Bind(
            teammateSource,
            Other,
            DeviceOne);

        state.AddDesignSource(teammateSource);

        Assert.Equal(2, state.Project.Sources.Count);
        ProjectDesignSource persistedOwner =
            state.Project.Sources.Single(source => source.Id == "source-owner");
        Assert.Equal(
            ownerMetadata,
            string.Join(
                "\n",
                persistedOwner.Metadata
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => $"{item.Key}={item.Value}")));
        Assert.Equal(
            Owner,
            ProjectCloudSyncMetadata.CloudOwnerEmail(persistedOwner));
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            persistedOwner,
            Other,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.True(StudioLocalSourceBindingPolicy.IsLocal(
            state.Project.Sources.Single(source => source.Id == "source-teammate"),
            Other,
            DeviceOne,
            hasVerifiedPayload: true));
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

    private static ProjectWorkspace CloudProject() => new()
    {
        ProjectId = "project-1",
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "cloud-project-1",
        },
    };

    private static ProjectDesignSource Source(
        ProjectWorkspace project,
        string id,
        string sourceKey,
        string owner)
    {
        var source = new ProjectDesignSource
        {
            Id = id,
            Kind = DesignSourceKind.Revit,
            InboxFolder = Path.Combine(Path.GetTempPath(), id, "deliveries"),
        };
        project.Sources.Add(source);
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, sourceKey);
        if (!string.IsNullOrWhiteSpace(owner))
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
        OwnerEmail = custodian,
        CustodianEmail = custodian,
    };

    private static SheetPackageManifest Manifest(
        ProjectWorkspace project,
        ProjectDesignSource source) => new()
    {
        SchemaVersion = 4,
        PackageId = Guid.NewGuid(),
        ProjectId = project.ProjectId,
        Source = new SheetPackageSource
        {
            SourceId = source.Id,
            Application = SheetSourceApplication.Revit,
            DocumentTitle = "building.rvt",
        },
        Sheets = [],
    };

    private static ProjectDesignSource RuntimeSource(
        string projectFolder,
        string id,
        string sourceKey,
        string owner,
        string device)
    {
        string nativePath = Path.Combine(
            projectFolder,
            "sources",
            id,
            id + ".rvt");
        string inbox = Path.Combine(
            projectFolder,
            "sources",
            id,
            "deliveries");
        Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
        Directory.CreateDirectory(inbox);
        File.WriteAllText(nativePath, "verified local payload");
        var source = new ProjectDesignSource
        {
            Id = id,
            Kind = DesignSourceKind.Revit,
            NativeDocumentPath = nativePath,
            InboxFolder = inbox,
        };
        var holder = new ProjectWorkspace { Sources = [source] };
        ProjectCloudSyncMetadata.BindToCloudSource(holder, source, sourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        StudioLocalSourceBindingPolicy.Bind(source, owner, device);
        return source;
    }
}
