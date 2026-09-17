using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The PIN is asked when the BOT is acting, not at every start of a seated machine.
///
/// 🔴 THE OWNER CHANGED A DECISION; THIS IS NOT A REGRESSION. The old rule was written
/// down on purpose - «a seated machine must lock on start-up whoever signs in later» -
/// and it did exactly that. The owner then said what they want instead: «Би бот төлөвөөс
/// гараад эзэмшигч эрхээр нэвтэрчихсэн байхад ботын пин кодыг байнга асуугаад байна. Бот
/// төлөвт шилжсэн үед л пин асуудаг байя.» So the rule moved; the code was not broken.
///
/// 🔴 AND THE OBVIOUS FIX WOULD HAVE CHANGED NOTHING, WHICH IS WHY IT IS PINNED HERE.
/// InstallBotLockIfSeated runs in the constructor (ShellView.cs:455). The session is
/// restored later, in OnRootLoaded, which is the Loaded handler and which additionally
/// awaits an ApplicationIdle dispatcher hop before it calls TryRestoreAsync. So at the
/// instant the lock is installed NOBODY is signed in - and IsTheBotActing answers «the
/// bot is acting» for an empty session, by design. Swapping the condition at the install
/// site would have produced an identical screen and a confident «fixed».
///
/// So the install still fails CLOSED on the machine question, and a RELEASE asks the
/// actor question once the session is final. The direction matters: covering first and
/// uncovering when proven cannot expose the shell, while installing late would leave the
/// owner's desktop usable for the width of a restore.
/// </summary>
public sealed class THEPINIsAskedWhenTheBOTActsNotAtEveryStartTests
{
    private const string Owner = "owner@example.com";

    private const string OtherOwner = "somebody-else@example.com";

    [Theory]
    // The case the owner reported, and the only one whose answer changes.
    [InlineData(true, Owner, Owner, false)]
    // 🔴 THE POSITIVE CONTROLS. Without these three a mutation that answered «never
    // lock» would satisfy the owner's sentence and pass - and the PIN would be gone.
    [InlineData(true, Owner, null, true)]
    [InlineData(true, Owner, OtherOwner, true)]
    [InlineData(true, null, null, true)]
    public void WHOIsActingDecidesWhetherTheLockBelongs(
        bool seated, string? seatOwner, string? signedIn, bool expectLock)
    {
        Assert.Equal(expectLock, StudioBotActor.IsTheBotActing(seated, seatOwner, signedIn));
    }

    [Fact]
    public void ADIFFERENTOwnerDoesNOTTakeTheSeatedMachine()
    {
        // 🔴 №29, IN THE OWNER'S OWN WORDS: «өөр эзэмшигчийн ботыг өөр эзэмшигчийн
        // эрхтэй хольж хутгаж болохгуй шүү». The discriminator is «is it THAT owner»,
        // never «did somebody sign in» - so relaxing the release to account.IsSignedIn
        // would hand a seated machine to any account that can reach the sign-in door.
        Assert.True(StudioBotActor.IsTheBotActing(true, Owner, OtherOwner));
        Assert.False(StudioBotActor.IsTheBotActing(true, Owner, Owner));

        // ⚠ AND THE KNOWN HOLE IS ASSERTED RATHER THAN LEFT TO BE REDISCOVERED: a seat
        // that never recorded who placed it cannot ask the question, and keeps the older
        // permissive answer. Seats made before 2026-09-12 carry an empty owner. Failing
        // closed here would lock the owner out of their own machine - the fault this
        // whole class of rules was written to fix.
        Assert.False(StudioBotActor.IsTheBotActing(true, "", Owner));
        Assert.False(StudioBotActor.IsTheBotActing(true, null, Owner));
    }

    [Fact]
    public void THEINSTALLStillFailsCLOSEDOnTheMachineQuestion()
    {
        // 🔴 THE ASSERTION THAT STOPS THE WRONG FIX COMING BACK. The tempting change is
        // to make the install ask «is the bot acting?». At constructor time that reads
        // an empty session and answers yes, so it changes no behaviour at all - and the
        // next person, seeing the actor question already asked there, would conclude the
        // rule was applied and look no further.
        string seat = ReadAppSource("ShellView.BotSeat.cs");
        string install = Between(seat, "private void InstallBotLockIfSeated()", "\n    }");
        string compact = Compact(install);

        Assert.Contains("StudioBotDeviceStateStore.Read()", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("ActingAsBot", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("IsTheBotActing", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void THERELEASEAsksTheACTORQuestionAndNothingLooser()
    {
        string seat = ReadAppSource("ShellView.BotSeat.cs");
        string release = Between(
            seat, "private void ReleaseBotLockIfOwnerIsActing()", "\n    }");
        string compact = Compact(release);

        Assert.Contains("ActingAsBot", compact, StringComparison.Ordinal);
        Assert.Contains("RemoveBotLock()", compact, StringComparison.Ordinal);

        // ⚠ NOT «somebody is signed in». Spelling it that way here would reinstate the
        // exact widening №29 forbids, in the one place that decides whether the PIN is
        // asked at all.
        Assert.DoesNotContain("account.IsSignedIn", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void THERELEASERunsAFTERTheSessionIsFinalNotBeforeIt()
    {
        // 🔴 THE ORDERING IS THE WHOLE FIX. Released before the restore, the check reads
        // an empty session, decides the bot is acting and leaves the lock exactly where
        // it was - a change that looks applied and does nothing.
        string shell = ReadAppSource("ShellView.cs");
        string loaded = Between(shell, "private async void OnRootLoaded(", "\n    }");

        int restore = loaded.IndexOf("TryRestoreAsync", StringComparison.Ordinal);
        int enforce = loaded.IndexOf("EnforceCompanionLicenseAsync", StringComparison.Ordinal);
        int release = loaded.IndexOf("ReleaseBotLockIfOwnerIsActing", StringComparison.Ordinal);

        Assert.True(restore > 0, "the restore was not found in OnRootLoaded");
        Assert.True(release > 0, "the release was not found in OnRootLoaded");
        Assert.True(release > restore, "the lock is released only after the session is restored");

        // The enforcement loop can sign somebody in too, so the session is not final
        // until it has run - the same reason the SSO proof is fetched after it.
        Assert.True(enforce > 0, "the licence enforcement was not found");
        Assert.True(release > enforce, "the lock is released only after the session is FINAL");
    }

    [Fact]
    public void THESILENTRestorePathRebuildsTheNavigationToo()
    {
        // ⚠ LATENT, NOT THE REPORTED FAULT, AND FIXED HERE BECAUSE IT IS THE TWIN OF ONE
        // THE OWNER ALREADY HIT. The shell is built in the constructor with no owner
        // session, so navigation is assembled as a bot's. RebuildNavigation was added to
        // the INTERACTIVE sign-in when the owner reported «компани цэс алга болчихсон» -
        // and the SILENT restore, which is the ordinary launch, was left without it.
        //
        // It costs nothing today only because StudioBotSurfaceVisibility.HiddenFromABot
        // is empty by the owner's decision №28, so no entry is actually withheld. The day
        // anything is added to that list, the ordinary launch loses the owner's menu
        // again. A gap that is harmless only because a list is empty is a gap.
        string loaded = Between(ReadAppSource("ShellView.cs"), "private async void OnRootLoaded(", "\n    }");

        Assert.Contains("RebuildNavigation()", loaded, StringComparison.Ordinal);
    }

    private static string Compact(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string Between(string source, string anchor, string closer)
    {
        string normalised = source.Replace("\r\n", "\n");
        int at = normalised.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, anchor + " was not found");
        int end = normalised.IndexOf(closer, at, StringComparison.Ordinal);
        Assert.True(end > at, "the end of the block was not found after " + anchor);
        return normalised[at..end];
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
