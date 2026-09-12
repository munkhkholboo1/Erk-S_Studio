using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A machine keeps its bot seat while the owner is the one working on it.
///
/// 🔴 THE OWNER'S REPORT, ON A DEV BUILD: «эзэмшигч рүү шилжихэд төслүүд харагдахгүй
/// байна». They signed in with their own passport on their own computer and their
/// project list was empty.
///
/// 🔴 AND NOTHING WAS BROKEN IN THE OBVIOUS PLACES. The resume DOES rebuild the list
/// - RefreshProjectsAsync is its last line. The session IS the owner's - the route is
/// gated on EnsureSignedInAsync and the resume drops the bot token outright. Both
/// rules involved were RIGHT: leaving the bot behind correctly discards the seat's
/// assignment list as another identity's, and the visibility rule correctly answers
/// «show nothing» when a bot's assignments are unknown. They combined into a wrong
/// screen because the filter asked «does this MACHINE hold a seat?» where it meant
/// «is the BOT the one acting?» - and the seat is deliberately not released when the
/// owner takes over.
/// </summary>
public sealed class THEOWNERONASeatedMachineSeesTheirOwnProjectsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void THEBOTIsActingOnlyWhenNoOwnerSessionIsInHand(
        bool deviceHoldsBotSeat,
        bool ownerSessionInHand,
        bool expected)
    {
        // The fourth row is the one that was wrong everywhere: a seated machine WITH
        // the owner signed in. The PIN opens the seat; the passport opens the owner.
        Assert.Equal(
            expected,
            StudioBotActor.IsTheBotActing(deviceHoldsBotSeat, ownerSessionInHand));
    }

    [Fact]
    public void ASEATEDMachineWithTheOwnerSignedInShowsTHEIRProjects()
    {
        // 🔴 THE DEFECT, END TO END THROUGH BOTH RULES. The assignment list is null
        // because the resume correctly threw away the seat's - and that null used to
        // mean «hide everything» about the owner.
        bool actingAsBot = StudioBotActor.IsTheBotActing(
            deviceHoldsBotSeat: true,
            ownerSessionInHand: true);

        Assert.True(StudioBotProjectVisibility.IsVisible(actingAsBot, null, "project-1"));
        Assert.True(StudioBotProjectVisibility.MayOpen(
            actingAsBot,
            assignedProjectIds: null,
            hasFile: true,
            fileIdentity: "project-1",
            rowProjectId: null));
    }

    [Fact]
    public void THESAMEMachineWithNoOwnerSessionStillHidesEverythingUnassigned()
    {
        // 🔴 THE OTHER DIRECTION, WHICH THE FIX MUST NOT COST. «Зөвхөн томилогдсон
        // төсөл дээр үүргийнхээ дагуу л оролцоно. Бусад төсөл харагдахгүй.» A fix
        // that made the owner's list come back by weakening this would have traded
        // one fault for a worse one.
        bool actingAsBot = StudioBotActor.IsTheBotActing(
            deviceHoldsBotSeat: true,
            ownerSessionInHand: false);

        Assert.True(actingAsBot);
        Assert.False(
            StudioBotProjectVisibility.IsVisible(actingAsBot, null, "project-1"),
            "an unread assignment list must still answer «nothing», never «everything»");

        var assigned = new HashSet<string>(StringComparer.Ordinal) { "project-1" };
        Assert.True(StudioBotProjectVisibility.IsVisible(actingAsBot, assigned, "project-1"));
        Assert.False(StudioBotProjectVisibility.IsVisible(actingAsBot, assigned, "project-2"));
    }

    [Fact]
    public void THEOwnersADMINISTRATIONComesBackWithThem()
    {
        // The same conflation hid Companies and Foundation from the owner on their
        // own seated machine - the surfaces the decree hides from a BOT.
        bool owner = StudioBotActor.IsTheBotActing(true, ownerSessionInHand: true);
        bool bot = StudioBotActor.IsTheBotActing(true, ownerSessionInHand: false);

        foreach (string page in StudioBotSurfaceVisibility.HiddenFromABot)
        {
            Assert.True(
                StudioBotSurfaceVisibility.IsVisible(owner, page),
                page + " stayed hidden from the owner on their own machine");
            Assert.False(
                StudioBotSurfaceVisibility.IsVisible(bot, page),
                page + " is offered to a bot");
        }
    }

    [Fact]
    public void THEOwnerACTINGOnASeatedMachineIsJudgedByTHEIROwnScopes()
    {
        // 🔴 THE SECOND FACE OF THE DEFECT, AND WORSE THAN THE EMPTY LIST. Authority
        // in the open project came from the SEAT's scopes «whenever the machine holds a
        // seat» - and the resume had just cleared those as another identity's. An
        // unknown source yields the empty set, correctly, so the owner had NO scopes at
        // all: every scoped action in their own open project refused, locally, with the
        // server never asked.
        string[] personal = ["project.read", "project.write"];
        StudioSessionKind kind = StudioBotActor.IsTheBotActing(
            deviceHoldsBotSeat: true,
            ownerSessionInHand: true)
            ? StudioSessionKind.BotSeat
            : StudioSessionKind.Personal;

        Assert.Equal(StudioSessionKind.Personal, kind);
        Assert.True(StudioEffectiveAuthority.Allows(kind, personal, seatScopes: null, "project.write"));
    }

    [Fact]
    public void ASEATIsStillJudgedByItsSeatAndNOTHINGElse()
    {
        // The other direction, which the fix must not cost: with no owner session the
        // seat's own scopes are in force, the owner's are not borrowed, and an unread
        // seat answer is still «nothing» rather than somebody else's rights.
        string[] personal = ["project.write"];
        StudioSessionKind kind = StudioBotActor.IsTheBotActing(
            deviceHoldsBotSeat: true,
            ownerSessionInHand: false)
            ? StudioSessionKind.BotSeat
            : StudioSessionKind.Personal;

        Assert.Equal(StudioSessionKind.BotSeat, kind);
        Assert.False(
            StudioEffectiveAuthority.Allows(kind, personal, seatScopes: null, "project.write"),
            "a seat borrowed the owner's scopes");
        Assert.False(
            StudioEffectiveAuthority.Allows(kind, personal, seatScopes: [], "project.write"),
            "a seat assigned nothing was given something");
    }

    [Fact]
    public void AUTHORITYAsksTheActorTooAndSaysSoInSource()
    {
        // ⚠ Source-anchored for the same reason as the wiring assertions above: the
        // shell property needs a ShellView instance to observe. The rule composition is
        // tested directly in the two tests above; this pins that the shell feeds it the
        // actor rather than the machine.
        Assert.Contains(
            "ActingAsBot ? StudioSessionKind.BotSeat : StudioSessionKind.Personal",
            ReadAppSource("ShellView.BotSeat.cs"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SEATManagementKeepsTheAnswerItAlwaysGave(bool seated, bool signedIn)
    {
        // 🔴 THE REFACTOR HAD TO BE TRUTH-PRESERVING, AND «!seated || signed in» IS
        // THE BEHAVIOUR THAT SHIPPED. Written out here independently as the
        // specification being preserved: the new form is derived from the actor rule,
        // and if the two ever disagree it is this test that says so.
        Assert.Equal(
            !seated || signedIn,
            StudioBotActor.MayManageSeats(seated, signedIn));
    }

    [Fact]
    public void THEACTORQuestionIsAskedWhereItIsMEANTAndNowhereElse()
    {
        // 🔴 THE CLASSIFICATION IS THE FIX, SO IT IS WRITTEN DOWN. Half the uses of
        // «seated» are about the MACHINE and must stay that way: a seated computer
        // locks on start-up whoever signs in afterwards, and the seating flow is
        // about the device. The other half are about WHO IS ACTING. A later sweep
        // that renamed one group into the other would either hide the owner's work
        // again or unlock a seat.
        string seat = ReadAppSource("ShellView.BotSeat.cs");
        string shell = ReadAppSource("ShellView.cs");

        // Who is acting.
        Assert.Contains(
            "StudioBotProjectVisibility.IsVisible(ActingAsBot, botAssignedProjectIds, projectId)",
            seat,
            StringComparison.Ordinal);
        Assert.Contains("if (!ActingAsBot)\n            return true;", Lf(seat), StringComparison.Ordinal);
        Assert.Contains("if (ActingAsBot && botAssignedProjectIds is null)", shell, StringComparison.Ordinal);
        Assert.Contains("actingAsBot: ActingAsBot))", shell, StringComparison.Ordinal);

        // What this machine is. These must NOT become the actor question.
        Assert.Contains("alreadySeated: SeatedAsBot", seat, StringComparison.Ordinal);
        Assert.Contains(
            "StudioBotMenuPlan.For(\n            SeatedAsBot,",
            Lf(seat),
            StringComparison.Ordinal);
        Assert.Contains(
            "StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(SeatedAsBot, account.IsSignedIn)",
            shell,
            StringComparison.Ordinal);

        // And the truth table has ONE home: the shell derives it, never spells it.
        Assert.Contains(
            "StudioBotActor.IsTheBotActing(SeatedAsBot, account.IsSignedIn)",
            Lf(seat),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SeatedAsBot && !account.IsSignedIn",
            StripComments(Lf(seat)) + StripComments(Lf(shell)));
    }

    private static string Lf(string source) => source.Replace("\r\n", "\n");

    /// <summary>
    /// Code without its comments.
    ///
    /// The «spelled inline» assertion above is about CODE: this file's own
    /// explanation of the old spelling quotes it, and a word-lock that failed on the
    /// documentation of the thing it forbids would be a test of its own prose.
    /// </summary>
    private static string StripComments(string source)
    {
        var result = new StringBuilder(source.Length);
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("///", StringComparison.Ordinal) ||
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
