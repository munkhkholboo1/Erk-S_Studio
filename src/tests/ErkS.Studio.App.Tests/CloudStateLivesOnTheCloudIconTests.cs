using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// 🔴 WHERE THE STATE IS SHOWN IS PART OF THE REQUIREMENT, not a detail of it.
///
/// The user asked for the cloud icon they already had to be told apart by
/// colour. What got built the first time was a separate coloured badge on the
/// album toolbar - and that toolbar is the one thing they had asked to make
/// SHORTER, so it was the opposite of the request in two directions at once.
///
/// This file pins the correction, because "add a small indicator here" is an
/// easy edit to make again.
/// </summary>
public sealed class CloudStateLivesOnTheCloudIconTests
{
    [Fact]
    public void THEStateColoursTheCloudIconOnTheProjectCard()
    {
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        string shell = ReadAppSource("ShellView.cs");

        // The glyph that changes colour is the one already sitting on the
        // project card, not a new element.
        Assert.Contains("cloudStateGlyph.Foreground = colour;", refresh, StringComparison.Ordinal);
        Assert.Contains("cloudStateGlyph = cloudGlyph;", shell, StringComparison.Ordinal);
        Assert.Contains("DockPanel.SetDock(cloudSyncButton, Dock.Right);", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void THEAlbumToolbarCarriesNOIndicatorElement()
    {
        // The row the user wants reduced to one action must not grow one.
        string workspaces = ReadAppSource("ShellView.Workspaces.cs");
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");

        Assert.DoesNotContain("documentGroup.Children.Add(cloudAlbumIndicator)", workspaces, StringComparison.Ordinal);
        Assert.DoesNotContain("cloudAlbumIndicator", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("cloudAlbumIndicatorBadge", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void COLOURIsNotTheONLYChannel()
    {
        // Master's standing condition, and it survives the move: a count under
        // the glyph plus the tooltip carry the same information the colour
        // does, so the state is readable without colour vision.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");

        // 🔴 SCOPED TO THE SHOWN PATH. The first version asserted that
        // "cloudStateCount.Text =" appeared anywhere in the file - and it does,
        // in the branch that CLEARS the count for a local-only project. Deleting
        // the line that actually shows the number left that test green. Caught
        // by mutation, and it is the third time today an assertion has matched
        // something other than what it meant.
        int method = refresh.IndexOf("private void RefreshCloudAlbumIndicator()", StringComparison.Ordinal);
        Assert.True(method > 0, "the painter is gone");
        // Bounded by the NEXT MEMBER'S SIGNATURE rather than by a newline
        // escape. Escape sequences do not survive the tooling that writes this
        // file, and a mangled one turns into a real line break inside a string
        // literal - which fails loudly, but only after wasting a build.
        string body = refresh[method..refresh.IndexOf(
            "private static string CountTextOf(", method, StringComparison.Ordinal)];

        int shown = body.IndexOf("Brush colour = IndicatorBrush(status.State);", StringComparison.Ordinal);
        Assert.True(shown > 0, "the shown path is gone");
        string shownPath = body[shown..];

        Assert.Contains("CountTextOf(status)", shownPath, StringComparison.Ordinal);
        Assert.Contains("cloudSyncButton.ToolTip =", shownPath, StringComparison.Ordinal);
        Assert.Contains("status.SummaryMn", shownPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ANUnknownCloudDoesNotPaintTheIconGREEN()
    {
        // The rule that survives every move: green is a claim about the cloud
        // and may only be shown once the cloud has answered.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        int table = refresh.IndexOf("private static Brush IndicatorBrush(", StringComparison.Ordinal);
        Assert.True(table > 0, "the colour table is gone");

        string body = refresh[table..refresh.IndexOf("\n    };", table, StringComparison.Ordinal)];
        int fallthrough = body.IndexOf("_ =>", StringComparison.Ordinal);
        Assert.True(fallthrough > 0, "the colour table has no default arm");
        Assert.Contains("MutedTextBrush", body[fallthrough..], StringComparison.Ordinal);
        Assert.DoesNotContain("SuccessBrush", body[fallthrough..], StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalOnlyProjectShowsNoStateOnTheIcon()
    {
        // A project with no cloud behind it has nothing to be behind on, so the
        // icon must not display a state it cannot have.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        int method = refresh.IndexOf("private void RefreshCloudAlbumIndicator()", StringComparison.Ordinal);
        Assert.True(method > 0, "the painter is gone");

        string body = refresh[method..refresh.IndexOf("\n    /// <summary>", method, StringComparison.Ordinal)];
        Assert.Contains("status.ShouldShow", body, StringComparison.Ordinal);
        Assert.Contains("cloudStateCount.Text = \"\";", body, StringComparison.Ordinal);
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8).Replace("\r\n", "\n");
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
