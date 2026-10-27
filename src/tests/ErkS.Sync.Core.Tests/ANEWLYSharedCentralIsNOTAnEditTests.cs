namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-27: a DWG made shared kept its pre-collab bytes as the central; the first
/// checkpoint then took it for «edited directly» and warned. Those bytes are the base, not an edit.
/// </summary>
public sealed class ANEWLYSharedCentralIsNOTAnEditTests
{
    [Fact]
    public void TheFIRSTCheckpointReplacesThePreCollabCentralWithoutAWarning()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        var syncHome = new SyncHome(home.Path);
        string central = Path.Combine(root.Path, "DWG", "TuvAimagMP-V3.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(central)!);
        File.WriteAllText(central, "the drawing before it was shared");
        SyncCollabFile file = SyncCollab.Register(root.Path, central, "", "", "0.075", DateTimeOffset.UtcNow, "seat-a");
        Assert.Equal(64, file.BaseSha256.Length);

        // Another machine's first checkpoint, from its local (this machine has placed none).
        string local = Path.Combine(root.Path, "elsewhere.dwg");
        File.WriteAllText(local, "the shared drawing, checkpoint 1");
        SyncCollab.Checkpoint(root.Path, file.FileId, local, new Dictionary<string, long>());

        SyncCollab.Maintain(root.Path, syncHome);

        Assert.Equal("the shared drawing, checkpoint 1", File.ReadAllText(central));
        SyncCollabStatus status = SyncCollab.Status(root.Path, syncHome, file.FileId, "seat-a");
        Assert.False(status.CentralEdited);
        Assert.Equal(1, status.PlacedCheckpoint);
    }

    [Fact]
    public void ACentralSAVEDDirectlyIsStillKeptAndSaid()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        var syncHome = new SyncHome(home.Path);
        string central = Path.Combine(root.Path, "plan.dwg");
        File.WriteAllText(central, "base");
        SyncCollabFile file = SyncCollab.Register(root.Path, central, "", "", "0.075", DateTimeOffset.UtcNow, "seat-a");
        File.WriteAllText(central, "someone saved the central itself");
        string local = Path.Combine(root.Path, "elsewhere.dwg");
        File.WriteAllText(local, "checkpoint 1");
        SyncCollab.Checkpoint(root.Path, file.FileId, local, new Dictionary<string, long>());

        SyncCollab.Maintain(root.Path, syncHome);

        Assert.Equal("someone saved the central itself", File.ReadAllText(central));
        Assert.True(SyncCollab.Status(root.Path, syncHome, file.FileId, "seat-a").CentralEdited);
    }
}
