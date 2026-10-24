namespace ErkS.Studio.App.Tests;

/// <summary>
/// 🔴 OWNER, 2026-10-24: «үндсэн эрхээрээ нэвтэрчихээд байхад энэ ботын пасс нэхэж нээгдээд байгааг
/// одоо болиул», «бот сонгоогүй байхад яагаад ингээд бот пасс байнга нэхэж нээгдээд байдаг юм».
/// A seated machine covered itself with the PIN screen at every start and lifted it only after the
/// session, the update check and the licence had all finished - so the owner, signed in on their own
/// machine, was asked for the bot's PIN every time. Their rule (written beside the release) is «Бот
/// төлөвт шилжсэн үед л пин асуудаг байя»: the start-up lock follows the LAST CHOICE, not the seat.
/// </summary>
public sealed class THEPinIsAskedONLYWhenTheBotWasChosenTests
{
    [Fact]
    public void ASeatWhoseBotWasNeverChosenOpensWithoutThePin()
    {
        Assert.False(StudioBotActor.LocksAtStartup(botChosen: null));
        Assert.False(StudioBotActor.LocksAtStartup(botChosen: false));
        Assert.True(StudioBotActor.LocksAtStartup(botChosen: true));
    }

    [Fact]
    public void TheChoiceIsKeptBESIDETheSeatNeverInIt()
    {
        string seat = File.ReadAllText(Path.Combine(AppSourceRoot(), "StudioBotDeviceState.cs"));
        string body = Body(File.ReadAllText(Path.Combine(AppSourceRoot(), "ShellView.BotSeat.cs")), "private static void RememberBotChoice(");

        Assert.DoesNotContain("BotChosen { get; set; }", seat, StringComparison.Ordinal);
        Assert.Contains("StudioBotChoiceStore.Write(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StudioBotDeviceStateStore.Write(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void EnteringBotStateRemembersItAndTheOwnerComingBackForgetsIt()
    {
        string source = File.ReadAllText(Path.Combine(AppSourceRoot(), "ShellView.BotSeat.cs"));

        Assert.Contains("RememberBotChoice(true)", Body(source, "private async Task EnterBotStateNowAsync("), StringComparison.Ordinal);
        Assert.Contains("RememberBotChoice(false)", Body(source, "private async Task ResumeAsOwnerNowAsync("), StringComparison.Ordinal);
        Assert.Contains("StudioBotActor.LocksAtStartup(", Body(source, "private void InstallBotLockIfSeated("), StringComparison.Ordinal);
    }

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        int next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    private static string AppSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App");
    }
}
