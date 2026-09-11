using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// On a seated machine every sign-in is the SAME sign-in.
///
/// 🔴 THERE WERE TWO DOORS AND ONE WAS INCOMPLETE. The account menu offered
/// «Эзэмшигчээр нэвтрэх…» and «Нэвтрэх». The first ran the full return; the
/// second only signed in - the bot token stayed in hand, the seat's assignments,
/// scopes and member line stayed cached, the lock was not lifted.
///
/// 🔴 AND THE INCOMPLETE ONE CLOSED THE COMPLETE ONE BEHIND IT. With an owner
/// session now held, StudioBotMenuPlan stops offering OwnerPassport - so the full
/// return disappeared from the menu, and the way back became «Ботын төлөвт
/// буцах…» followed by the owner door. Two steps, neither obvious, to somebody
/// who had already lost a day to «яаж энэ төлвөөс гарах болж байна».
/// </summary>
public sealed class BOTHDoorsLeadOutTests
{
    [Fact]
    public void THEPlainSignInTakesTheOwnerDoorWHEREVERTheMenuOffersIt()
    {
        // 🔴 DERIVED FROM THE MENU, NOT RESTATED BESIDE IT. «Wherever the menu
        // offers the owner door, the plain sign-in reaches the same place» is the
        // rule; written as its own condition it would be true today and drift the
        // first time either side moved - invisibly, because both conditions would
        // still look right.
        foreach (bool seated in new[] { true, false })
        {
            foreach (bool signedIn in new[] { true, false })
            {
                bool menuOffersTheDoor = StudioBotMenuPlan
                    .For(seated, signedIn)
                    .Contains(BotMenuEntry.OwnerPassport);

                Assert.Equal(
                    menuOffersTheDoor,
                    StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(seated, signedIn));
            }
        }

        // 🔴 AND THE VALUES CANNOT PROVE IT, WHICH IS WHY THIS LINE IS HERE. A
        // mutation replaced the derivation with a hand-written
        // «seatedAsBot && !ownerSessionInHand» and every assertion above stayed
        // green - the two agree TODAY, which is precisely the coincidence the
        // derivation exists to survive. What is being claimed is not «the answers
        // match» but «there is one answer», and that is a fact about the code.
        string route = ReadAppSource("StudioSignInRoute.cs");
        Assert.Contains(
            ".For(seatedAsBot, ownerSessionInHand)",
            route,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Contains(BotMenuEntry.OwnerPassport)",
            route,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEREDIRECTHappensEXACTLYInTheTrappedState()
    {
        // 🔴 THE POSITIVE AND NEGATIVE CONTROLS, SO THE RULE ABOVE IS NOT MERELY
        // A RESTATEMENT OF ITS OWN IMPLEMENTATION. If somebody rewrites the
        // derivation, these still say what the answer has to be.
        Assert.True(
            StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(
                seatedAsBot: true,
                ownerSessionInHand: false),
            "a seated machine with no owner session must not get the plain sign-in");

        Assert.False(
            StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(
                seatedAsBot: false,
                ownerSessionInHand: false),
            "an ordinary machine must keep the ordinary sign-in");
        Assert.False(
            StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(
                seatedAsBot: true,
                ownerSessionInHand: true));
        Assert.False(
            StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(
                seatedAsBot: false,
                ownerSessionInHand: true));
    }

    [Fact]
    public void THENonSeatedPathIsUNTOUCHED()
    {
        // 🔴 THE OTHER HALF, ASSERTED SEPARATELY. A repair that quietly sent every
        // sign-in through the seat door would «fix» this by breaking the ordinary
        // one, and the suite would look just as green.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private async Task ToggleAccountAsync()");

        Assert.Contains("await EnsureSignedInAsync();", body, StringComparison.Ordinal);
        Assert.Contains("await VerifyOwnerOnSeatedDeviceAsync();", body, StringComparison.Ordinal);

        // The redirect is guarded; the ordinary sign-in is what is left after it.
        int guard = body.IndexOf(
            "StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(",
            StringComparison.Ordinal);
        int ordinary = body.IndexOf("await EnsureSignedInAsync();", StringComparison.Ordinal);
        Assert.True(guard > 0, "the plain sign-in no longer consults the route");
        Assert.True(ordinary > guard, "the ordinary sign-in must be the path NOT taken by the guard");
    }

    [Fact]
    public void SIGNINGOutIsNotRoutedAnywhere()
    {
        // Leaving is not entering. The seat door asks for a passport, which is
        // the opposite of what somebody pressing «Гарах» wants.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private async Task ToggleAccountAsync()");

        int signOutBranch = body.IndexOf("if (account.IsSignedIn)", StringComparison.Ordinal);
        int guard = body.IndexOf(
            "StudioSignInRoute.GoesThroughTheSeatedOwnerDoor(",
            StringComparison.Ordinal);

        Assert.True(signOutBranch > 0, "the sign-out branch is gone");
        Assert.True(guard > signOutBranch, "the route must not stand in the sign-out path");
        Assert.Contains("account.SignOut();", body[signOutBranch..guard], StringComparison.Ordinal);
    }

    [Fact]
    public void THEOwnerDoorStillRunsTheFULLReturn()
    {
        // 🔴 THE REDIRECT IS ONLY WORTH ANYTHING IF ITS DESTINATION IS COMPLETE.
        // Sending the plain sign-in to a door that had itself stopped clearing the
        // bot token would move the defect rather than remove it - and the counted
        // list is what made the two directions the same size in the first place.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"),
            "private async Task ResumeAsOwnerNowAsync()");

        foreach (string step in new[]
        {
            "account.UseBotToken(null);",
            "unlockedSeatIdentity = null;",
            "botAssignedProjectIds = null;",
            "botAssignedProjectScopes = null;",
            "botSeatMember = null;",
            "ApplyDeviceSeat();",
            "RemoveBotLock();",
            "UpdateAccountUi();",
        })
        {
            Assert.Contains(step, body, StringComparison.Ordinal);
        }

        // And the door the menu shows is the same method, so there is one
        // destination rather than two that have to be kept in step.
        string seat = ReadAppSource("ShellView.BotSeat.cs");
        Assert.Contains(
            "BotMenuEntry.OwnerPassport => Item(\"Эзэмшигчээр нэвтрэх…\", VerifyOwnerOnSeatedDeviceAsync)",
            seat,
            StringComparison.Ordinal);
        Assert.Contains(
            "botLockScreen.OwnerSignInRequested += async () =>",
            seat,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEONEGeneralSignInOfferGoesThroughTheRoutedMethod()
    {
        // 🔴 A SWEEP WAS WRITTEN HERE AND DELETED, AND THE REASON IS WORTH KEEPING.
        // «Every place that offers a sign-in must consult the route» cannot be
        // asked of the source: EnsureSignedInAsync appears in fifteen places and
        // nearly all of them are PREREQUISITES for a task - open the company
        // library, start collaborating - not offers to sign in. Routing those
        // through the seat door would be wrong, so the sweep would have needed a
        // list of exemptions longer than its findings, and an exemption list is
        // how a guard stops being read.
        //
        // The claim that can actually be defended is narrower and true: there is
        // exactly ONE general sign-in offer, and it reaches the routed method.
        //
        // ⚠ And «Нэвтрэх» is not a reliable handle either - the lock screen's PIN
        // button carries the same word for a different act, and the login dialog's
        // own submit button carries it for a third. A scan on the word would have
        // to spare two of its three hits.
        string shell = ReadAppSource("ShellView.cs");

        Assert.Contains(
            "Header = account.IsSignedIn ? \"Гарах\" : \"Нэвтрэх\",",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "signOut.Click += async (_, _) => await ToggleAccountAsync();",
            shell,
            StringComparison.Ordinal);
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

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(Path.Combine(AppSourceDirectory(), fileName), Encoding.UTF8);

    private static string AppSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        Assert.Fail("the application's source folder was not found; this test reads it");
        return "";
    }
}
