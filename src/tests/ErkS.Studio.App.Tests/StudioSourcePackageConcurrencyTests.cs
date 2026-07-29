using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSourcePackageConcurrencyTests
{
    [Fact]
    public void FirstRegistrationHasNoExpectedBase()
    {
        (ProjectWorkspace project, ProjectSourceSyncCandidate candidate) =
            Candidate("owner@erks.local");

        Assert.Equal(
            "",
            StudioSourcePackageConcurrency.ExpectedBaseSourceId(
                project,
                candidate));
    }

    [Fact]
    public void UpdateUsesExactCurrentSnapshotForImmutableOwnerAndSourceKey()
    {
        (ProjectWorkspace project, ProjectSourceSyncCandidate candidate) =
            Candidate("owner@erks.local");
        project.Cloud.SharedSources =
        [
            Shared("current-owner", "shared-source", "owner@erks.local"),
            Shared("other-person", "shared-source", "other@erks.local"),
            Shared("other-key", "different-source", "owner@erks.local"),
        ];

        Assert.Equal(
            "current-owner",
            StudioSourcePackageConcurrency.ExpectedBaseSourceId(
                project,
                candidate));
    }

    [Fact]
    public void CustodianUpdateRetainsOriginalOwnerStreamIdentity()
    {
        (ProjectWorkspace project, ProjectSourceSyncCandidate candidate) =
            Candidate("original@erks.local");
        project.Cloud.SharedSources =
        [
            new ProjectCloudSourceReference
            {
                SourceId = "transferred-current",
                SourceKey = "shared-source",
                RegisteredBy = "original@erks.local",
                CustodianEmail = "custodian@erks.local",
                OwnerEmail = "custodian@erks.local",
                Status = "Registered",
            },
        ];

        Assert.Equal(
            "transferred-current",
            StudioSourcePackageConcurrency.ExpectedBaseSourceId(
                project,
                candidate));
    }

    [Fact]
    public void MissingImmutableOwnerNeverAdoptsAnotherParticipantsStream()
    {
        (ProjectWorkspace project, ProjectSourceSyncCandidate candidate) =
            Candidate("");
        project.Cloud.SharedSources =
        [
            Shared("foreign-current", "shared-source", "foreign@erks.local"),
        ];

        Assert.Equal(
            "",
            StudioSourcePackageConcurrency.ExpectedBaseSourceId(
                project,
                candidate));
    }

    [Fact]
    public void AmbiguousMirrorMustRefreshInsteadOfGuessingByTimestamp()
    {
        (ProjectWorkspace project, ProjectSourceSyncCandidate candidate) =
            Candidate("owner@erks.local");
        project.Cloud.SharedSources =
        [
            Shared("old", "shared-source", "owner@erks.local"),
            Shared("new", "shared-source", "owner@erks.local"),
        ];

        Assert.Throws<InvalidOperationException>(() =>
            StudioSourcePackageConcurrency.ExpectedBaseSourceId(
                project,
                candidate));
    }

    private static (
        ProjectWorkspace Project,
        ProjectSourceSyncCandidate Candidate) Candidate(string owner)
    {
        var project = new ProjectWorkspace
        {
            Cloud = new ProjectCloudLink
            {
                Origin = ProjectOrigins.Cloud,
                ServerProjectId = "project-1",
            },
        };
        var source = new ProjectDesignSource { Id = "local-source" };
        project.Sources = [source];
        ProjectCloudSyncMetadata.BindToCloudSource(
            project,
            source,
            "shared-source");
        if (!string.IsNullOrWhiteSpace(owner))
            ProjectCloudSyncMetadata.BindCloudOwner(source, owner);
        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            new ErkS.Platform.Contracts.SheetPackageManifest
            {
                SchemaVersion = 4,
                PackageId = Guid.NewGuid(),
                ExportedAtUtc = DateTimeOffset.UtcNow,
                Source = new ErkS.Platform.Contracts.SheetPackageSource
                {
                    SourceId = source.Id,
                    Application =
                        ErkS.Platform.Contracts.SheetSourceApplication.Revit,
                },
                Sheets = [],
            },
            "content-hash");
        return (
            project,
            Assert.Single(
                ProjectCloudSyncMetadata.PendingSourcePackages(project)));
    }

    private static ProjectCloudSourceReference Shared(
        string id,
        string sourceKey,
        string owner) => new()
    {
        SourceId = id,
        SourceKey = sourceKey,
        RegisteredBy = owner,
        OwnerEmail = owner,
        CustodianEmail = owner,
        Status = "Registered",
    };
}
