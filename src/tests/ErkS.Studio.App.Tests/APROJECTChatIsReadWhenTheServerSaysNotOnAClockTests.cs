using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The project chat was read every 12 s (Мастер, 2026-10-31; owner, 2026-10-24: «өөрчлөлтийг
/// мэдэрч уншдаг болгохгуй бол дэмий ачаалал үүсгэнэ»). The server tells Erk-S Sync a project's
/// chat changed; Sync tells Studio on the release line it already holds; Studio reads the chat
/// then, and only then. Only the project id travels.
/// </summary>
public sealed class APROJECTChatIsReadWhenTheServerSaysNotOnAClockTests
{
    [Fact]
    public void AChatEventNamesItsProject()
    {
        Assert.Equal("a1da1b71", StudioReleaseListener.ChatProjectIn("{\"event\":\"project-chat\",\"projectId\":\"a1da1b71\"}"));
    }

    [Fact]
    public void OtherLinesAreNotChat()
    {
        Assert.Null(StudioReleaseListener.ChatProjectIn("{\"event\":\"release\",\"productCode\":\"ErkS.Studio\",\"version\":\"v1\"}"));
        Assert.Null(StudioReleaseListener.ChatProjectIn("{\"event\":\"project-chat\"}"));
        Assert.Null(StudioReleaseListener.ChatProjectIn("not json"));
    }

    [Fact]
    public void NoClockReadsTheChat()
    {
        string shell = ReadAppSource("ShellView.cs") + ReadAppSource("ShellView.Chat.cs");

        Assert.DoesNotContain("projectChatRefreshTimer", shell, StringComparison.Ordinal);
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
