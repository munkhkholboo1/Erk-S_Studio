namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Two machines' copies are compared by path and hash; what differs is listed, never chosen.
///
/// 🔴 Owner, 2026-10-23: a colleague's non-empty folder keeps every file that matches and
/// shows the rest as «зөрүүтэй» for a person to decide. Nothing here may pick a winner.
/// </summary>
public sealed class ADIFFERENTFileIsSHOWNNeverOverwrittenTests
{
    [Fact]
    public void SameDifferentAndOneSidedFilesAreTOLDApart()
    {
        using var here = new TemporaryFolder();
        using var there = new TemporaryFolder();
        here.File("same.dwg", "a");
        there.File("SAME.dwg", "a");
        here.File("changed.pdf", "mine");
        there.File("changed.pdf", "theirs");
        here.File(@"only\here.pdf");
        there.File(@"only\there.pdf");

        SyncIndexDifference difference = SyncIndexComparison.Compare(
            SyncIndexBuilder.Build(here.Path, "f-1"),
            SyncIndexBuilder.Build(there.Path, "f-1"));

        Assert.Equal(1, difference.SameCount);
        Assert.Equal(["changed.pdf"], difference.Different.Select(entry => entry.RelativePath));
        Assert.Equal([@"only\here.pdf"], difference.OnlyHere.Select(entry => entry.RelativePath));
        Assert.Equal([@"only\there.pdf"], difference.OnlyThere.Select(entry => entry.RelativePath));
        Assert.False(difference.IsIdentical);
    }

    [Fact]
    public void ComparingNeverTouchesAFile()
    {
        using var here = new TemporaryFolder();
        using var there = new TemporaryFolder();
        string mine = here.File("changed.pdf", "mine");
        there.File("changed.pdf", "theirs");

        _ = SyncIndexComparison.Compare(SyncIndexBuilder.Build(here.Path, "f"), SyncIndexBuilder.Build(there.Path, "f"));

        Assert.Equal("mine", File.ReadAllText(mine));
    }

    [Fact]
    public void AnUNCHANGEDFileKeepsItsHashesWithoutBeingReadAgain()
    {
        using var folder = new TemporaryFolder();
        string file = folder.File("model.rvt", "v1");
        SyncIndex first = SyncIndexBuilder.Build(folder.Path, "f");
        // Same size and write time, different bytes: a re-read would see it, a reuse does not.
        DateTime written = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, "v2");
        File.SetLastWriteTimeUtc(file, written);

        SyncIndex second = SyncIndexBuilder.Build(folder.Path, "f", first);

        Assert.Equal(first.Entries[0].Sha256, second.Entries[0].Sha256);
    }

    [Fact]
    public void ALargeFileIsHashedInChunksAndAsAWhole()
    {
        using var folder = new TemporaryFolder();
        string file = Path.Combine(folder.Path, "big.dwg");
        File.WriteAllBytes(file, new byte[SyncIndex.ChunkSize + 10]);

        SyncIndexEntry entry = SyncIndexBuilder.Build(folder.Path, "f").Entries.Single();

        Assert.Equal(2, entry.ChunkSha256.Count);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),
            entry.Sha256);
    }

    [Fact]
    public void TheIndexSurvivesItsOwnFileFormat()
    {
        using var folder = new TemporaryFolder();
        folder.File("plan.dwg", "a");
        SyncIndex index = SyncIndexBuilder.Build(folder.Path, "f");
        string stored = Path.Combine(folder.Path, "state", "index.json");

        SyncIndexStore.Save(index, stored);
        SyncIndex? back = SyncIndexStore.TryLoad(stored);

        Assert.NotNull(back);
        Assert.True(SyncIndexComparison.Compare(index, back!).IsIdentical);
    }
}
