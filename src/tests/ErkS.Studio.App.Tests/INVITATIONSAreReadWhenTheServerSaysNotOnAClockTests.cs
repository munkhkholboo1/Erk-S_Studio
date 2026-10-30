using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Invitations were read every 45 s (Мастер, 2026-10-31). The server now tells Erk-S Sync the
/// account's invitations changed («notifications», kind only, SRV 8e4f05e); Sync tells Studio on
/// the release line; Studio reads them then, at sign-in and on the Projects page.
/// </summary>
public sealed class INVITATIONSAreReadWhenTheServerSaysNotOnAClockTests
{
    [Fact]
    public void ANotificationsLineNamesItsKind()
    {
        Assert.Equal(
            "project-membership-invitation",
            StudioReleaseListener.NotificationsKindIn("{\"event\":\"notifications\",\"kind\":\"project-membership-invitation\"}"));
        Assert.Equal("", StudioReleaseListener.NotificationsKindIn("{\"event\":\"notifications\"}"));
    }

    [Fact]
    public void OtherLinesAreNotNotifications()
    {
        Assert.Null(StudioReleaseListener.NotificationsKindIn("{\"event\":\"project-chat\",\"projectId\":\"a\"}"));
        Assert.Null(StudioReleaseListener.NotificationsKindIn("not json"));
    }

    /// <summary>
    /// The 45 s clock read invitations, team presence and project access. All three are now
    /// server events (SRV 8e4f05e, db93505), and the clock is gone.
    /// </summary>
    [Fact]
    public void NoClockReadsTheServer()
    {
        string shell = ReadAppSource("ShellView.cs");

        Assert.DoesNotContain("notificationRefreshTimer", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessAndPresenceLinesNameTheirProject()
    {
        Assert.Equal("p1", StudioReleaseListener.ProjectIn("{\"event\":\"project-access\",\"projectId\":\"p1\"}", "project-access"));
        Assert.Equal("p2", StudioReleaseListener.ProjectIn("{\"event\":\"presence\",\"projectId\":\"p2\"}", "presence"));
        Assert.Null(StudioReleaseListener.ProjectIn("{\"event\":\"presence\",\"projectId\":\"p2\"}", "project-access"));
        Assert.Null(StudioReleaseListener.ProjectIn("{\"event\":\"presence\"}", "presence"));
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
