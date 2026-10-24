namespace ErkS.Studio.App.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24: «хэр хэмжээний файл хэдэн хувьтай синк хийгдсэнийг нь мэдэх хэрэгтэй» -
/// and, about the first way it was shown, «2 секунт тутам нэг юм уншаад байна гэсэн үг үү?
/// өөрчлөлтийг мэдэрч уншдаг болгохгуй бол дэмий ачаалал үүсгэнэ». The section shows how far in
/// step and what is arriving, and reads the tray's state only when Windows says it was written.
/// </summary>
public sealed class THESyncSectionFOLLOWSChangeNotAClockTests
{
    [Fact]
    public void TheSectionIsDrivenByAFileWatcherAndShowsTheProgress()
    {
        string sync = File.ReadAllText(Path.Combine(AppSourceRoot(), "ShellView.Sync.cs"));

        Assert.DoesNotContain("DispatcherTimer", sync, StringComparison.Ordinal);
        Assert.Contains("new FileSystemWatcher(", sync, StringComparison.Ordinal);
        Assert.Contains("peer.SyncedPercent", sync, StringComparison.Ordinal);
        Assert.Contains("peer.TransferDoneBytes", sync, StringComparison.Ordinal);
        Assert.Contains("бүрэн синк хийгдсэн", sync, StringComparison.Ordinal);
    }

    private static string AppSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App");
    }
}
