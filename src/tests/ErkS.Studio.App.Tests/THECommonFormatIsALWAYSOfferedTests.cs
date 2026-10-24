namespace ErkS.Studio.App.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24: «студио нийтлэг хуудасны формат сонгож тэр нь нүүр хуудасны форматыг
/// тохируулдаг байх ёстой. бид уг нь тэрийг урьд нь хийсэн байх ёстой». The selector existed but
/// was hidden whenever no format had been chosen - every album not made from a template - so the
/// cover fell back to A3. It is always offered now, and unchosen it shows the album's own format.
/// </summary>
public sealed class THECommonFormatIsALWAYSOfferedTests
{
    [Fact]
    public void TheSelectorIsNeverHiddenAndShowsTheAlbumsFormat()
    {
        string workspaces = File.ReadAllText(Path.Combine(AppSourceRoot(), "ShellView.Workspaces.cs"));

        Assert.DoesNotContain("albumGeneratedFormatPanel.Visibility = state.Album.GeneratedPageFormat is null", workspaces, StringComparison.Ordinal);
        Assert.Contains("albumGeneratedFormatPanel.Visibility = Visibility.Visible;", workspaces, StringComparison.Ordinal);
        Assert.Contains("AlbumBuilder.InferPageFormat(state.Album, state.Library)", workspaces, StringComparison.Ordinal);
        Assert.Contains("альбумын хуудаснаас", workspaces, StringComparison.Ordinal);
    }

    private static string AppSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App");
    }
}
