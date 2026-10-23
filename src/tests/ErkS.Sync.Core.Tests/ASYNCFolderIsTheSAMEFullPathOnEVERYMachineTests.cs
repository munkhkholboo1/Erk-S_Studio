namespace ErkS.Sync.Core.Tests;

/// <summary>
/// One project folder sits at exactly one full path on every member's machine.
///
/// 🔴 Owner, 2026-10-23: xrefs and Revit links hold absolute paths, and they keep working on a
/// colleague's machine only because the folder is at the same place there. A join at another
/// path is refused with the path it should have - never accepted and «fixed» later.
/// </summary>
public sealed class ASYNCFolderIsTheSAMEFullPathOnEVERYMachineTests
{
    [Theory]
    [InlineData(@"D:\Cloud Work\Erin", @"d:\cloud work\erin")]
    [InlineData(@"D:\Cloud Work\Erin\", @"D:\Cloud Work\Erin")]
    [InlineData(@"D:/Cloud Work/Erin", @"D:\Cloud Work\Erin")]
    [InlineData(@"D:\Cloud Work\x\..\Erin", @"D:\Cloud Work\Erin")]
    public void SpellingIsNotIdentity(string left, string right) =>
        Assert.True(SyncPath.Same(left, right));

    [Fact]
    public void TheDriveLetterIsWrittenOneWay() =>
        Assert.Equal(@"D:\Cloud Work\Erin", SyncPath.Canonical(@"d:\Cloud Work\Erin\"));

    [Fact]
    public void ADifferentPlaceIsADifferentFolder() =>
        Assert.False(SyncPath.Same(@"D:\Cloud Work\Erin", @"E:\Cloud Work\Erin"));

    [Fact]
    public void AJoinAtANOTHERPathIsRefusedNamingTheRightOne()
    {
        using var folder = new TemporaryFolder();

        SyncFolderRefusal? refusal = SyncFolderEligibility.CheckJoin(folder.Path, @"D:\Cloud Work\Erin");

        Assert.Equal(SyncFolderEligibility.PathMismatchCode, refusal?.Code);
        Assert.Contains(@"D:\Cloud Work\Erin", refusal!.Message);
    }

    [Fact]
    public void AJoinAtTheRegisteredPathPasses()
    {
        using var folder = new TemporaryFolder();
        folder.File("plan.dwg");

        Assert.Null(SyncFolderEligibility.CheckJoin(folder.Path, folder.Path.ToUpperInvariant() + "\\"));
    }

    [Fact]
    public void ADriveRootIsNeverAProjectFolder()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(SyncFolderEligibility.DriveRootCode, SyncFolderEligibility.CheckMake(root)?.Code);
    }

    [Fact]
    public void AFolderINSIDEASyncFolderIsRefused()
    {
        using var folder = new TemporaryFolder();
        SyncFolderMarker.Write(folder.Path, Identity(folder.Path));
        string inner = folder.Folder("Revit");

        Assert.Equal(SyncFolderEligibility.InsideSyncFolderCode, SyncFolderEligibility.CheckMake(inner)?.Code);
    }

    [Fact]
    public void AFolderHOLDINGASyncFolderIsRefused()
    {
        using var folder = new TemporaryFolder();
        string inner = folder.Folder("Erin");
        SyncFolderMarker.Write(inner, Identity(inner));

        Assert.Equal(SyncFolderEligibility.ContainsSyncFolderCode, SyncFolderEligibility.CheckMake(folder.Path)?.Code);
    }

    [Fact]
    public void AnALREADYRegisteredFolderIsNotRegisteredTwice()
    {
        using var folder = new TemporaryFolder();
        SyncFolderMarker.Write(folder.Path, Identity(folder.Path));

        Assert.Equal(SyncFolderEligibility.AlreadySyncFolderCode, SyncFolderEligibility.CheckMake(folder.Path)?.Code);
    }

    [Fact]
    public void TheMarkerRoundTripsAndHidesItsFolder()
    {
        using var folder = new TemporaryFolder();
        SyncFolderIdentity written = Identity(folder.Path);

        SyncFolderMarker.Write(folder.Path, written);

        Assert.Equal(written, SyncFolderMarker.TryRead(folder.Path));
        Assert.True((File.GetAttributes(SyncFolderMarker.DirectoryOf(folder.Path)) & FileAttributes.Hidden) != 0);
    }

    [Fact]
    public void AnUnregisteredFolderLosesONLYItsOwnMarkerAndKeepsItsFiles()
    {
        using var folder = new TemporaryFolder();
        folder.File("plan.dwg", "x");
        SyncFolderMarker.Write(folder.Path, Identity(folder.Path));

        Assert.False(SyncFolderMarker.Remove(folder.Path, "another-folder"));
        Assert.NotNull(SyncFolderMarker.TryRead(folder.Path));

        Assert.True(SyncFolderMarker.Remove(folder.Path, "f-1"));
        Assert.Null(SyncFolderMarker.TryRead(folder.Path));
        Assert.False(Directory.Exists(SyncFolderMarker.DirectoryOf(folder.Path)));
        Assert.True(File.Exists(Path.Combine(folder.Path, "plan.dwg")));
        Assert.False(SyncFolderMarker.Remove(folder.Path, "f-1"));
    }

    internal static SyncFolderIdentity Identity(string folder) => new()
    {
        FolderId = "f-1",
        ProjectCode = "MAD-2026-ЕТ-05",
        ProjectId = "p-1",
        CanonicalPath = SyncPath.Canonical(folder),
        CreatedAtUtc = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero),
    };
}
