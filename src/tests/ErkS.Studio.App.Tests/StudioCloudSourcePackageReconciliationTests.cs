using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCloudSourcePackageReconciliationTests
{
    [Fact]
    public void ActiveCanonicalDropsShadowedLegacySnapshotsAndKeepsDistinctKeyedStreams()
    {
        DateTimeOffset legacyAt = new(2026, 7, 16, 4, 0, 0, TimeSpan.Zero);
        StudioCloudSourcePackage legacy = Source(
            "legacy",
            "",
            "Same name.rvt",
            legacyAt);
        StudioCloudSourcePackage first = Source(
            "first",
            "source-a",
            "Same name.rvt",
            legacyAt.AddDays(1));
        StudioCloudSourcePackage second = Source(
            "second",
            "source-b",
            "Same name.rvt",
            legacyAt.AddDays(2));

        IReadOnlyList<StudioCloudSourcePackage> result =
            StudioCloudSourcePackageReconciliation.ActiveCanonical([legacy, first, second]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, source => source.SourceKey == "source-a");
        Assert.Contains(result, source => source.SourceKey == "source-b");
        Assert.DoesNotContain(result, source => source.SourceId == "legacy");
    }

    [Fact]
    public void ActiveCanonicalPreservesUnmatchedLegacySourceForBackwardCompatibility()
    {
        DateTimeOffset at = new(2026, 7, 16, 4, 0, 0, TimeSpan.Zero);
        StudioCloudSourcePackage legacy = Source("legacy", "", "Legacy only.rvt", at);
        StudioCloudSourcePackage keyed = Source("keyed", "source-a", "Other.rvt", at.AddDays(1));

        IReadOnlyList<StudioCloudSourcePackage> result =
            StudioCloudSourcePackageReconciliation.ActiveCanonical([legacy, keyed]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, source => source.SourceId == "legacy");
        Assert.Contains(result, source => source.SourceId == "keyed");
    }

    [Fact]
    public void ActiveCanonicalKeepsOnlyLatestRegisteredSnapshotForOneKey()
    {
        DateTimeOffset at = new(2026, 7, 16, 4, 0, 0, TimeSpan.Zero);
        StudioCloudSourcePackage old = Source("old", "source-a", "Building.rvt", at);
        StudioCloudSourcePackage current = Source("current", "source-a", "Building.rvt", at.AddHours(1));

        StudioCloudSourcePackage result = Assert.Single(
            StudioCloudSourcePackageReconciliation.ActiveCanonical([old, current]));

        Assert.Equal("current", result.SourceId);
    }

    [Fact]
    public void ActiveCanonicalKeepsTheSameKeyFromDifferentContributors()
    {
        DateTimeOffset at = new(2026, 7, 21, 4, 0, 0, TimeSpan.Zero);
        StudioCloudSourcePackage first = Source("first", "same-key", "ATD.pdf", at);
        StudioCloudSourcePackage second = Source("second", "same-key", "ATD.pdf", at.AddMinutes(1));
        second.RegisteredBy = "architect-b@erks.local";

        IReadOnlyList<StudioCloudSourcePackage> result =
            StudioCloudSourcePackageReconciliation.ActiveCanonical([first, second]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, source => source.RegisteredBy == "architect@erks.local");
        Assert.Contains(result, source => source.RegisteredBy == "architect-b@erks.local");
    }

    private static StudioCloudSourcePackage Source(
        string sourceId,
        string sourceKey,
        string reference,
        DateTimeOffset registeredAt) => new()
    {
        SourceId = sourceId,
        SourceKey = sourceKey,
        SourceApplication = "Revit",
        SourceDocumentReference = reference,
        ManifestId = "manifest-" + sourceId,
        ContentHash = "hash-" + sourceId,
        Status = "Registered",
        RegisteredBy = "architect@erks.local",
        RegisteredAtUtc = registeredAt,
        ExportedAtUtc = registeredAt,
    };
}
