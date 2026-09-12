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
    // not seated: nobody is ever the bot
    [InlineData(false, Owner, Owner, false)]
    [InlineData(false, "", "", false)]
    // seated, nobody signed in: the seat is acting
    [InlineData(true, Owner, "", true)]
    [InlineData(true, "", "", true)]
    // seated, THE seat's own owner signed in: they are acting, not the bot
    [InlineData(true, Owner, Owner, false)]
    [InlineData(true, Owner, "OWNER@Erk-S.MN", false)]
    // 🔴 seated, a DIFFERENT owner signed in: the seat's rights are not theirs
    [InlineData(true, Owner, Stranger, true)]
    // ⚠ seated by a build that never recorded its owner: the older behaviour
    [InlineData(true, "", Owner, false)]
    public void THEBOTIsActingUnlessTHATOwnerIsHere(
        bool deviceHoldsBotSeat,
        string seatEnteredByEmail,
        string signedInEmail,
        bool expected)
    {
        // 🔴 THE OWNER DREW THIS LINE THEMSELVES (2026-09-12, Decision 29): «өөр
        // эзэмшигчийн ботыг өөр эзэмшигчийн эрхтэй хольж хутгаж болохгуй шүү». The
        // question was «is somebody signed in», which answers the same for the seat's
        // owner and for a stranger - and a stranger taking a seat's identity is the
        // one failure nobody could undo.
        //
        // ⚠ THE LAST ROW IS A KNOWN HOLE WITH A KNOWN SHAPE. EnteredByEmail was
        // never written before 2026-09-12, so seats made earlier cannot answer; failing
        // closed there would tell the owner on their own machine that the bot is
        // acting, which is the fault fixed hours ago. Recorded, not hidden.
        Assert.Equal(
            expected,
            StudioBotActor.IsTheBotActing(
                deviceHoldsBotSeat, seatEnteredByEmail, signedInEmail));
    }

    private const string Owner = "owner@erk-s.mn";
    private const string Stranger = "someone.else@erk-s.mn";

    [Fact]
    public void ASEATEDMachineWithTheOwnerSignedInShowsTHEIRProjects()
    {
        // 🔴 THE DEFECT, END TO END THROUGH BOTH RULES. The assignment list is null
        // because the resume correctly threw away the seat's - and that null used to
        // mean «hide everything» about the owner.
        bool actingAsBot = StudioBotActor.IsTheBotActing(true, Owner, Owner);

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
        bool actingAsBot = StudioBotActor.IsTheBotActing(true, Owner, signedInEmail: "");

        Assert.True(actingAsBot);
        Assert.False(
            StudioBotProjectVisibility.IsVisible(actingAsBot, null, "project-1"),
            "an unread assignment list must still answer «nothing», never «everything»");

        var assigned = new HashSet<string>(StringComparer.Ordinal) { "project-1" };
        Assert.True(StudioBotProjectVisibility.IsVisible(actingAsBot, assigned, "project-1"));
        Assert.False(StudioBotProjectVisibility.IsVisible(actingAsBot, assigned, "project-2"));
    }

    [Fact]
    public void EVERYSurfaceIsOfferedToBOTHActorsNow()
    {
        // ⚠ THIS TEST USED TO ASSERT THE OPPOSITE FOR A BOT, and the owner changed
        // the rule underneath it: surfaces stay, actions ask for the owner. What it
        // still guards is the half that was the defect - the owner on a seated
        // machine losing their own administration - now by the stronger statement
        // that NOBODY loses a surface.
        bool owner = StudioBotActor.IsTheBotActing(true, Owner, Owner);
        bool bot = StudioBotActor.IsTheBotActing(true, Owner, signedInEmail: "");

        Assert.Empty(StudioBotSurfaceVisibility.HiddenFromABot);
        foreach (string page in StudioBotSurfaceVisibility.AllPages)
        {
            Assert.True(StudioBotSurfaceVisibility.IsVisible(owner, page), page);
            Assert.True(StudioBotSurfaceVisibility.IsVisible(bot, page), page);
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
        StudioSessionKind kind = StudioBotActor.IsTheBotActing(true, Owner, Owner)
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
        StudioSessionKind kind = StudioBotActor.IsTheBotActing(true, Owner, signedInEmail: "")
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
    [InlineData(false, Owner)]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(true, Owner)]
    public void SEATManagementKeepsTheAnswerItAlwaysGaveForTHATOwner(
        bool seated,
        string signedIn)
    {
        // 🔴 THE REFACTOR HAD TO BE TRUTH-PRESERVING, AND «!seated || signed in» IS
        // THE BEHAVIOUR THAT SHIPPED. Written out here independently as the
        // specification being preserved - for the seat's OWN owner, which is every
        // case the old formula was ever asked about on a real machine.
        Assert.Equal(
            !seated || signedIn.Length > 0,
            StudioBotActor.MayManageSeats(seated, Owner, signedIn));
    }

    [Fact]
    public void ADIFFERENTOwnerMayNOTManageThisMachinesSeat()
    {
        // 🔴 STRICTER THAN WHAT SHIPPED, ON PURPOSE. «!seated || signed in» said yes
        // to anybody holding a session; a seat belongs to the licence holder who made
        // it, and releasing or renaming somebody else's seat is the mixing the owner
        // ruled out. This row is where the old formula and the new one disagree, and
        // the disagreement is the decision.
        Assert.False(StudioBotActor.MayManageSeats(true, Owner, Stranger));
        Assert.True(StudioBotActor.MayManageSeats(true, Owner, Owner));

        // ⚠ And on a seat that never recorded its owner it stays permissive, for the
        // same reason the actor rule does.
        Assert.True(StudioBotActor.MayManageSeats(true, "", Stranger));
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
            "StudioBotActor.IsTheBotActing(\n                seat is not null,\n" +
            "                seat?.EnteredByEmail,\n                account.Current?.Email)",
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
