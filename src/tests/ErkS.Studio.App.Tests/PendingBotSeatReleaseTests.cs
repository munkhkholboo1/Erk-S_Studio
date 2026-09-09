using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The refusal that means the job is done.
///
/// 🔴 THIS BLOCKED A PERSON FROM WORKING. A machine left bot state on 5
/// September; the seat was already free on the server; and every owner sign-in
/// afterwards ended with «Цуцлагдаагүй ботын суудал: «Erk-S» (Энэ суудлын
/// төхөөрөмж ботын төлөвт байхгүй байна.)» - a bot-seat refusal, on the sign-in
/// they had just completed, replacing the message that would have said they were
/// signed in. From the outside that is «I sign in as the owner and it comes up
/// as the bot, and I cannot sign in as the owner».
///
/// The server returns that refusal when NO device holds the seat, which is
/// exactly what a release is for. The client treated every exception as failure
/// and kept the entry forever, so the list could never empty and the message
/// came back on every sign-in.
///
/// The predicate that recognises it already existed - BotSeatErrors.SeatIsGone,
/// written for the resume path - and nothing on this path called it. The rule
/// is written, the caller does not ask: the third time in one day.
/// </summary>
public sealed class PendingBotSeatReleaseTests
{
    [Fact]
    public void ASEATThatIsAlreadyFreeIsFORGOTTENRatherThanRetriedForever()
    {
        // Read from source because the flush needs a signed-in account service
        // and a window. The assertion is narrow: the already-free refusal must
        // reach a Forget, not the failure list.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        int method = source.IndexOf(
            "private async Task<BotSeatFlushOutcome> FlushPendingBotSeatReleasesAsync()",
            StringComparison.Ordinal);
        Assert.True(method > 0, "the flush was not found");
        string body = source[method..source.IndexOf("\n    }", method, StringComparison.Ordinal)];

        int guard = body.IndexOf("when (BotSeatErrors.SeatIsGone(exception))", StringComparison.Ordinal);
        // 🔴 THIS ANCHOR USED TO REACH INTO THE CATCH-ALL'S FIRST STATEMENT,
        // and that statement changed - at which point the ordering check found
        // nothing, passed on `generic < 0`, and stopped testing anything. A
        // check written so that «not found» reads as «fine» is not a check.
        // The catch-all is now located by what MAKES it the catch-all: a
        // `catch (Exception …)` carrying no `when`.
        int generic = UnguardedCatch(body);

        Assert.True(guard > 0, "an already-free seat is still treated as a failure");
        Assert.True(generic > 0, "the general failure branch was not found; this test is reading nothing");
        Assert.True(
            guard < generic,
            "the already-free case must be caught before the general failure, or it never runs");
        Assert.Contains("alreadyFree++", body, StringComparison.Ordinal);

        // And the failure that is NOT the already-free one leaves a record of
        // itself on the entry, so an entry that survives says why in its own
        // file rather than only on a status line nobody kept.
        int note = body.IndexOf("StudioPendingBotSeatReleases.NoteAttempt(", StringComparison.Ordinal);
        Assert.True(note > generic, "the note belongs inside the general failure branch");

        // Forgotten twice: once on a real release, once when the seat was
        // already free. A flush that forgets only the first can never empty the
        // list, which is the defect itself.
        Assert.Equal(2, Occurrences(body, "StudioPendingBotSeatReleases.Forget("));
    }

    [Fact]
    public void THEPredicateRecognisesTheServersOwnCode()
    {
        // 🔴 THE TWO SIDES MUST AGREE ON THE STRING, AND THIS IS THE ONLY PLACE
        // THAT SAYS SO. The server refuses a release with bot_state_not_found
        // when no device holds the seat. If that spelling ever parts company
        // with this one the entry becomes immortal again, and the symptom is a
        // person who cannot finish signing in - not a stack trace.
        Assert.Equal("bot_state_not_found", BotSeatErrors.SeatNotFound);

        var refusal = new StudioAccountException(
            "Энэ суудлын төхөөрөмж ботын төлөвт байхгүй байна.",
            System.Net.HttpStatusCode.Conflict,
            BotSeatErrors.SeatNotFound);

        Assert.True(
            BotSeatErrors.SeatIsGone(refusal),
            "the release refusal must be recognised as «the seat is not held»");
    }

    [Fact]
    public void ANORDINARYFailureIsStillKept()
    {
        // The control. If every refusal were forgotten, a seat genuinely still
        // held somewhere would be dropped from the list and never released -
        // trading a stuck message for a lost obligation.
        var offline = new StudioAccountException("Сервертэй холбогдсонгүй.");

        Assert.False(BotSeatErrors.SeatIsGone(offline));
    }

    /// <summary>
    /// Where the branch that catches everything begins - the one WITHOUT a
    /// `when`, which is what makes it last and makes the ordering matter.
    /// </summary>
    private static int UnguardedCatch(string body)
    {
        const string Opening = "catch (Exception ";
        for (int at = body.IndexOf(Opening, StringComparison.Ordinal);
            at >= 0;
            at = body.IndexOf(Opening, at + 1, StringComparison.Ordinal))
        {
            int lineEnd = body.IndexOf('\n', at);
            string line = lineEnd < 0 ? body[at..] : body[at..lineEnd];
            if (!line.Contains(" when (", StringComparison.Ordinal))
                return at;
        }
        return -1;
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
