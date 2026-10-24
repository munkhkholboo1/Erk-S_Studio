namespace ErkS.Sync.Core.Tests;

/// <summary>
/// A machine's own working files never enter the index: Revit local copies and backups,
/// AutoCAD locks and autosaves, Office owner files, temporary files, SYN's own folder.
///
/// 🔴 RULES, NOT A LIST SOMEBODY EDITS (plan §4.6). These are what turned a Resilio-synced
/// project into conflict copies; they are decided here, once, for every folder.
/// </summary>
public sealed class AMACHINESOwnDebrisNEVERTravelsTests
{
    [Theory]
    [InlineData("plan.dwl")]
    [InlineData("plan.dwl2")]
    [InlineData("plan.bak")]
    [InlineData("plan_1_2_3456.sv$")]
    [InlineData("~$Тайлбар бичиг.docx")]
    [InlineData("Thumbs.db")]
    [InlineData("export.tmp")]
    [InlineData("Model.0003.rvt")]
    [InlineData("Door.0001.rfa")]
    [InlineData("big.dwg.!sync")]
    public void ScratchFilesAreExcluded(string name) =>
        Assert.True(SyncExclusions.IsExcludedFile(name));

    [Theory]
    [InlineData("plan.dwg")]
    [InlineData("Model.rvt")]
    [InlineData("Model 2026.rvt")]
    [InlineData("ЕТ-01.pdf")]
    [InlineData("diagram.erks")]
    public void WorkIsKept(string name) =>
        Assert.False(SyncExclusions.IsExcludedFile(name));

    [Fact]
    public void ARevitLOCALCopyBesideItsCentralNeverTravels()
    {
        using var folder = new TemporaryFolder();
        folder.File(@"Revit\Erin.rvt");
        folder.File(@"Revit\Erin_backup\incrementtable.1234.dat");
        folder.File(@"Revit\Erin_munkh.rvt");
        folder.File(@"Revit\Erin_munkh_backup\x.dat");
        folder.File(@"Revit\Erin_munkh.0002.rvt");
        folder.File(@"Revit\Link.rvt");

        SyncIndex index = SyncIndexBuilder.Build(folder.Path, "f-1");

        string[] paths = index.Entries.Select(entry => entry.RelativePath).ToArray();
        Assert.Contains(@"Revit\Erin.rvt", paths);
        Assert.Contains(@"Revit\Link.rvt", paths);
        Assert.Contains(@"Revit\Erin_backup\incrementtable.1234.dat", paths);
        Assert.DoesNotContain(paths, path => path.Contains("munkh", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(SyncFileKind.RevitCentral, Kind(index, @"Revit\Erin.rvt"));
        Assert.Equal(SyncFileKind.RevitCentralData, Kind(index, @"Revit\Erin_backup\incrementtable.1234.dat"));
        Assert.Equal(SyncFileKind.RevitModel, Kind(index, @"Revit\Link.rvt"));
    }

    [Fact]
    public void SYNSOwnFolderAndResiliosAreNeverIndexed()
    {
        using var folder = new TemporaryFolder();
        folder.File("plan.dwg");
        SyncFolderMarker.Write(folder.Path, ASYNCFolderIsTheSAMEFullPathOnEVERYMachineTests.Identity(folder.Path));

        SyncIndex index = SyncIndexBuilder.Build(folder.Path, "f-1");

        Assert.Equal(["plan.dwg"], index.Entries.Select(entry => entry.RelativePath));
        Assert.Equal(SyncFileKind.Dwg, index.Entries[0].Kind);
    }

    [Fact]
    public void AFolderResilioKEEPSIsRefusedAndTheMarkerNamed()
    {
        using var folder = new TemporaryFolder();
        folder.File(@"DWG\big.dwg.!sync");

        SyncFolderRefusal? refusal = SyncFolderEligibility.CheckMake(folder.Path);

        Assert.Equal(SyncFolderEligibility.ResilioActiveCode, refusal?.Code);
        Assert.Contains("big.dwg.!sync", refusal!.Message);
    }

    [Fact]
    public void AResilioShareABOVETheFolderIsFoundToo()
    {
        using var folder = new TemporaryFolder();
        folder.Folder(".sync");
        string project = folder.Folder("Erin");

        Assert.Equal(SyncFolderEligibility.ResilioActiveCode, SyncFolderEligibility.CheckMake(project)?.Code);
    }

    // 🔴 The owner's Resilio share is the whole «D:\Cloud Work» (2026-10-24). Moving a project to
    // SYN one at a time (plan §4.6 «төсөл төслөөр шилжинэ») means Resilio stops keeping THAT
    // folder - which, inside one big share, is its IgnoreList. A folder Resilio ignores is free.
    [Theory]
    [InlineData("/Erin-test")]
    [InlineData("Erin-test")]
    [InlineData("/Erin-*")]
    [InlineData("\\Erin-test\\")]
    public void AFolderTheShareIGNORESIsNoLongerResilios(string ignoreLine)
    {
        using var share = new TemporaryFolder();
        share.File(@".sync\IgnoreList", "# OS generated files #\r\nThumbs.db\r\n" + ignoreLine + "\r\n");
        string project = share.Folder("Erin-test");
        string inside = share.Folder(@"Erin-test\DWG");

        Assert.Null(SyncFolderEligibility.CheckMake(project));
        Assert.Null(ResilioDetector.FindMarker(inside));
    }

    [Fact]
    public void AnIgnoreLineForANOTHERFolderLeavesThisOneResilios()
    {
        using var share = new TemporaryFolder();
        share.File(@".sync\IgnoreList", "/Erin-test\r\n*.tmp\r\n");
        string project = share.Folder("Grand mark");

        Assert.Equal(SyncFolderEligibility.ResilioActiveCode, SyncFolderEligibility.CheckMake(project)?.Code);
    }

    private static SyncFileKind Kind(SyncIndex index, string path) =>
        index.Entries.Single(entry => entry.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)).Kind;
}
