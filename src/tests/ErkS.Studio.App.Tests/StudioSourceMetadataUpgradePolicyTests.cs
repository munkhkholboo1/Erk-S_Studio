using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSourceMetadataUpgradePolicyTests
{
    private const string Owner = "owner@erks.local";
    private const string Foreign = "foreign@erks.local";
    private const string DeviceOne = "device-one";
    private const string DeviceTwo = "device-two";
    private const string SourceKey = "source-key";

    [Fact]
    public void Apply_BackfillsExactLegacySourceOnceAndPersistsSchemaVersion()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources = [RegistrySource("registry-1", Owner)];

        StudioSourceMetadataUpgradeReport first =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);
        StudioSourceMetadataUpgradeReport second =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);

        Assert.Equal(1, first.ChangedCount);
        Assert.Equal(1, first.BoundCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.LegacyBindingBackfilled,
            Assert.Single(first.Decisions).Reason);
        Assert.Equal(
            "source_metadata_legacy_binding_backfilled",
            Assert.Single(first.Decisions).ReasonCode);
        Assert.True(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            Owner,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.Equal(
            StudioSourceMetadataUpgradePolicy.CurrentSchemaVersion,
            StudioSourceMetadataUpgradePolicy.SchemaVersion(source));

        Assert.Equal(0, second.ChangedCount);
        Assert.Equal(0, second.BoundCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.SchemaCurrent,
            Assert.Single(second.Decisions).Reason);
    }

    [Fact]
    public void Apply_CurrentSchemaNeverRechecksTheLocalPayload()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources = [RegistrySource("registry-1", Owner)];
        int payloadChecks = 0;

        StudioSourceMetadataUpgradePolicy.Apply(
            project,
            Owner,
            DeviceOne,
            _ =>
            {
                payloadChecks++;
                return true;
            });
        StudioSourceMetadataUpgradePolicy.Apply(
            project,
            Owner,
            DeviceOne,
            _ =>
            {
                payloadChecks++;
                return true;
            });

        Assert.Equal(1, payloadChecks);
        Assert.Equal(
            StudioSourceMetadataUpgradePolicy.CurrentSchemaVersion,
            StudioSourceMetadataUpgradePolicy.SchemaVersion(source));
    }

    [Fact]
    public void Apply_EmailAndRegistryWithoutVerifiedPayloadNeverCreateBindingOrVersion()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources = [RegistrySource("registry-1", Owner)];

        StudioSourceMetadataUpgradeReport report =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => false);

        Assert.Equal(0, report.ChangedCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.VerifiedPayloadMissing,
            Assert.Single(report.Decisions).Reason);
        Assert.False(StudioLocalSourceBindingPolicy.HasAnyBinding(source));
        Assert.Equal(0, StudioSourceMetadataUpgradePolicy.SchemaVersion(source));
    }

    [Fact]
    public void Apply_ForeignOwnerNeverAdoptsEvenWhenPayloadExists()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources = [RegistrySource("registry-1", Owner)];

        StudioSourceMetadataUpgradeReport report =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Foreign,
                DeviceOne,
                _ => true);

        Assert.Equal(0, report.ChangedCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.ImmutableOwnerMismatch,
            Assert.Single(report.Decisions).Reason);
        Assert.False(StudioLocalSourceBindingPolicy.HasAnyBinding(source));
    }

    [Fact]
    public void Apply_DuplicateRegistryIdentityIsAmbiguousAndNeverBinds()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources =
        [
            RegistrySource("registry-1", Owner),
            RegistrySource("registry-2", Owner),
        ];

        StudioSourceMetadataUpgradeReport report =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);

        Assert.Equal(0, report.ChangedCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.CloudRegistryIdentityAmbiguous,
            Assert.Single(report.Decisions).Reason);
        Assert.False(StudioLocalSourceBindingPolicy.HasAnyBinding(source));
    }

    [Fact]
    public void Apply_MissingOrIncompleteRegistryIdentityNeverBinds()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources =
        [
            RegistrySource("", Owner),
        ];

        StudioSourceMetadataUpgradeReport report =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);

        Assert.Equal(0, report.ChangedCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.CloudRegistryIdentityMissing,
            Assert.Single(report.Decisions).Reason);
        Assert.False(StudioLocalSourceBindingPolicy.HasAnyBinding(source));
    }

    [Fact]
    public void Apply_ExistingOtherDeviceBindingIsVersionedButNeverOverwritten()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources = [RegistrySource("registry-1", Owner)];
        StudioLocalSourceBindingPolicy.Bind(source, Owner, DeviceTwo);

        StudioSourceMetadataUpgradeReport first =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);
        StudioSourceMetadataUpgradeReport second =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);

        Assert.Equal(1, first.ChangedCount);
        Assert.Equal(0, first.BoundCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.ExistingBindingVersioned,
            Assert.Single(first.Decisions).Reason);
        Assert.True(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            Owner,
            DeviceTwo,
            hasVerifiedPayload: true));
        Assert.False(StudioLocalSourceBindingPolicy.IsLocal(
            source,
            Owner,
            DeviceOne,
            hasVerifiedPayload: true));
        Assert.Equal(0, second.ChangedCount);
    }

    [Fact]
    public void Apply_PartialExistingBindingIsNeverCompletedOrVersionedImplicitly()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources = [RegistrySource("registry-1", Owner)];
        source.Metadata["local.bindingAccountEmail"] = Owner;

        StudioSourceMetadataUpgradeReport report =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);

        Assert.Equal(0, report.ChangedCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.ExistingBindingIncomplete,
            Assert.Single(report.Decisions).Reason);
        Assert.Equal(Owner, source.Metadata["local.bindingAccountEmail"]);
        Assert.False(source.Metadata.ContainsKey(
            "local.bindingDeviceFingerprint"));
        Assert.Equal(0, StudioSourceMetadataUpgradePolicy.SchemaVersion(source));
    }

    [Fact]
    public void Apply_NewerMetadataSchemaIsNeverDowngradedOrRebound()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LegacySource(project, Owner);
        project.Cloud.SharedSources = [RegistrySource("registry-1", Owner)];
        source.Metadata[StudioSourceMetadataUpgradePolicy.SchemaVersionKey] =
            (StudioSourceMetadataUpgradePolicy.CurrentSchemaVersion + 1)
                .ToString();

        StudioSourceMetadataUpgradeReport report =
            StudioSourceMetadataUpgradePolicy.Apply(
                project,
                Owner,
                DeviceOne,
                _ => true);

        Assert.Equal(0, report.ChangedCount);
        Assert.Equal(
            StudioSourceMetadataUpgradeReason.SchemaNewer,
            Assert.Single(report.Decisions).Reason);
        Assert.False(StudioLocalSourceBindingPolicy.HasAnyBinding(source));
        Assert.Equal(
            StudioSourceMetadataUpgradePolicy.CurrentSchemaVersion + 1,
            StudioSourceMetadataUpgradePolicy.SchemaVersion(source));
    }

    [Fact]
    public void LegacyUpgradeEvidence_NativeFileAloneIsInsufficient()
    {
        string root = TempRoot();
        try
        {
            string nativePath = Path.Combine(root, "building.rvt");
            File.WriteAllText(nativePath, "native payload");
            ProjectWorkspace project = CloudProject();
            ProjectDesignSource source = LegacySource(project, Owner);
            source.NativeDocumentPath = nativePath;
            source.InboxFolder = Path.Combine(root, "inbox");
            Directory.CreateDirectory(source.InboxFolder);

            Assert.True(
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));
            Assert.False(
                StudioLocalSourceBindingPolicy
                    .HasVerifiedLegacyUpgradePayload(project, source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyUpgradeEvidence_RequiresExactCurrentPackageAndNativePath()
    {
        string root = TempRoot();
        try
        {
            string nativePath = Path.Combine(root, "building.rvt");
            File.WriteAllText(nativePath, "native payload");
            string inbox = Path.Combine(root, "inbox");
            Directory.CreateDirectory(inbox);
            ProjectWorkspace project = CloudProject();
            ProjectDesignSource source = LegacySource(project, Owner);
            source.NativeDocumentPath = nativePath;
            source.InboxFolder = inbox;
            string manifestPath = SheetPackageWriter.Write(
                new SheetPackageManifest
                {
                    SchemaVersion =
                        SheetPackageManifest.CurrentSchemaVersion,
                    PackageScope = SheetPackageScope.FullSnapshot,
                    ProjectId = project.ProjectId,
                    Source = new SheetPackageSource
                    {
                        SourceId = source.Id,
                        Application = SheetSourceApplication.Revit,
                        DocumentPath = nativePath,
                        DocumentTitle = Path.GetFileName(nativePath),
                    },
                    Sheets = [],
                },
                inbox,
                "exact-source");
            SheetPackageLoadResult package =
                SheetPackageReader.Load(manifestPath);
            ProjectCloudSyncMetadata.RecordPackage(
                project,
                source,
                package.Manifest!,
                package.ManifestSha256);

            Assert.True(
                StudioLocalSourceBindingPolicy
                    .HasVerifiedLegacyUpgradePayload(project, source));

            File.AppendAllText(manifestPath, Environment.NewLine);

            Assert.False(
                StudioLocalSourceBindingPolicy
                    .HasVerifiedLegacyUpgradePayload(project, source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProjectWorkspace CloudProject() => new()
    {
        ProjectId = "project-1",
        Cloud = new ProjectCloudLink
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "project-1",
        },
    };

    private static ProjectDesignSource LegacySource(
        ProjectWorkspace project,
        string owner)
    {
        var source = new ProjectDesignSource
        {
            Id = "local-source",
            Kind = DesignSourceKind.Revit,
        };
        project.Sources = [source];
        ProjectCloudSyncMetadata.BindToCloudSource(
            project,
            source,
            SourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        return source;
    }

    private static ProjectCloudSourceReference RegistrySource(
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

    private static string TempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-source-metadata-upgrade-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
