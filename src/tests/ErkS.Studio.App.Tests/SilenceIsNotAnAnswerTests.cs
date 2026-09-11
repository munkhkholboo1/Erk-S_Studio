using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// «I have no rules for you» and «I could not tell you my rules» are different
/// facts, and a list cannot hold both.
///
/// 🔴 THE ONE WAY TO SWITCH OFF «KEEP THE RULES ON THE SERVER» WITHOUT ANYBODY
/// NOTICING. GetServerRulesAsync returned an empty list for a refusal, a
/// dropped network, an unparseable body AND for a server that genuinely has no
/// rules. The client then ran on its own defaults, and nothing anywhere said
/// which of the two worlds it was in.
///
/// Today's only consumer reads it correctly. The hazard is the next one, and
/// the difference is what kind of rule it carries:
///
///     a rule that DISPLAYS   may fall back to a default
///     a rule that RESTRICTS  may NOT - «no rule» reads as «allowed», and a
///                            limit the server could not state stops being
///                            enforced
/// </summary>
public sealed class SilenceIsNotAnAnswerTests
{
    [Fact]
    public void ANEmptyListFromTheSERVERIsStillAnANSWER()
    {
        // The case the old shape could not express. The server spoke; it has no
        // rules; that is a fact and the client may act on it.
        StudioServerRuleAnswer answered = StudioServerRuleAnswer.FromServer([]);

        Assert.True(answered.ServerAnswered);
        Assert.Empty(answered.StatedRules);
    }

    [Fact]
    public void SILENCERefusesToBeReadAsRules()
    {
        // 🔴 THE ASSERTION THAT MAKES THE TYPE WORTH HAVING. A caller that
        // forgets to ask does not quietly receive «no rules» - it fails, on its
        // first run, loudly. For a restricting rule that is the difference
        // between «the limit could not be fetched» and «there is no limit».
        StudioServerRuleAnswer silent =
            StudioServerRuleAnswer.Unanswered("Сервер дүрмээ өгсөнгүй: 503 Service Unavailable.");

        Assert.False(silent.ServerAnswered);
        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => silent.StatedRules);

        // And the refusal carries WHY, because a silence nobody can explain is
        // the thing this whole mechanism was built after.
        Assert.Contains("503", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void THEReasonTravelsWithTheSilence()
    {
        StudioServerRuleAnswer silent =
            StudioServerRuleAnswer.Unanswered("Сервертэй холбогдож чадсангүй: HttpRequestException.");

        Assert.Contains("HttpRequestException", silent.WhyNot, StringComparison.Ordinal);

        // An answer carries no excuse, because there is nothing to excuse.
        Assert.Equal("", StudioServerRuleAnswer.FromServer([]).WhyNot);
    }

    [Fact]
    public void EVERYExitFromTheRulesChannelSaysWhichWorldItIsIn()
    {
        // 🔴 DERIVED FROM THE METHOD, NOT LISTED. Four ways out - not signed in,
        // refused, unparseable/unreachable, answered - and a fifth added later
        // that returns a bare list would put the ambiguity straight back. The
        // method may not return anything but an answer.
        string body = MethodBody(
            ReadAppSource("StudioAccountService.cs"),
            "public async Task<StudioServerRuleAnswer> GetServerRulesAsync(");

        Assert.Equal(0, Occurrences(CodeOnly(body), "return [];"));
        Assert.Equal(3, Occurrences(body, "StudioServerRuleAnswer.Unanswered("));
        Assert.Equal(1, Occurrences(body, "StudioServerRuleAnswer.FromServer("));
    }

    [Fact]
    public void THEDisplayRuleStillFallsBackAndStillRetries()
    {
        // The behaviour is deliberately UNCHANGED. The presence window is a
        // display rule: a default is the right answer when the server is silent,
        // and the next visit asks again rather than pinning one bad moment for
        // the rest of the session.
        string body = MethodBody(
            ReadAppSource("ShellView.ProjectOverview.cs"),
            "private async Task RefreshPresenceRuleAsync()");

        int guard = body.IndexOf("if (!answer.ServerAnswered)", StringComparison.Ordinal);
        int latch = body.IndexOf("presenceRuleLoaded = true;", StringComparison.Ordinal);
        Assert.True(guard > 0, "the consumer no longer distinguishes silence from an answer");
        Assert.True(latch > guard, "silence must not latch the rule for the rest of the session");

        // And it reads the rules only on the answered side.
        Assert.Contains("answer.StatedRules", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NOBODYReadsTheRULESWithoutAskingFirst()
    {
        // 🔴 THE TYPE MAKES THE MISTAKE LOUD; THIS MAKES IT IMPOSSIBLE TO SHIP.
        // Throwing catches a forgetful caller on its first run - but only if
        // somebody runs that path. Every file that reads the rules is held to
        // having asked whether there are any.
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
        var readers = new List<string>();
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            if (file.Name.Equals("StudioServerRuleAnswer.cs", StringComparison.Ordinal))
                continue;
            string code = CodeOnly(File.ReadAllText(file.FullName, Encoding.UTF8));

            // 🔴 THE PROPERTY WAS RENAMED BECAUSE OF THIS TEST, AND THAT WAS THE
            // RIGHT FIX. It was «Stated», and the scan matched
            // StudioCompanionServerAnswer.Stated - an unrelated enum member in
            // another file - reporting the account service as reading rules it
            // never touches. Whole-word matching did not help: the collision was
            // a whole word.
            //
            // Third time in one day that a name-shaped search found the wrong
            // thing. The lesson is not «write cleverer searches»: it is that a
            // name a search cannot tell apart is a name that hides its own
            // callers from everybody, this test included.
            if (!code.Contains(".StatedRules", StringComparison.Ordinal))
                continue;

            readers.Add(file.Name);
            Assert.True(
                code.Contains("ServerAnswered", StringComparison.Ordinal),
                file.Name + " reads the rules without asking whether the server answered");
        }

        // The positive control: «every reader asks» is also true where nobody
        // reads, and the rule would then be worth nothing.
        Assert.NotEmpty(readers);
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string CodeOnly(string source) =>
        string.Join(
            "\n",
            source.Split('\n').Where(line =>
            {
                string text = line.TrimStart();
                return !text.StartsWith("//", StringComparison.Ordinal) &&
                    !text.StartsWith("*", StringComparison.Ordinal);
            }));

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
