using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSourceBoundaryTests
{
    private const string OwnerA = "owner-a@erks.local";
    private const string OwnerB = "owner-b@erks.local";
    private const string DeviceA = "device-a";
    private const string DeviceB = "device-b";
    private const string SourceKey = "shared-source";

    [Fact]
    public void InboxPayload_RequiresLosslessManifestForExactSourceId()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-studio-source-payload-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = new ProjectDesignSource
            {
                Id = "source-a",
                InboxFolder = root,
            };
            File.WriteAllText(Path.Combine(root, "unrelated.json"), "{}");
            File.WriteAllText(Path.Combine(root, "unrelated.pdf"), "not a PDF");

            Assert.False(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));

            WriteEmptyFullSnapshot(root, "wrong-source", "source-b");

            Assert.False(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));

            WriteEmptyFullSnapshot(root, "exact-source", source.Id);

            Assert.True(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitNativeRelink_AcceptsExistingNativePayloadWithoutInboxPackage()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-studio-native-payload-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string nativePath = Path.Combine(root, "building.rvt");
            File.WriteAllText(nativePath, "native test payload");
            var source = new ProjectDesignSource
            {
                Id = "source-a",
                NativeDocumentPath = nativePath,
                InboxFolder = Path.Combine(root, "missing-inbox"),
            };

            Assert.True(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InboxPayload_RequiresCurrentRecordedManifestAndContentHash()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-studio-current-source-payload-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new ProjectWorkspace();
            var source = new ProjectDesignSource
            {
                Id = "source-a",
                InboxFolder = root,
            };
            project.Sources.Add(source);

            string historicalPath =
                WriteEmptyFullSnapshot(root, "historical", source.Id);
            SheetPackageLoadResult historical =
                SheetPackageReader.Load(historicalPath);
            ProjectCloudSyncMetadata.RecordPackage(
                project,
                source,
                historical.Manifest!,
                historical.ManifestSha256);
            Assert.True(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));

            string currentPath =
                WriteEmptyFullSnapshot(root, "current", source.Id);
            SheetPackageLoadResult current =
                SheetPackageReader.Load(currentPath);
            ProjectCloudSyncMetadata.RecordPackage(
                project,
                source,
                current.Manifest!,
                current.ManifestSha256);
            Assert.True(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));

            File.AppendAllText(currentPath, Environment.NewLine);

            Assert.False(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));

            File.Delete(currentPath);

            Assert.False(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CloudUnionPreviewScope_ExcludesCopiedMirrorPendingSourceAndComponent()
    {
        ProjectWorkspace project = ProjectWithPendingSource();
        ProjectDesignSource source = Assert.Single(project.Sources);
        StudioLocalSourceBindingPolicy.Bind(source, OwnerA, DeviceA);
        string componentCode =
            StudioAlbumComponentIdentity.SourceCode(OwnerA, SourceKey);
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [componentCode]);

        StudioCloudUnionPendingScope local =
            StudioCloudUnionPreviewScope.Resolve(
                project,
                OwnerA,
                DeviceA,
                _ => true);
        StudioCloudUnionPendingScope copiedMirror =
            StudioCloudUnionPreviewScope.Resolve(
                project,
                OwnerA,
                DeviceB,
                _ => true);

        Assert.Single(local.Sources);
        Assert.Equal([componentCode], local.ComponentCodes);
        Assert.Empty(copiedMirror.Sources);
        Assert.Empty(copiedMirror.ComponentCodes);
    }

    [Fact]
    public void LegacySourceIdentity_RejectsAmbiguousSourceKeyAcrossImmutableOwners()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource sourceA =
            Source("source-a", SourceKey, OwnerA);
        ProjectDesignSource sourceB =
            Source("source-b", SourceKey, OwnerB);
        project.Sources = [sourceA, sourceB];

        Assert.Same(
            sourceA,
            StudioLegacySourceResolver.Resolve(project, sourceA.Id));
        Assert.Null(
            StudioLegacySourceResolver.Resolve(project, SourceKey));
    }

    [Fact]
    public void LegacyPendingComponent_WithAmbiguousOwnerStreamIsBlocked()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource sourceA =
            Source("source-a", SourceKey, OwnerA);
        ProjectDesignSource sourceB =
            Source("source-b", SourceKey, OwnerB);
        project.Sources = [sourceA, sourceB];
        StudioLocalSourceBindingPolicy.Bind(sourceA, OwnerA, DeviceA);
        string legacyCode = "source:" + SourceKey;
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [legacyCode]);

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            OwnerA,
            "DEVICE-A",
            new StudioCloudProjectRefreshResult(false, null),
            DeviceA,
            _ => true);

        Assert.False(plan.IsComponentAuthorized(legacyCode));
        Assert.Contains(
            plan.Blocked,
            item => item.Code.Equals(
                legacyCode,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LegacySourceIdentity_ResolvesUniqueSourceKey()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source =
            Source("source-a", SourceKey, OwnerA);
        project.Sources = [source];

        Assert.Same(
            source,
            StudioLegacySourceResolver.Resolve(project, SourceKey));
    }

    [Fact]
    public void LegacySourceIdentity_RejectsCloudOwnerAmbiguityWithOneLocalMirror()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource localMirror =
            Source("source-a", SourceKey, OwnerA);
        project.Sources = [localMirror];
        project.Cloud.SharedSources =
        [
            SharedSource("cloud-a", OwnerA),
            SharedSource("cloud-b", OwnerB),
        ];

        Assert.Null(
            StudioLegacySourceResolver.Resolve(project, SourceKey));
    }

    private static ProjectWorkspace ProjectWithPendingSource()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source =
            Source("source-a", SourceKey, OwnerA);
        project.Sources = [source];
        project.Cloud.SharedSources =
        [
            new ProjectCloudSourceReference
            {
                SourceId = source.Id,
                SourceKey = SourceKey,
                SourceApplication = "Revit",
                SourceDocumentReference = "building.rvt",
                ManifestId = "manifest-a",
                ContentHash = "hash-a",
                SheetCount = 1,
                Status = "Registered",
                RegisteredBy = OwnerA,
                OwnerEmail = OwnerA,
                CustodianEmail = OwnerA,
            },
        ];
        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            new SheetPackageManifest
            {
                SchemaVersion = 4,
                PackageId =
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
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
                        SheetId = "sheet-a",
                        Sha256 = "sheet-hash-a",
                    },
                ],
            },
            "manifest-hash-a");
        return project;
    }

    private static ProjectWorkspace CloudProject() => new()
    {
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project-1",
        },
    };

    private static ProjectDesignSource Source(
        string id,
        string sourceKey,
        string owner)
    {
        var source = new ProjectDesignSource
        {
            Id = id,
            Kind = DesignSourceKind.Revit,
        };
        var project = new ProjectWorkspace { Sources = [source] };
        ProjectCloudSyncMetadata.BindToCloudSource(
            project,
            source,
            sourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        return source;
    }

    private static ProjectCloudSourceReference SharedSource(
        string sourceId,
        string owner) => new()
    {
        SourceId = sourceId,
        SourceKey = SourceKey,
        Status = "Registered",
        RegisteredBy = owner,
        OwnerEmail = owner,
        CustodianEmail = owner,
    };

    private static string WriteEmptyFullSnapshot(
        string root,
        string baseName,
        string sourceId)
    {
        return SheetPackageWriter.Write(
            new SheetPackageManifest
            {
                SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
                PackageId = Guid.NewGuid(),
                PackageScope = SheetPackageScope.FullSnapshot,
                Source = new SheetPackageSource
                {
                    SourceId = sourceId,
                    Application = SheetSourceApplication.Revit,
                    DocumentTitle = "building.rvt",
                },
                Sheets = [],
            },
            root,
            baseName);
    }
}
