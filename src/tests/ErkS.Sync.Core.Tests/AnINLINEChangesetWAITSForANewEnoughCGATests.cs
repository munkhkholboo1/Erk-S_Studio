namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync / CGA v0.081, 2026-10-28: simple objects travel inline in the manifest (schema 2) and the
/// changeset may have no entities at all. A CGA older than 0.081 - or one that never said its version -
/// would move its ack past objects it cannot read, so it is not given such a changeset: that writer's log
/// stops there (nothing after it either), the others go on, and the machine shows «CGA хоцорсон».
/// </summary>
public sealed class AnINLINEChangesetWAITSForANewEnoughCGATests
{
    [Fact]
    public void AMANIFESTOnlyChangesetIsCarriedAndGiven()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string dwg = Path.Combine(root.Path, "plan.dwg");
        File.WriteAllText(dwg, "base");
        SyncCollabFile file = SyncCollab.Register(root.Path, dwg, "", "", "0.081", DateTimeOffset.UtcNow, "seat-a_d1");

        long seq = SyncCollab.Publish(root.Path, file.FileId, "seat-a_d1", Manifest(scratch, "m1.json", 2), "");

        IReadOnlyList<SyncCollabChangeset> pending = SyncCollab.Pull(root.Path, new SyncHome(home.Path), file.FileId, "seat-b_d2", null, "0.081", out int held);
        SyncCollabChangeset only = Assert.Single(pending);
        Assert.Equal((1L, ""), (seq, only.EntitiesPath));
        Assert.Contains("\"schema\":2", File.ReadAllText(only.ManifestPath));
        Assert.Equal(0, held);
        Assert.True(SyncCollab.Status(root.Path, new SyncHome(home.Path), file.FileId, "seat-b_d2").EntitiesOptional);
    }

    [Fact]
    public void AnOLDOrUNKNOWNCgaStopsAtTheFirstInlineChangeset()
    {
        using var root = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string dwg = Path.Combine(root.Path, "plan.dwg");
        File.WriteAllText(dwg, "base");
        SyncCollabFile file = SyncCollab.Register(root.Path, dwg, "", "", "0.081", DateTimeOffset.UtcNow, "seat-a_d1");
        string entities = Path.Combine(scratch.Path, "e.dwg");
        File.WriteAllText(entities, "e");
        SyncCollab.Publish(root.Path, file.FileId, "seat-a_d1", Manifest(scratch, "m1.json", 1), entities);
        SyncCollab.Publish(root.Path, file.FileId, "seat-a_d1", Manifest(scratch, "m2.json", 2), "");
        SyncCollab.Publish(root.Path, file.FileId, "seat-a_d1", Manifest(scratch, "m3.json", 1), entities);

        foreach (string old in new[] { "0.080", "v0.079+rg4", "" })
        {
            using var home = new TemporaryFolder();
            IReadOnlyList<SyncCollabChangeset> pending = SyncCollab.Pull(root.Path, new SyncHome(home.Path), file.FileId, "seat-b_d2", null, old, out int held);
            Assert.Equal([1L], pending.Select(item => item.Seq)); // 2 is held, and 3 waits behind it
            Assert.Equal(2, held);
            Assert.Equal(2, SyncCollab.Status(root.Path, new SyncHome(home.Path), file.FileId, "seat-b_d2").HeldForNewerCga);
        }

        using var newer = new TemporaryFolder();
        Assert.Equal([1L, 2L, 3L], SyncCollab.Pull(root.Path, new SyncHome(newer.Path), file.FileId, "seat-b_d2", null, "0.081+rg12", out int none).Select(item => item.Seq));
        Assert.Equal(0, none);
    }

    [Fact]
    public void APartnerWithAnOLDCgaIsShownCGABEHIND()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        string dwg = Path.Combine(root.Path, "plan.dwg");
        File.WriteAllText(dwg, "base");
        SyncCollabFile file = SyncCollab.Register(root.Path, dwg, "", "", "0.081", DateTimeOffset.UtcNow, "seat-a_d1");
        SyncCollab.Register(root.Path, dwg, file.FileId, "", "0.079", DateTimeOffset.UtcNow, "seat-b_d2");
        SyncCollab.Publish(root.Path, file.FileId, "seat-a_d1", Manifest(scratch, "m1.json", 2), "");
        SyncCollab.Report(root.Path, file.FileId, "seat-a_d1", new Dictionary<string, long> { ["seat-a_d1"] = 1 }, "x", DateTimeOffset.UtcNow);
        SyncCollab.Report(root.Path, file.FileId, "seat-b_d2", new Dictionary<string, long>(), "y", DateTimeOffset.UtcNow);

        SyncCollabHealth partner = Assert.Single(SyncCollab.Status(root.Path, new SyncHome(home.Path), file.FileId, "seat-a_d1").Health);

        Assert.Equal(("seat-b_d2", "cga-behind"), (partner.Writer, partner.State));
    }

    [Theory]
    [InlineData("0.081", true)]
    [InlineData("v0.081", true)]
    [InlineData("0.082+rg5", true)]
    [InlineData("1.0", true)]
    [InlineData("0.080", false)]
    [InlineData("", false)]
    [InlineData("dev", false)]
    public void ACgaVersionIsCOMPAREDPartByPart(string version, bool enough) =>
        Assert.Equal(enough, SyncCollab.CgaAtLeast(version, SyncCollab.InlineSchemaMinCga));

    private static string Manifest(TemporaryFolder scratch, string name, int schema)
    {
        string path = Path.Combine(scratch.Path, name);
        File.WriteAllText(path, schema >= 2 ? $"{{\"schema\":{schema},\"inline\":[]}}" : "{\"handles\":[]}");
        return path;
    }
}
