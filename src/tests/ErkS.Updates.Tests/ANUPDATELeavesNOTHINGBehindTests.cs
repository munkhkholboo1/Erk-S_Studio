namespace ErkS.Updates.Tests;

/// <summary>Master, 2026-10-27: every update's packages stayed in %LOCALAPPDATA% - gigabytes since July.</summary>
public sealed class ANUPDATELeavesNOTHINGBehindTests
{
    [Fact]
    public void AFINISHEDStagingKeepsOnlyItsLog()
    {
        string staging = Folder();
        File.WriteAllText(Path.Combine(staging, "apply.log"), "applied");
        File.WriteAllText(Path.Combine(staging, "target.json"), "{}");
        File.WriteAllText(Path.Combine(staging, "aabbcc"), "a staged file");
        Directory.CreateDirectory(Path.Combine(staging, "sub"));

        UpdateStaging.Finished(staging);

        Assert.Equal(["apply.log"], Directory.EnumerateFileSystemEntries(staging).Select(Path.GetFileName));
    }

    [Fact]
    public void FoldersOfINSTALLEDVersionsGoNEWEROnesStay()
    {
        string root = Folder();
        foreach (string name in new[] { "v0.001.13", "delta-v0.001.9", "delta-v0.001.130", "delta-v0.001.131", "v0.001.200", "notes" })
            Directory.CreateDirectory(Path.Combine(root, name));

        int removed = UpdateStaging.Tidy(root, "v0.001.130");

        Assert.Equal(3, removed);
        Assert.Equal(["delta-v0.001.131", "notes", "v0.001.200"], Directory.EnumerateDirectories(root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void VersionsCompareByNUMBERNotText()
    {
        Assert.True(UpdateStaging.Compare("v0.001.9", "0.001.10") < 0);
        Assert.Equal(0, UpdateStaging.Compare("Demo V0.001.130", "v0.001.130"));
    }

    private static string Folder()
    {
        string path = Path.Combine(Path.GetTempPath(), "erks-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
