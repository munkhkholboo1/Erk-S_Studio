using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The navigation is rebuilt wherever who-is-acting changes - in BOTH directions.
///
/// 🔴 THE OWNER LOST A MENU AND COULD NOT GET IT BACK: «компани цэс алга болчихсон
/// байна», in the owner state and in the bot state alike. The navigation is built
/// once, and on a machine that holds a bot seat it is built with NO owner session -
/// so «Компани» and «Төслийн мэдээлэл» were correctly left out for a bot at start-up,
/// and nothing added them when the owner signed in.
///
/// 🔴 THE ASYMMETRY WAS THE WHOLE DEFECT, AND IT PREDATES THE ACTOR FIX. Signing OUT
/// has rebuilt the navigation all along; signing IN never did. So the rule that takes
/// surfaces away ran and the one that gives them back did not - and the seat survives
/// a restart, which is why the owner could not escape it. The earlier discriminator
/// («is this machine seated») would have hidden the menu from the owner even after a
/// rebuild, so that fix is what makes this one able to work.
///
/// ⚠ WHAT THIS FILE PROVES AND WHAT IT DOES NOT: the shell is WPF and needs a window,
/// an account and a project to observe, so these are source assertions about WHERE
/// the rebuild is called. The rule it feeds is tested directly beside it, in
/// THEOWNERONASeatedMachineSeesTheirOwnProjectsTests.
/// </summary>
public sealed class THEMENUMovesWithWhoIsActingTests
{
    /// <summary>
    /// Every method that changes who is acting on this machine. Named rather than
    /// discovered, because the point is that a new one must be added here on purpose.
    /// </summary>
    private static readonly (string File, string Signature)[] ActorChanges =
    [
        ("ShellView.cs", "private async Task<bool> EnsureSignedInAsync()"),
        ("ShellView.BotSeat.cs", "private async Task EnterBotStateNowAsync(StudioBotDeviceState seat)"),
        ("ShellView.BotSeat.cs", "private async Task ResumeAsOwnerNowAsync()"),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EVERYActorChangeRebuildsTheNavigation(int which)
    {
        (string file, string signature) = ActorChanges[which];
        string body = MethodBody(ReadAppSource(file), signature);

        Assert.Contains("RebuildNavigation();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SIGNINGOutAlreadyDidItAndStillDoes()
    {
        // The direction that was never broken, asserted so a tidy-up cannot make the
        // pair asymmetric again in the other direction.
        string source = Lf(ReadAppSource("ShellView.cs"));
        int signOut = source.IndexOf("account.SignOut();", StringComparison.Ordinal);
        Assert.True(signOut > 0, "the sign-out path is gone");

        int rebuild = source.IndexOf("RebuildNavigation();", signOut, StringComparison.Ordinal);
        int nextMethod = source.IndexOf("\n    private", signOut, StringComparison.Ordinal);
        Assert.True(
            rebuild > 0 && (nextMethod < 0 || rebuild < nextMethod),
            "signing out no longer rebuilds the navigation");
    }

    [Fact]
    public void AREBUILDRestoresTheHighlightItJustThrewAway()
    {
        // 🔴 FOUND WHILE FIXING THE MENU, NOT REPORTED BY ANYBODY. RebuildNavigation
        // makes fresh Borders and every one of them starts transparent, so any rebuild
        // not immediately followed by SelectPage left the shell looking as if no page
        // were open. The sign-out path has done exactly that all along. Adding three
        // more rebuilds would have spread a cosmetic fault, so the highlight moved
        // into the rebuild instead of being re-applied at each caller.
        string body = MethodBody(ReadAppSource("ShellView.cs"), "private void RebuildNavigation()");

        Assert.Equal(2, Occurrences(body, "HighlightActiveNavItem();"));
    }

    [Fact]
    public void THEHighlightReadsTheSHELLSPageNotACallersArgument()
    {
        // Taking a parameter would have forced every rebuild to know which page is
        // open - and a rebuild happens precisely when the caller is thinking about
        // something else. Reading activePage is what lets it be called from anywhere
        // without going through SelectPage, whose job includes closing projects.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private void HighlightActiveNavItem()");

        Assert.Contains("candidate == activePage", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AREBUILDNowPutsEVERYPageBackForEitherActor()
    {
        // The composition, so this file does not only assert call sites. Since the
        // owner ruled that surfaces stay and actions ask, a rebuild must produce the
        // same navigation for both - which is what makes the rebuild itself the
        // thing that matters rather than the rule it consults.
        Assert.Empty(StudioBotSurfaceVisibility.HiddenFromABot);
        foreach (string page in StudioBotSurfaceVisibility.AllPages)
        {
            Assert.True(StudioBotSurfaceVisibility.IsVisible(
                StudioBotActor.IsTheBotActing(true, SeatOwner, signedInEmail: ""), page));
            Assert.True(StudioBotSurfaceVisibility.IsVisible(
                StudioBotActor.IsTheBotActing(true, SeatOwner, SeatOwner), page));
        }
    }

    private const string SeatOwner = "owner@erk-s.mn";

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string Lf(string source) => source.Replace("\r\n", "\n");

    private static string MethodBody(string source, string signature)
    {
        string normalised = Lf(source);
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
