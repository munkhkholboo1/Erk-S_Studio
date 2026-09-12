using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// An empty project list explains ITS OWN reason for being empty.
///
/// 🔴 ONE SENTENCE WAS SERVING THREE DIFFERENT REASONS, and on a bot seat it was
/// false advice. The screen said «Cloud ERA бүртгэлээр нэвтэрнэ үү» to a machine that
/// had signed in with its PIN - so following it changes nothing, or talks the reader
/// out of bot state to fix a list that is empty because nobody has assigned the seat
/// a project yet. The owner named what it should say: «Тухайн суудалд томилогдсон
/// төсөл одоогоор байхгүй».
///
/// 🔴 AND THE RIGHT WORDS WERE ALREADY BEING COMPUTED. The status line under the
/// shell has said «гишүүн томилогдоогүй · томилогдсон төсөл алга» all along. Nothing
/// new was invented: the phrase has one home now and both places read it.
/// </summary>
public sealed class THEEMPTYListSaysWHYItIsEmptyTests
{
    [Fact]
    public void ASEATIsNEVERToldToSignIn()
    {
        // The defect itself, in one assertion: a seat is signed in.
        (string title, string message) =
            StudioBotProjectVisibility.ExplainEmptyList(Assigned());

        Assert.DoesNotContain("нэвтэрнэ үү", title);
        Assert.DoesNotContain("нэвтэрнэ үү", message);
        Assert.DoesNotContain("Cloud ERA", title + message);
    }

    [Fact]
    public void ASEATAssignedNothingIsToldWHOAssigns()
    {
        // «Empty» without «who can change that» is a dead end. The licence holder
        // makes assignments, so that is what the sentence says.
        (string title, string message) =
            StudioBotProjectVisibility.ExplainEmptyList(Assigned());

        Assert.Contains(
            StudioBotProjectVisibility.Capitalised(
                StudioBotProjectVisibility.NoAssignedProjectsMn),
            title);
        Assert.Contains("лиценз эзэмшигч", message);
    }

    [Fact]
    public void ANUNREADListIsNOTTheSameAsAnEmptyOne()
    {
        // 🔴 TWO REASONS THAT MUST NOT SHARE A SENTENCE. «The server has not answered»
        // is «try again»; «you have been assigned nothing» is «ask the owner». Folded
        // together, one of the two readers is sent to fix the wrong thing - and the
        // unread case is the one that looks identical from the outside.
        (string unreadTitle, string unreadMessage) =
            StudioBotProjectVisibility.ExplainEmptyList(null);
        (string emptyTitle, string emptyMessage) =
            StudioBotProjectVisibility.ExplainEmptyList(Assigned());

        Assert.NotEqual(unreadTitle, emptyTitle);
        Assert.NotEqual(unreadMessage, emptyMessage);
        Assert.Equal(StudioBotProjectVisibility.AssignmentsUnreadMn, unreadMessage);
        Assert.Contains("Сервертэй", unreadMessage);
        Assert.DoesNotContain("Сервертэй", emptyMessage);
    }

    [Fact]
    public void THEPhraseHasONEHomeAndBOTHPlacesReadIt()
    {
        // 🔴 THE STATUS LINE WAS ALREADY RIGHT, so it is the one that was shared from
        // rather than rewritten. Two spellings of «this seat has no projects» is how
        // the two screens come to disagree about the same fact - and the one under
        // the shell is the one the owner had been reading.
        string seat = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Contains(
            "StudioBotProjectVisibility.NoAssignedProjectsMn",
            seat,
            StringComparison.Ordinal);
        Assert.DoesNotContain("· томилогдсон төсөл алга.", seat, StringComparison.Ordinal);
    }

    [Fact]
    public void THESeatIsAskedBEFORESignedInBecauseASeatIsNotSignedIn()
    {
        // ⚠ ORDER IS THE WHOLE FIX AND IT IS A SOURCE ASSERTION: a seat has no owner
        // session, so «!account.IsSignedIn» is true on a seat and would answer first.
        // Asked in the other order, the seat gets the sign-in advice again and every
        // sentence above is unreachable.
        string body = MethodBody(ReadAppSource("ShellView.cs"), "ApplyProjectBrowserView(");
        int bot = body.IndexOf("if (ActingAsBot)", StringComparison.Ordinal);
        int signedIn = body.IndexOf("else if (!account.IsSignedIn)", StringComparison.Ordinal);

        Assert.True(bot > 0, "the seat's own empty state is gone");
        Assert.True(signedIn > bot, "the sign-in branch answers before the seat's");
    }

    [Fact]
    public void ANOWNERWithNoProjectsStillGetsTheirOwnSentence()
    {
        // ⚠ THE THIRD STATE, WHICH IS THE EASIEST TO LOSE: a signed-in owner with no
        // projects yet. It must keep saying «create one or open a file» - neither
        // «sign in» nor «ask for an assignment».
        string source = ReadAppSource("ShellView.cs");

        Assert.Contains("Төсөл одоогоор алга", source, StringComparison.Ordinal);
        Assert.Contains(
            "Шинэ төсөл үүсгэх эсвэл өмнөх төслийн файлыг нээнэ үү.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEOFFLINEFallbackNAMESTheWideningItDoes()
    {
        // 🔴 THE ONE BRANCH THAT LISTS EVERY PROJECT ON THE DISK. Everywhere else a
        // cloud folder appears only because the server returned it for THIS account;
        // when the cloud call fails, every local project is listed so the person can
        // work - and until this line said so, that widening was silent.
        //
        // ⚠ THE PROJECT FOLDER IS PER WINDOWS USER, NOT PER ACCOUNT, and a plain
        // local project carries no owner field, so two accounts on one machine meet
        // here. The owner warned about exactly this: «локал төслүүд өөр өөр
        // эзэмшигчийн хаяг дээр харагдаад байх вий». Closing the gap needs their
        // decision; saying it out loud did not.
        string source = ReadAppSource("ShellView.cs");

        Assert.Contains(
            "зөвхөн энэ компьютер дээрх төслүүд",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "өөр бүртгэлийн төсөл байж болно",
            source,
            StringComparison.Ordinal);
    }

    private static HashSet<string> Assigned() => new(StringComparer.Ordinal);

    private static string MethodBody(string source, string anchor)
    {
        string normalised = source.Replace("\r\n", "\n");
        int at = normalised.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, anchor + " was not found");
        int end = normalised.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(end > at, "the end of the block was not found");
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
