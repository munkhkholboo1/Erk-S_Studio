using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Whose name the account block prints.
///
/// 🔴 THE OWNER SIGNED IN ON THEIR OWN MACHINE AND WAS SHOWN THE BOT. «Би дөнгөж
/// сая өөрийн үндсэн бүртгэлээр нэвтэрсэн. Гэтэл бот төлөв хэвээрээ.» The line
/// asked «does this machine hold a seat» and printed «Бот: &lt;name&gt;» whenever the
/// answer was yes - which stays yes for as long as the machine is a seat, signed
/// in or not. Their own name went into a tooltip.
///
/// A seat is a fact about the DEVICE. This line answers WHO IS ACTING. Both must
/// be said, and only one of them gets the name.
/// </summary>
public sealed class AccountIdentityLineTests
{
    [Fact]
    public void ANOwnerOnTheirOwnSEATEDMachineIsNamedAsThePerson()
    {
        Assert.Equal(
            AccountIdentityKind.PersonOnSeatedDevice,
            StudioAccountIdentityLine.For(
                ownerSessionInHand: true, deviceHoldsSeat: true, seatUnlocked: false));
    }

    [Fact]
    public void THEPersonWinsTheNameEvenWhileTheSeatIsOPEN()
    {
        // Both can be true for a moment: the PIN opens the seat and the owner
        // then signs in. The work goes out under the person's credential, so
        // the person is who this line names.
        Assert.Equal(
            AccountIdentityKind.PersonOnSeatedDevice,
            StudioAccountIdentityLine.For(
                ownerSessionInHand: true, deviceHoldsSeat: true, seatUnlocked: true));
    }

    [Fact]
    public void ASeatWithNobodySignedInIsTheBot()
    {
        Assert.Equal(
            AccountIdentityKind.SeatLocked,
            StudioAccountIdentityLine.For(
                ownerSessionInHand: false, deviceHoldsSeat: true, seatUnlocked: false));
        Assert.Equal(
            AccountIdentityKind.SeatActing,
            StudioAccountIdentityLine.For(
                ownerSessionInHand: false, deviceHoldsSeat: true, seatUnlocked: true));
    }

    [Fact]
    public void ANOrdinaryMachineIsThePersonOrNobody()
    {
        Assert.Equal(
            AccountIdentityKind.Person,
            StudioAccountIdentityLine.For(
                ownerSessionInHand: true, deviceHoldsSeat: false, seatUnlocked: false));
        Assert.Equal(
            AccountIdentityKind.SignedOut,
            StudioAccountIdentityLine.For(
                ownerSessionInHand: false, deviceHoldsSeat: false, seatUnlocked: false));
    }

    [Fact]
    public void EVERYSeatedKindStillSaysTheMachineHoldsASeat()
    {
        // «A seated machine says so wherever the account is shown» was true
        // when there was one seated kind. There are three now, and the promise
        // has to hold for the one this change adds - otherwise signing in as
        // the owner quietly hides that the machine is a seat at all, which is
        // the opposite defect and just as bad.
        foreach (bool signedIn in new[] { true, false })
        {
            foreach (bool unlocked in new[] { true, false })
            {
                AccountIdentityKind kind = StudioAccountIdentityLine.For(
                    ownerSessionInHand: signedIn, deviceHoldsSeat: true, seatUnlocked: unlocked);
                Assert.True(
                    StudioAccountIdentityLine.ShowsDeviceSeat(kind),
                    $"a seated machine stopped saying so as {kind}");
            }
        }

        // And the other direction, so the flag is not simply always true.
        Assert.False(StudioAccountIdentityLine.ShowsDeviceSeat(AccountIdentityKind.Person));
        Assert.False(StudioAccountIdentityLine.ShowsDeviceSeat(AccountIdentityKind.SignedOut));
    }

    [Fact]
    public void THEPanelASKSTheRuleInsteadOfDecidingForItself()
    {
        // 🔴 THIS RULE HAS BEEN WRONG EVERY TIME IT LIVED INSIDE A METHOD THAT
        // BUILDS CONTROLS. Pulled out, it is only worth having if the panel
        // stops testing `seatedAs is not null` for itself - a second copy of
        // the question is how the two answers drift apart.
        string source = ReadAppSource("ShellView.cs");
        int start = source.IndexOf(
            "AccountIdentityKind identity = StudioAccountIdentityLine.For(",
            StringComparison.Ordinal);
        Assert.True(start > 0, "the account panel does not ask the rule");

        // The window starts where the rule has ANSWERED - asking it is what
        // the one permitted `seatedAs is not null` is for - and runs to the end
        // of the block that paints the answer. Inside it nothing may re-derive
        // the answer from the seat, because a second copy of the question is
        // how two answers drift apart.
        int painted = source.IndexOf(
            "accountStatusText.Text = identity switch", start, StringComparison.Ordinal);
        int end = source.IndexOf("accountButton.ToolTip", painted, StringComparison.Ordinal);
        Assert.True(painted > start, "the answer is never painted");
        Assert.True(end > painted, "the end of the identity block was not found");
        Assert.DoesNotContain(
            "seatedAs is not null",
            source[painted..end],
            StringComparison.Ordinal);
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
