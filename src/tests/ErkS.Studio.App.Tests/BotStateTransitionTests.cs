using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Becoming a bot must change the RUNNING program, not only the disk.
///
/// 🔴 THE OWNER FOUND THIS AND GUESSED THE CAUSE CORRECTLY. «Төхөөрөмжийг бот
/// болгоход хэрэглэгчийн төслүүд хэвээрээ харагдаж үлдэж байна. Ботын удирдлага
/// гэх мэт үйлдлүүд байсаар байна. Магадгүй студиог бүрэн хааж нээхэд
/// засагддаг байх. Гэхдээ хэрэглэгч ийм илүү үйлдэл хийхгүй.»
///
/// They were right about the mechanism: the seat was written to disk and the
/// server erased the owner's credential, while the window carried on as that
/// person. The lock screen has exactly one caller - start-up - so reopening
/// Studio was the only thing that applied the change.
///
/// These read the source because the transition needs a window, a signed-in
/// account service and a live shell. The assertions are aimed at the parts that
/// were missing, not at how they are spelled.
/// </summary>
public sealed class BotStateTransitionTests
{
    [Fact]
    public void SEATINGThisDeviceAppliesTheChangeImmediately()
    {
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task SeatThisDeviceAsync()");

        // Written to disk AND applied to the running program, in that order.
        Assert.Contains("StudioBotDeviceStateStore.Write(", body, StringComparison.Ordinal);
        Assert.Contains("await EnterBotStateNowAsync(", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("StudioBotDeviceStateStore.Write(", StringComparison.Ordinal) <
                body.IndexOf("await EnterBotStateNowAsync(", StringComparison.Ordinal),
            "the seat must be stored before the program is switched to it");
    }

    [Fact]
    public void THETransitionENDSTheOwnersSessionRatherThanCoveringIt()
    {
        // 🔴 A LOCK SCREEN IS A GRID LAID ON TOP. Leaving a live owner session
        // under it would run a person's rights behind the bot's PIN - the exact
        // thing a seat exists to prevent - and the owner asked for their things
        // to be «огт харагдахгүй», which covering does not achieve.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task EnterBotStateNowAsync(");

        Assert.Contains("account.SignOut();", body, StringComparison.Ordinal);
        Assert.Contains("await RefreshProjectsAsync();", body, StringComparison.Ordinal);
        Assert.Contains("InstallBotLockIfSeated();", body, StringComparison.Ordinal);

        // The list is rebuilt with no session in hand, so what belonged to the
        // owner is gone rather than sitting under the lock.
        Assert.True(
            body.IndexOf("account.SignOut();", StringComparison.Ordinal) <
                body.IndexOf("await RefreshProjectsAsync();", StringComparison.Ordinal),
            "the session must end before the project list is rebuilt, or the old list survives");
    }

    [Fact]
    public void THETransitionDropsWhatWasReadForTheOTHERIdentity()
    {
        // Assignments, scopes and the seat member were read for whoever was
        // signed in a moment ago. Kept, they answer for the wrong identity -
        // and «not read yet» must never behave as «no restriction».
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task EnterBotStateNowAsync(");

        foreach (string field in new[]
        {
            "unlockedSeatIdentity = null;",
            "botAssignedProjectIds = null;",
            "botAssignedProjectScopes = null;",
            "botSeatMember = null;",
        })
        {
            Assert.Contains(field, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BOTHWaysIntoBotStateGoThroughTheSAMETransition()
    {
        // 🔴 FIVE IDENTITY PATHS ALREADY DO FIVE DIFFERENT SUBSETS OF THE SAME
        // WORK, which is how a half state gets written with nothing failing.
        // These two are the ones this change adds and touches, and they are held
        // to one method so a fix to either reaches both.
        string source = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Contains(
            "await EnterBotStateNowAsync(",
            MethodBody(source, "private async Task SeatThisDeviceAsync()"),
            StringComparison.Ordinal);
        Assert.Contains(
            "await EnterBotStateNowAsync(",
            MethodBody(source, "private async Task EnterBotStateAsync()"),
            StringComparison.Ordinal);

        // Defined once, called twice: three occurrences in the file.
        Assert.Equal(3, Occurrences(source, "EnterBotStateNowAsync("));
    }

    [Fact]
    public void THELockIsSafeToInstallTwice()
    {
        // It has more than one caller now - start-up, seating, and the switch
        // back. Two overlays would leave a PIN box that unlocks nothing in front
        // of the one that does.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private void InstallBotLockIfSeated()");

        Assert.Contains("if (botLockScreen is not null)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEDestructiveEntryNamesWhatItGivesUp()
    {
        // It now stands beside an entry that merely switches, and both concern
        // the bot. Two lines starting with the same word where only one is
        // irreversible is a mis-click that costs a seat.
        string source = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Contains("\"Бот эрхээр нэвтрэх…\"", source, StringComparison.Ordinal);
        Assert.Contains("сулалж", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Ботын төлөвөөс гарах…\"", source, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
