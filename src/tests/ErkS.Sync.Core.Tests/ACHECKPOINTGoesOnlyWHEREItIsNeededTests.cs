namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-27: a large CGA file's checkpoint (200–500 MB) went to every member at each local
/// save. Older checkpoints never travel; the newest only to a machine that needs it.
/// </summary>
public sealed class ACHECKPOINTGoesOnlyWHEREItIsNeededTests
{
    private const string FileId = "ee46d4b4c92a4e36b5f89e7f6185b966";

    [Fact]
    public void OLDCheckpointsNeverTravelTheNEWESTOnlyWhenNeeded()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        var syncHome = new SyncHome(home.Path);
        SyncIndexEntry[] theirs = [Checkpoint(1), Checkpoint(2), Checkpoint(3), Entry(@"DWG\plan.dwg")];
        var none = new HashSet<string>();

        // A machine with no local placed yet (a new member) needs a base: the newest only.
        Assert.Equal([Path(1), Path(2)], SyncCollab.CheckpointsNotWanted(root.Path, syncHome, theirs, none).Order());

        // Once a local is placed here, no checkpoint comes - the changesets carry the work …
        string dwg = System.IO.Path.Combine(root.Path, "made.dwg");
        File.WriteAllText(dwg, "drawing");
        SyncCollab.Checkpoint(root.Path, FileId, dwg, new Dictionary<string, long>(), syncHome);
        Assert.Equal([Path(1), Path(2), Path(3)], SyncCollab.CheckpointsNotWanted(root.Path, syncHome, theirs, none).Order());

        // … unless CGA asks for it (a new local): then the newest.
        Assert.Equal([Path(1), Path(2)], SyncCollab.CheckpointsNotWanted(root.Path, syncHome, theirs, new HashSet<string> { FileId }).Order());
    }

    [Fact]
    public void ANumberIsNEVERReusedForACheckpointNotFetchedHere()
    {
        using var root = new TemporaryFolder();
        string checkpoints = System.IO.Path.Combine(root.Path, SyncCollab.DirectoryName, FileId, "checkpoints");
        Directory.CreateDirectory(checkpoints);
        // Checkpoint 5's note arrived; its DWG did not (lazy).
        File.WriteAllText(System.IO.Path.Combine(checkpoints, "000005.json"), "{\"checkpoint\":5,\"through\":{}}");
        string dwg = System.IO.Path.Combine(root.Path, "made.dwg");
        File.WriteAllText(dwg, "drawing");

        int made = SyncCollab.Checkpoint(root.Path, FileId, dwg, new Dictionary<string, long>());

        Assert.Equal(6, made);
        Assert.Equal(6, SyncCollab.NewestCheckpoint(root.Path, FileId));
    }

    private static string Path(int number) => System.IO.Path.Combine(SyncCollab.DirectoryName, FileId, "checkpoints", number.ToString("D6") + ".dwg");

    private static SyncIndexEntry Checkpoint(int number) => Entry(Path(number));

    private static SyncIndexEntry Entry(string path) => new(path, 10, DateTime.UtcNow, SyncFileKinds.Of(path), "aa", ["aa"]);
}
