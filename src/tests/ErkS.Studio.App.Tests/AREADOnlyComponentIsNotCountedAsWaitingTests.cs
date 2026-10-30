namespace ErkS.Studio.App.Tests;

/// <summary>
/// Erin, 2026-10-31: the refresh ended on «⚠ Альбум бүрэн бус — 1 хэсэг илгээгдээгүй» for an ATD
/// component this device does not hold and the Cloud holds whole. The sync refused it, the
/// indicator's quicker rule counted it as sendable. What the sync refuses is counted blocked.
/// </summary>
public sealed class AREADOnlyComponentIsNotCountedAsWaitingTests
{
    [Fact]
    public void TheSyncRecordsWhatItRefusedAndTheSplitReadsIt()
    {
        string shell = ReadAppSource("ShellView.cs");
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");

        Assert.Contains("RememberSyncRefusedComponents(allPendingAlbumComponents, authorizedPendingAlbumComponents);", shell, StringComparison.Ordinal);
        Assert.Contains("renderable.Contains(code) && !syncRefusedComponentCodes.Contains(code)", refresh, StringComparison.Ordinal);
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
