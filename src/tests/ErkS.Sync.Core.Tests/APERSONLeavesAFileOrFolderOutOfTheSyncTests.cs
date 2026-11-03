namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-11-03: «Erk-S Sync дээр төслийн фолдерт байгаа фолдер болон файлыг сонгож синк хийгддэггүй болгодог
/// болгох хэрэгтэй байна. тэгэхгүй бол зарим хэрэггүй том файлууд уншаад дуусахгүй байна». Explorer «Синк хийхгүй
/// болгох» writes a «/path» line into the sync folder's own «.erks-sync-ignore»: never read for the index, never
/// taken by a partner; «Синкт буцааж оруулах» takes it back.
/// </summary>
public sealed class APERSONLeavesAFileOrFolderOutOfTheSyncTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    private static string SyncFolder(TemporaryFolder root)
    {
        string folder = root.Folder("Erin");
        SyncFolderMarker.Write(folder, new SyncFolderIdentity { FolderId = "f-1", ProjectCode = "ERIN-2026", CanonicalPath = Canonical });
        return folder;
    }

    [Fact]
    public void ALEFTOutFolderAndFileAreNotReadAndCOMEBackWhenTakenBack()
    {
        using var root = new TemporaryFolder();
        string folder = SyncFolder(root);
        Directory.CreateDirectory(Path.Combine(folder, "Renders", "raw"));
        File.WriteAllText(Path.Combine(folder, "Renders", "raw", "frame.exr"), "big");
        File.WriteAllText(Path.Combine(folder, "scene.max"), "big");
        File.WriteAllText(Path.Combine(folder, "plan.dwg"), "keep");

        Assert.Equal(SyncIgnoreChange.Added, SyncIgnore.Exclude(folder, "Renders", isDirectory: true));
        Assert.Equal(SyncIgnoreChange.Added, SyncIgnore.Exclude(folder, "scene.max", isDirectory: false));
        Assert.Equal(SyncIgnoreChange.AlreadyExcluded, SyncIgnore.Exclude(folder, @"Renders\raw\frame.exr", isDirectory: false));

        SyncIndex index = SyncIndexBuilder.Build(folder, "f-1");
        Assert.DoesNotContain(index.Entries, entry => entry.RelativePath.StartsWith("Renders", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(index.Entries, entry => entry.RelativePath == "scene.max");
        Assert.Contains(index.Entries, entry => entry.RelativePath == "plan.dwg");
        Assert.Contains(index.Entries, entry => entry.RelativePath == SyncIgnore.FileName); // the rule travels
        Assert.Equal([@"Renders\", "scene.max"], SyncIgnore.Chosen(folder));

        Assert.Equal(SyncIgnoreChange.ExcludedByRule, SyncIgnore.Include(folder, @"Renders\raw\frame.exr", isDirectory: false));
        Assert.Equal(SyncIgnoreChange.Removed, SyncIgnore.Include(folder, "Renders", isDirectory: true));
        Assert.Equal(SyncIgnoreChange.NotExcluded, SyncIgnore.Include(folder, "Renders", isDirectory: true));

        index = SyncIndexBuilder.Build(folder, "f-1");
        Assert.Contains(index.Entries, entry => entry.RelativePath == @"Renders\raw\frame.exr");
        Assert.DoesNotContain(index.Entries, entry => entry.RelativePath == "scene.max");
    }

    [Fact]
    public void ACHOSENPathIsThatPathOnlyNotTheNameEverywhere()
    {
        using var root = new TemporaryFolder();
        string folder = SyncFolder(root);
        Directory.CreateDirectory(Path.Combine(folder, "Sub"));
        File.WriteAllText(Path.Combine(folder, "scene.max"), "x");
        File.WriteAllText(Path.Combine(folder, "Sub", "scene.max"), "x");
        File.WriteAllText(Path.Combine(folder, "# note [1].dwg"), "x");

        SyncIgnore.Exclude(folder, "scene.max", isDirectory: false);
        SyncIgnore.Exclude(folder, "# note [1].dwg", isDirectory: false);

        SyncIndex index = SyncIndexBuilder.Build(folder, "f-1");
        Assert.Contains(index.Entries, entry => entry.RelativePath == @"Sub\scene.max");
        Assert.DoesNotContain(index.Entries, entry => entry.RelativePath == "scene.max");
        Assert.DoesNotContain(index.Entries, entry => entry.RelativePath == "# note [1].dwg");
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(SyncIgnore.FileName)]
    [InlineData(@".erks-sync\state.json")]
    [InlineData(@".erks-collab\changesets")]
    public void THEFolderItselfAndSYNsOwnFilesAreNeverLeftOut(string path)
    {
        using var root = new TemporaryFolder();
        string folder = SyncFolder(root);

        Assert.Equal(SyncIgnoreChange.Refused, SyncIgnore.Exclude(folder, path, isDirectory: false));
        Assert.False(File.Exists(Path.Combine(folder, SyncIgnore.FileName)));
    }

    [Fact]
    public void AMARKEROutsideAProjectRootOrEEProjectIsStillNotHonoured()
    {
        using var root = new TemporaryFolder();
        string folder = SyncFolder(root);
        Directory.CreateDirectory(Path.Combine(folder, "Sub"));
        File.WriteAllText(Path.Combine(folder, "Sub", SyncIgnore.FileName), "*.dwg\n");
        File.WriteAllText(Path.Combine(folder, "Sub", "plan.dwg"), "x");

        SyncIndex index = SyncIndexBuilder.Build(folder, "f-1");

        Assert.Contains(index.Entries, entry => entry.RelativePath == @"Sub\plan.dwg");
    }

    [Fact]
    public async Task APARTNERNeverTakesWhatTheFolderLeftOut()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer(Canonical, [a, b]) { Relay = true };
        string folderA = SyncFolder(rootA), folderB = SyncFolder(rootB);
        File.WriteAllText(Path.Combine(folderA, "plan.pdf"), "keep");
        File.WriteAllText(Path.Combine(folderA, "scene.max"), "big and not needed");
        File.WriteAllText(Path.Combine(folderB, "notes.txt"), "B's own");
        SyncIgnore.Exclude(folderA, "scene.max", isDirectory: false);

        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await engineA.PassAsync(CancellationToken.None);
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        await engineB.PassAsync(CancellationToken.None);
        await engineB.PassAsync(CancellationToken.None); // the rule landing first makes the rest give way to a new pass

        Assert.True(File.Exists(Path.Combine(folderB, "plan.pdf")));
        Assert.True(File.Exists(Path.Combine(folderB, SyncIgnore.FileName)));
        Assert.False(File.Exists(Path.Combine(folderB, "scene.max")));
    }
}
