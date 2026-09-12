using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The seat records which owner put the machine into bot state.
///
/// 🔴 THE FIELD HAD NEVER ONCE BEEN WRITTEN. It was added on 2026-09-04 and read from
/// «account.Current?.Email» on the line AFTER the transition - and the last thing the
/// transition does is erase this machine's owner credential, setting Current to null.
/// That is not a bug in the transition; it is its purpose. So the value was taken
/// from a session that had just been deliberately destroyed, and the answer was
/// always the empty string.
///
/// 🔴 IT SURFACED AS A BLANK IN THE OWNER'S OWN FILE, which is the only reason anybody
/// looked: a field that is always empty looks exactly like a field nobody has filled
/// in yet. Nothing read it, so nothing went red - «unread field is a pending lie».
/// </summary>
public sealed class THESEATRemembersWhoPutItThereTests
{
    [Fact]
    public void THEOwnersEmailIsTakenBEFORETheirSessionIsErased()
    {
        // ⚠ THIS IS A SOURCE-ORDER ASSERTION AND HERE IS WHAT IT CANNOT DO: it proves
        // the read happens before the call, not that the value is right. A behavioural
        // test needs a real StudioAccountService, a credential vault and a server -
        // the same reason the transition itself has no unit test. The order IS the
        // defect, so the order is what is pinned.
        string source = Lf(ReadAppSource("BotSeatDialogs.cs"));

        int capture = source.IndexOf(
            "string enteredByEmail = account.Current?.Email ?? \"\";",
            StringComparison.Ordinal);
        int transition = source.IndexOf(
            "await account.EnterBotStateAsync(",
            StringComparison.Ordinal);

        Assert.True(capture > 0, "the owner's email is no longer captured at all");
        Assert.True(transition > 0, "the bot-state transition is gone");
        Assert.True(
            capture < transition,
            "the owner's email is read after EnterBotStateAsync, which erases the " +
            "session it is read from - so the field is written empty every time");
    }

    [Fact]
    public void THEFieldIsNEVERFilledFromASessionThatHasBeenErased()
    {
        // The trap is specifically «read it out of account.Current at the point of
        // writing the record». Assigning the captured local is fine; reaching back
        // into the account there is the fault, whatever it is renamed to.
        string source = StripComments(Lf(ReadAppSource("BotSeatDialogs.cs")));

        Assert.Contains("EnteredByEmail = enteredByEmail,", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EnteredByEmail = account.Current",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THESeatStateKeepsTheFieldThroughAPinAttempt()
    {
        // 🔴 THE SECOND WAY THE VALUE COULD HAVE GONE, RULED OUT RATHER THAN ASSUMED.
        // The lock screen rewrites the file on every PIN attempt, so a rewrite that
        // rebuilt the record instead of mutating the one it read would blank this
        // field on the first wrong PIN - and the owner's file HAD been rewritten
        // hours after it was created. It mutates and writes back the same object,
        // which is why the entered-at time survived while the email did not.
        string source = Lf(ReadAppSource("BotLockScreen.cs"));

        Assert.Contains("StudioBotDeviceStateStore.Write(seat);", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "StudioBotDeviceStateStore.Write(new StudioBotDeviceState",
            source,
            StringComparison.Ordinal);
    }

    private static string Lf(string source) => source.Replace("\r\n", "\n");

    private static string StripComments(string source)
    {
        var result = new StringBuilder(source.Length);
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString();
    }

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
