using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The device's own seat is erased on TWO paths and no others.
///
/// 🔴 WRITTEN THE NIGHT A SERVER DEPLOY DAMAGED PRODUCTION DATA. Seven of eight
/// seats lost the organisation they were created under, and the question that
/// had to be answered before anything else was whether Studio would react to
/// the damage by destroying what it holds LOCALLY - which would have turned a
/// recoverable server problem into a device-side loss on the machines the
/// restore was meant to save.
///
/// The answer was clean, and it was clean by inspection only: two Clear() calls,
/// both correctly guarded, and no writer that copies a server-supplied
/// organisation into the local seat. A safety property that holds because
/// somebody read the file once is a property that disappears the day a third
/// caller is added, and the cost of losing it is a person's machine forgetting
/// it is a seat.
///
/// So it is derived here rather than described: the OWNERS of every destructive
/// call are read out of the source and held to a named set.
/// </summary>
public sealed class LocalSeatIsNotDestroyedTests
{
    [Fact]
    public void ONLYTwoNamedPathsMayERASEThisDevicesSeat()
    {
        // One is the server saying, by a named code, that the seat has ended.
        // The other is the owner's own deliberate release. Nothing else - and
        // in particular, nothing that merely fails to FIND the seat.
        // 🔴 THIS READ ONE FILE. A third caller added anywhere else in the app
        // would have been invisible to it - the same shape as the test that
        // compared fields and could not see a call. The scan is now over every
        // source file in the project, so «two owners» means two in the product,
        // not two in the file somebody happened to name.
        IReadOnlyCollection<string> owners = OwnersAcrossTheApp("StudioBotDeviceStateStore.Clear()");

        Assert.Equal(
            new[] { "LeaveBotStateAsync", "ResumeAsBotAsync" },
            owners.Order());
    }

    [Fact]
    public void ONLYTheOWNERSReleaseTakesASeatFromItsMachine()
    {
        // 🔴 THIS BRANCH USED TO FIRE ON FOUR CODES AND IT COST SOMEBODY THEIR
        // MACHINE. SeatIsGone answers «should this call stop retrying», which is
        // true of four; only ONE of them says the SEAT ended - the owner
        // released it, which the release dialog promises the device notices by
        // itself. «bot_state_not_found» is also what a fingerprint that failed
        // to match produces, on a machine whose seat is perfectly alive.
        //
        // The narrower predicate is what the destructive branch reads.
        string body = CodeOf("ShellView.BotSeat.cs", "private async Task ResumeAsBotAsync(");

        int ended = body.IndexOf("BotSeatErrors.SeatWasEndedByOwner(released)", StringComparison.Ordinal);
        int erase = body.IndexOf("StudioBotDeviceStateStore.Clear();", StringComparison.Ordinal);
        Assert.True(ended > 0, "the destructive branch no longer reads the narrow predicate");
        Assert.True(erase > ended, "the erasure must sit inside the owner-ended branch");

        // And the predicate really is narrower than the one it replaced.
        //
        // Sliced at its semicolon, not by MethodBody: this member is
        // EXPRESSION-BODIED, and a reader that looks for a closing brace runs
        // straight past it into the next method - which does mention the wider
        // codes. The first run of this test failed exactly that way.
        string catalogue = ReadAppSource("BotSeatDialogs.cs");
        int at = catalogue.IndexOf(
            "public static bool SeatWasEndedByOwner(Exception exception)", StringComparison.Ordinal);
        Assert.True(at > 0, "the narrow predicate was not found");
        string narrow = catalogue[at..(catalogue.IndexOf(';', at) + 1)];
        foreach (string wider in new[] { "SeatNotFound", "SeatChanged", "SeatUnavailable" })
            Assert.DoesNotContain(wider, narrow, StringComparison.Ordinal);
        Assert.Contains("SeatReleasedRemotely", narrow, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalThatIsNOTTheOwnersReleaseKeepsTheSeat()
    {
        // The credential and the reads answered for a session that just failed,
        // so they go. The seat is a fact about the machine and stays - and the
        // sentence has to say so, or the person reads «could not resume» as
        // «this machine is no longer a seat» and acts on it.
        string body = CodeOf("ShellView.BotSeat.cs", "private async Task ResumeAsBotAsync(");

        int refused = body.IndexOf("!BotSeatErrors.SeatWasEndedByOwner(refused)", StringComparison.Ordinal);
        Assert.True(refused > 0, "the non-destructive branch is gone");

        string branch = body[refused..body.IndexOf("catch (StudioAccountException released)", refused, StringComparison.Ordinal)];
        Assert.Contains("account.UseBotToken(null);", branch, StringComparison.Ordinal);
        Assert.Contains("Суудал энэ төхөөрөмж дээр хэвээр", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("StudioBotDeviceStateStore.Clear();", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void THEServerSideErasureIsGatedOnTheNAMEDRefusalCode()
    {
        // 🔴 MATCHING ON A STATUS WOULD SWEEP IN EVERY 403 AND 404 THE SERVER
        // CAN ANSWER, including "your token expired", "your signature did not
        // verify" and anything a half-migrated deployment produces - transient
        // things after which the seat is still there. This branch destroys
        // local state, so it reads the code and nothing else.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"),
            "private async Task ResumeAsBotAsync(StudioBotDeviceState seat)");

        int guard = body.IndexOf(
            "when (BotSeatErrors.SeatIsGone(", StringComparison.Ordinal);
        int erase = body.IndexOf("StudioBotDeviceStateStore.Clear()", StringComparison.Ordinal);

        Assert.True(guard > 0, "the erasure is no longer behind the named-code predicate");
        Assert.True(erase > guard, "the erasure must sit INSIDE that guarded branch");
    }

    [Fact]
    public void ASeatMissingFromTheOrganisationsListErasesNOTHING()
    {
        // The list is keyed by organisation. A seat whose organisation link is
        // damaged - or a listing that simply fails - drops out of it, and that
        // absence must never be read as «the seat is gone». Absence is not a
        // value.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.DoesNotContain("StudioBotDeviceStateStore.Clear", source, StringComparison.Ordinal);
    }

    [Fact]
    public void THELocalSeatsIDENTITYIsNeverTakenFromAServerResponse()
    {
        // What the device stores about its seat - which seat, under which
        // organisation - is written when it ENTERS bot state and is frozen
        // there. If a resume response could overwrite it, a server answering
        // with an empty organisation would erase the device's own record of
        // where it belongs, which is exactly the damage this file was written
        // for.
        string botSeat = ReadAppSource("ShellView.BotSeat.cs");
        string resume = MethodBody(botSeat, "private async Task ResumeAsBotAsync(StudioBotDeviceState seat)");

        Assert.DoesNotContain("StudioBotDeviceStateStore.Write(", resume, StringComparison.Ordinal);

        // In this file the seat is stored on ONE path: the one that seats the
        // machine, from the dialog that did it.
        Assert.Equal(
            new[] { "SeatThisDeviceAsync" },
            MethodsContaining("ShellView.BotSeat.cs", "StudioBotDeviceStateStore.Write(").Order());
    }

    [Fact]
    public void THELockScreenWritesBackTheSEATItAlreadyHeld()
    {
        // Its three writes move PIN counters. Each one stores the object the
        // screen was constructed with, so nothing a server said can reach the
        // stored identity through this door.
        string source = ReadAppSource("BotLockScreen.cs");

        int writes = source.Split("StudioBotDeviceStateStore.Write(").Length - 1;
        int writesOfTheHeldSeat = source.Split("StudioBotDeviceStateStore.Write(seat);").Length - 1;

        Assert.True(writes > 0, "the lock screen no longer stores anything; this test reads nothing");
        Assert.Equal(writes, writesOfTheHeldSeat);
    }

    /// <summary>
    /// The names of the methods that contain <paramref name="needle"/>, read out
    /// of the file.
    ///
    /// Derived rather than listed, because the point is to notice a caller
    /// nobody has thought about yet - and a hand-written list stays true on the
    /// day one appears.
    /// </summary>
    /// <summary>
    /// Every method in the app that contains <paramref name="needle"/>, across
    /// all of its source - not one named file.
    /// </summary>
    private static IReadOnlyCollection<string> OwnersAcrossTheApp(string needle)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? app = null;
        while (directory is not null && app is null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
                app = new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.NotNull(app);
        var found = new List<string>();
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(file.FullName, Encoding.UTF8);
            for (int at = source.IndexOf(needle, StringComparison.Ordinal);
                at >= 0;
                at = source.IndexOf(needle, at + 1, StringComparison.Ordinal))
            {
                string owner = EnclosingMethod(source, at);
                if (!found.Contains(owner, StringComparer.Ordinal))
                    found.Add(owner);
            }
        }

        Assert.NotEmpty(found);
        return found;
    }

    private static IReadOnlyCollection<string> MethodsContaining(string fileName, string needle)
    {
        string source = ReadAppSource(fileName);
        var found = new List<string>();
        for (int at = source.IndexOf(needle, StringComparison.Ordinal);
            at >= 0;
            at = source.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            string owner = EnclosingMethod(source, at);
            if (!found.Contains(owner, StringComparer.Ordinal))
                found.Add(owner);
        }
        return found;
    }

    /// <summary>
    /// The name of the member declaration enclosing <paramref name="index"/>.
    ///
    /// Members of a class sit at four spaces; anything deeper is a lambda or a
    /// local function inside one, which is exactly what should be attributed to
    /// its owner rather than counted separately.
    /// </summary>
    private static string EnclosingMethod(string source, int index)
    {
        int best = -1;
        foreach (string opening in new[]
        {
            "\n    private ", "\n    public ", "\n    internal ", "\n    protected ",
        })
        {
            int at = source.LastIndexOf(opening, index, StringComparison.Ordinal);
            if (at > best)
                best = at;
        }

        Assert.True(best > 0, "no enclosing member was found for the call at " + index);
        int paren = source.IndexOf('(', best);
        int arrow = source.IndexOf("=>", best, StringComparison.Ordinal);
        int head = paren < 0 || (arrow >= 0 && arrow < paren) ? arrow : paren;
        Assert.True(head > best, "the enclosing declaration could not be read");

        string[] words = source[best..head]
            .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(words);
        return words[^1];
    }

    private static string CodeOf(string fileName, string signature) =>
        MethodBody(ReadAppSource(fileName), signature);

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
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
