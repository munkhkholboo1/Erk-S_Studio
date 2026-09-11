using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A machine that has been a seat must always be able to get out.
///
/// 🔴 THE OWNER'S MACHINE WAS TRAPPED FOR A DAY. Their local seat record was
/// gone while the SERVER still held the device in bot state, so every read of
/// «are we seated» said no, the menu showed the unseated entries, and the way
/// out was not among them. Asked to leave, they were told to manage the seat;
/// asked to manage it, they were told to leave first. They described the shape
/// before anybody found it: «анх үүсэхдээ сайхан шилжинэ … ахиж нээхэд шилжиж
/// орж чадахгүй. Ийм л парадокс».
///
/// Every rule here exists to stop the exit being hidden again, from any
/// direction.
/// </summary>
public sealed class TheExitIsNeverHiddenTests
{
    [Fact]
    public void AMachineThatHASBeenASeatIsOfferedTheExitEvenWithNoSeatOnDisk()
    {
        // 🔴 THE OWNER'S EXACT STATE. No seat record, owner signed in, and the
        // server still holding the device - which the client cannot see from
        // here. The exit has to be on the menu anyway, because it is the only
        // thing that can find out.
        IReadOnlyList<BotMenuEntry> entries = StudioBotMenuPlan.For(
            seatedAsBot: false,
            ownerSessionInHand: true,
            machineHasBeenASeat: true);

        Assert.Contains(BotMenuEntry.LeaveBotState, entries);
    }

    [Fact]
    public void AMachineThatHasNEVERBeenASeatIsNotOfferedIt()
    {
        // The negative control, and the reason the rule is narrow: showing
        // «leave bot state» on an ordinary machine would be noise, and noise on
        // a destructive-sounding entry is its own harm.
        IReadOnlyList<BotMenuEntry> entries = StudioBotMenuPlan.For(
            seatedAsBot: false,
            ownerSessionInHand: true,
            machineHasBeenASeat: false);

        Assert.DoesNotContain(BotMenuEntry.LeaveBotState, entries);
        Assert.Contains(BotMenuEntry.SeatThisDevice, entries);
    }

    [Fact]
    public void ANUnreadableKeyStoreOFFERSTheExitRatherThanHidingIt()
    {
        // 🔴 «UNKNOWN» MUST NOT MEAN «NO». Hiding the exit is what built the
        // trap, so a machine that cannot answer the question is given the way
        // out and a sentence, not silence.
        //
        // Read from source: the property reads a file, and what matters is which
        // way it falls when that read throws.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"), "private static bool MachineHasBeenASeat");

        int guard = body.IndexOf("catch (Exception)", StringComparison.Ordinal);
        Assert.True(guard > 0, "the key-store read no longer has a failure path");
        Assert.Contains("return true;", body[guard..], StringComparison.Ordinal);
    }

    [Fact]
    public void ASEATEDMachineWithoutAnOwnerSessionStillOnlyGetsThePassport()
    {
        // The wider offer must not leak into the locked state: a machine held by
        // its PIN may not release itself, and the door there is the passport.
        IReadOnlyList<BotMenuEntry> entries = StudioBotMenuPlan.For(
            seatedAsBot: true,
            ownerSessionInHand: false,
            machineHasBeenASeat: true);

        Assert.Equal(new[] { BotMenuEntry.OwnerPassport }, entries);
    }

    [Fact]
    public void THEExitDoesNotReturnINSILENCEWhenNoSeatIsOnDisk()
    {
        // 🔴 THE SECOND HALF OF THE TRAP. Even with the entry shown, the action
        // began with «if the seat record is missing, return» - so pressing it
        // did nothing at all: no message, no change, nothing to learn from.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"), "private async Task LeaveBotStateAsync()");

        Assert.Contains("await LeaveServerHeldBotStateAsync();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THERecoveryAsksTheSERVERForTheSeatItCannotSeeLocally()
    {
        // With no seat record there is no bot id, and the release needs one. The
        // resume route is keyed by the device fingerprint, so it can answer with
        // the name this machine cannot find for itself.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"),
            "private async Task LeaveServerHeldBotStateAsync()");

        Assert.Contains("await account.ResumeAsBotAsync()", body, StringComparison.Ordinal);
        Assert.Contains("await account.LeaveBotStateAsync(held.BotId)", body, StringComparison.Ordinal);

        // And a machine the server says is already free is told so, rather than
        // being handed a failure about a problem that is over.
        Assert.Contains("BotSeatErrors.SeatIsGone(refused)", body, StringComparison.Ordinal);
        Assert.Contains("машин чөлөөтэй", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BOTHExitDialogsSayEXACTLYWhatIsLostLocally()
    {
        // 🔴 THE OWNER LOST TRUST IN WHAT THE APPLICATION TELLS THEM TODAY. A
        // destructive-sounding action with a vague warning is where that gets
        // worse, so both exits name what goes and what stays - and what stays is
        // the part that matters: their files.
        string source = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Equal(2, Occurrences(source, "ХЭВЭЭР ҮЛДЭХ: төслийн файл"));
        Assert.Equal(2, Occurrences(source, "УСТАХ: суудл"));
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
        int start = normalised.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = normalised.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return normalised[start..end];
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
