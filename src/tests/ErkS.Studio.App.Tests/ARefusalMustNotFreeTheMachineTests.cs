using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// «I could not place this device» is not «you are free».
///
/// 🔴 THE SAME MISTAKE, MADE TWICE, THE SECOND TIME BY THE FIX FOR THE FIRST.
/// BotSeatErrors.SeatIsGone answers «should this call stop retrying», which is
/// true of FOUR codes; only one of them says the seat ENDED. «bot_state_not_found»
/// is what a fingerprint that failed to match produces on a machine whose seat is
/// perfectly alive, and SRV measured «bot_state_seat_unavailable» arriving from
/// resume while an ACTIVE state exists and the seat row is a tombstone - the exact
/// opposite of free.
///
/// The resume path was narrowed to the one honest code after it cost somebody
/// their machine. The recovery route added the day after was written with the
/// WIDE predicate over a destructive branch, and the tests did not see it: every
/// one of them was scoped to «ResumeAsBotAsync», so a second eraser anywhere else
/// was invisible. The rule is derived over the whole project here.
/// </summary>
public sealed class ARefusalMustNotFreeTheMachineTests
{
    private const string Wide = "BotSeatErrors.SeatIsGone(";
    private const string Narrow = "BotSeatErrors.SeatWasEndedByOwner(";

    [Fact]
    public void NOWIDELYGuardedBranchANYWHEREErasesWhatThisMachineHolds()
    {
        // 🔴 DERIVED OVER THE PROJECT, NOT OVER ONE METHOD. The rule is about
        // what may delete a seat, and a rule that only reads the method where
        // the defect was last found cannot see the next one.
        var checkedBranches = 0;
        foreach ((string name, string source) in AppSources())
        {
            foreach (string branch in BranchesGuardedBy(source, Wide))
            {
                checkedBranches++;
                if (branch.Contains(Narrow, StringComparison.Ordinal))
                    continue;

                // This branch fires on codes that do NOT say the seat ended, so
                // it may not take the machine's own record away.
                Assert.DoesNotContain("ForgetLocalSeatTraces(", branch, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "StudioBotDeviceStateStore.Clear()", branch, StringComparison.Ordinal);
                Assert.DoesNotContain("машин чөлөөтэй", branch, StringComparison.Ordinal);
            }
        }

        // The instrument: a scan that matched nothing would satisfy every
        // assertion above without reading a line of the product.
        Assert.True(
            checkedBranches >= 2,
            "the scan found only " + checkedBranches + " branches guarded by the wide predicate");
    }

    [Fact]
    public void THERecoveryTellsTheTruthWhenTheSERVERWillNotNameTheSeat()
    {
        // Stuck and informed beats stuck and certain. The comfortable sentence
        // is what stopped everybody checking last time.
        string branch = Assert.Single(
            BranchesGuardedBy(ReadAppSource("ShellView.BotSeat.cs"), Wide),
            text => text.Contains("суудлаараа таньсангүй", StringComparison.Ordinal));

        Assert.Contains("гэсэн үг БИШ", branch, StringComparison.Ordinal);
        Assert.Contains("Локал бичлэгт хүрсэнгүй", branch, StringComparison.Ordinal);
        Assert.Contains("StudioBoundaryRefusals.StorePath", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void THEOWNERSOwnReleaseSTILLFreesTheMachine()
    {
        // The positive control. A rule that refused to free anything would pass
        // every assertion above and leave the exit as dead as it was found.
        string branch = Assert.Single(
            BranchesGuardedBy(ReadAppSource("ShellView.BotSeat.cs"), Narrow),
            text => text.Contains("эзэмшигч аль хэдийн чөлөөлжээ", StringComparison.Ordinal));

        Assert.Contains("ForgetLocalSeatTraces();", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void ANANSWERThatNamesNOSeatPromisesNothingAndDeletesNothing()
    {
        // The server replied without refusing and without naming a seat. This
        // route exists because the server may hold a seat this machine cannot
        // see, so a nameless answer settles nothing in either direction.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"),
            "private async Task LeaveServerHeldBotStateAsync()");
        int at = body.IndexOf(
            "if (string.IsNullOrWhiteSpace(held.BotId))", StringComparison.Ordinal);
        Assert.True(at > 0, "the nameless-answer branch is gone");

        string branch = body[at..body.IndexOf("await account.LeaveBotStateAsync", at, StringComparison.Ordinal)];
        Assert.DoesNotContain("машин чөлөөтэй", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("ForgetLocalSeatTraces(", branch, StringComparison.Ordinal);
        Assert.Contains("чөлөөлөх зүйл олдсонгүй", branch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every catch body whose «when» clause mentions <paramref name="predicate"/>,
    /// sliced from the guard to whichever comes first: the next catch, or the end
    /// of the enclosing method.
    /// </summary>
    private static IEnumerable<string> BranchesGuardedBy(string source, string predicate)
    {
        string text = source.Replace("\r\n", "\n");
        for (int at = text.IndexOf(predicate, StringComparison.Ordinal);
            at >= 0;
            at = text.IndexOf(predicate, at + 1, StringComparison.Ordinal))
        {
            // Only a catch guard counts; the predicates are also called in
            // ordinary conditions elsewhere, and those decide no deletion.
            int line = text.LastIndexOf("\n        catch (", at, StringComparison.Ordinal);
            if (line < 0 || text.IndexOf('\n', line + 1) > at)
            {
                // Not preceded by a catch on its own line, or the catch is on a
                // later line than the match: not a guard clause.
                if (line < 0)
                    continue;
            }

            int nextCatch = text.IndexOf("\n        catch (", at, StringComparison.Ordinal);
            int methodEnd = text.IndexOf("\n    }", at, StringComparison.Ordinal);
            int end = nextCatch >= 0 && (methodEnd < 0 || nextCatch < methodEnd)
                ? nextCatch
                : methodEnd;
            if (end < 0)
                end = text.Length;
            yield return text[at..end];
        }
    }

    private static IEnumerable<(string Name, string Source)> AppSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
            {
                foreach (FileInfo file in new DirectoryInfo(candidate)
                             .GetFiles("*.cs", SearchOption.TopDirectoryOnly))
                {
                    yield return (file.Name, File.ReadAllText(file.FullName, Encoding.UTF8));
                }

                yield break;
            }

            directory = directory.Parent;
        }

        Assert.Fail("the app project was not found; this test reads it from source");
    }

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
