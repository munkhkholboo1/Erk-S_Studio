using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSharedSourceProjectionTests
{
    [Fact]
    public void Project_UsesImmutableRegistrantAfterCustodyTransfer()
    {
        ProjectCloudSourceReference source = Source(
            "shared-key",
            registeredBy: "original@erks.local",
            ownerEmail: "new-custodian@erks.local");
        source.CustodianEmail = "new-custodian@erks.local";

        StudioCloudSourcePackage projected = Assert.Single(
            StudioSharedSourceProjection.Create([source]));

        Assert.Equal("original@erks.local", projected.RegisteredBy);
        Assert.Equal("new-custodian@erks.local", projected.CustodianEmail);
        Assert.Equal(
            "original@erks.local",
            StudioSharedSourceProjection.ImmutableOwner(source));
        Assert.Equal(
            StudioAlbumComponentIdentity.SourceCode(
                "original@erks.local",
                "shared-key"),
            StudioAlbumComponentIdentity.SourceCode(
                projected.RegisteredBy,
                projected.SourceKey));
    }

    [Fact]
    public void Project_PreservesDifferentRegistrantsThatShareASourceKey()
    {
        ProjectCloudSourceReference[] sources =
        [
            Source("same-key", "architect-a@erks.local", "architect-a@erks.local"),
            Source("same-key", "architect-b@erks.local", "architect-b@erks.local"),
        ];

        IReadOnlyList<StudioCloudSourcePackage> projected =
            StudioSharedSourceProjection.Create(sources);

        Assert.Equal(2, projected.Count);
        Assert.Equal(
            2,
            projected
                .Select(source => StudioAlbumComponentIdentity.SourceCode(
                    source.RegisteredBy,
                    source.SourceKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Fact]
    public void Project_FallsBackToLegacyOwnerWhenRegistrantIsMissing()
    {
        ProjectCloudSourceReference source = Source(
            "legacy-key",
            registeredBy: "",
            ownerEmail: "legacy-owner@erks.local");

        StudioCloudSourcePackage projected = Assert.Single(
            StudioSharedSourceProjection.Create([source]));

        Assert.Equal("legacy-owner@erks.local", projected.RegisteredBy);
    }

    private static ProjectCloudSourceReference Source(
        string sourceKey,
        string registeredBy,
        string ownerEmail) => new()
    {
        SourceId = Guid.NewGuid().ToString("N"),
        SourceKey = sourceKey,
        SourceApplication = "Revit",
        SourceDocumentReference = sourceKey + ".rvt",
        ManifestId = sourceKey + "-manifest",
        ContentHash = new string('a', 64),
        SheetCount = 2,
        Status = "Registered",
        RegisteredBy = registeredBy,
        OwnerEmail = ownerEmail,
        RegisteredAtUtc = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero),
    };
}
