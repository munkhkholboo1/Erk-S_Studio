using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSourceSyncSnapshotGuardTests
{
    [Fact]
    public void GuardAcceptsExactLibraryVersion()
    {
        var library = new SheetLibrary();
        long version = StudioSourceSyncSnapshotGuard.Capture(library);

        StudioSourceSyncSnapshotGuard.Require(
            library,
            version,
            "cloud_sync");

        Assert.True(
            StudioSourceSyncSnapshotGuard.IsCurrent(library, version));
    }

    [Fact]
    public void GuardRejectsLibraryResetAfterPreview()
    {
        var library = new SheetLibrary();
        long version = StudioSourceSyncSnapshotGuard.Capture(library);

        library.Clear();

        Assert.False(
            StudioSourceSyncSnapshotGuard.IsCurrent(library, version));
        Assert.Throws<StudioSourceSyncSnapshotChangedException>(() =>
            StudioSourceSyncSnapshotGuard.Require(
                library,
                version,
                "cloud_sync"));
    }
}
