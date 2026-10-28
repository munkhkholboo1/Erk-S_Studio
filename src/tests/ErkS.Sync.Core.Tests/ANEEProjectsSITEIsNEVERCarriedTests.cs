namespace ErkS.Sync.Core.Tests;

/// <summary>
/// EE / Erk-S Sync, 2026-10-28 (_shared/ee-syn-project-sync-2026-10-28.md): an EE project's «.erks-sync-ignore»
/// keeps what the Engine builds again (site/, maps/*/site/, history/, *.eepack) off the wire - 6.4 GB of Altai
/// was going to a partner over a phone. The marker travels; what it excludes is never taken, and only an EE
/// project folder (one with an .eeproj) is honoured.
/// </summary>
public sealed class ANEEProjectsSITEIsNEVERCarriedTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";
    private const string Rules = "# EE\nsite/\nmaps/*/site/\nhistory/\n*.eepack\n";

    [Theory]
    [InlineData(@"EE\Altai\site\buildings\a.emsh", true)]
    [InlineData(@"EE\Altai\maps\m1\site\terrain.bin", true)]
    [InlineData(@"EE\Altai\history\collab-state.json", true)]
    [InlineData(@"EE\Altai\Altai.eepack", true)]
    [InlineData(@"EE\Altai\deep\x.eepack", true)]
    [InlineData(@"EE\Altai\edits.json", false)]
    [InlineData(@"EE\Altai\.erks-sync-ignore", false)]
    [InlineData(@"EE\Altai\maps\m1\edits.json", false)]
    [InlineData(@"Other\site\a.dwg", false)] // outside the marker's folder
    public void THERulesAreTheMarkersFOLDERs(string path, bool ignored)
    {
        using var root = new TemporaryFolder();
        string project = Path.Combine(root.Path, "EE", "Altai");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Altai.eeproj"), "{}");
        File.WriteAllText(Path.Combine(project, SyncIgnore.FileName), Rules);

        SyncIgnore ignore = SyncIgnore.Of(root.Path, [Path.Combine("EE", "Altai", SyncIgnore.FileName)]);

        Assert.Equal(ignored, ignore.Ignores(path));
    }

    [Fact]
    public void AMarkerOUTSIDEAnEEProjectIsNotHONOURED()
    {
        using var root = new TemporaryFolder();
        File.WriteAllText(Path.Combine(root.Path, SyncIgnore.FileName), "*.dwg\n");
        File.WriteAllText(Path.Combine(root.Path, "plan.dwg"), "x");

        SyncIndex index = SyncIndexBuilder.Build(root.Path, "f-1");

        Assert.Contains(index.Entries, entry => entry.RelativePath == "plan.dwg");
    }

    [Fact]
    public async Task THEIndexLEAVESItOutAndAPartnerNEVERTakesIt()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        var identity = new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical };
        string folderA = rootA.Folder("Erin"), folderB = rootB.Folder("Erin");
        SyncFolderMarker.Write(folderA, identity);
        SyncFolderMarker.Write(folderB, identity);
        string project = Path.Combine(folderA, "Erk-S_Engine", "Altai");
        Directory.CreateDirectory(Path.Combine(project, "site", "buildings"));
        File.WriteAllText(Path.Combine(project, "Altai.eeproj"), "{}");
        File.WriteAllText(Path.Combine(project, "edits.json"), "{\"hand\":1}");
        File.WriteAllText(Path.Combine(project, "site", "buildings", "ee_1.emsh"), "built again everywhere");

        // A's SYN before the marker: the site is in its index (an older partner, or before EE wrote the rule).
        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await engineA.PassAsync(CancellationToken.None);
        File.WriteAllText(Path.Combine(project, "Altai.eeproj"), "{}");
        // B already has the marker (it travelled first) and must not take the site A still offers.
        string projectB = Path.Combine(folderB, "Erk-S_Engine", "Altai");
        Directory.CreateDirectory(projectB);
        File.WriteAllText(Path.Combine(projectB, "Altai.eeproj"), "{}");
        File.WriteAllText(Path.Combine(projectB, SyncIgnore.FileName), Rules);

        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        await engineB.PassAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(projectB, "edits.json")));
        Assert.False(File.Exists(Path.Combine(projectB, "site", "buildings", "ee_1.emsh")));
        Assert.Equal(100, engineB.LastState.Folders.Single().Peers.Single().InPercent); // not waited for

        // A writes the marker: its own index leaves the site out from the next pass; the marker travels.
        File.WriteAllText(Path.Combine(project, SyncIgnore.FileName), Rules);
        await engineA.PassAsync(CancellationToken.None);
        SyncIndex indexA = SyncIndexStore.TryLoad(new SyncHome(homeA.Path).IndexPath("f-1"))!;
        Assert.DoesNotContain(indexA.Entries, entry => entry.RelativePath.Contains(@"\site\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(indexA.Entries, entry => entry.RelativePath.EndsWith(SyncIgnore.FileName, StringComparison.OrdinalIgnoreCase));
    }
}
