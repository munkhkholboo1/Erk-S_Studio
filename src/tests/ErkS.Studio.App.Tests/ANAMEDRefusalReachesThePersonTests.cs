using System.Text;
using System.Text.RegularExpressions;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// When the server refuses with a reason, the person is told the reason.
///
/// 🔴 THE SERVER SAID WHY SIX TIMES AND NOBODY SAW IT ONCE. Every
/// «/bot-seats» route was behind a guard requiring an owner with no seat - and
/// releasing the seat was on that list, so the only way out of bot state was
/// refused by the precondition it existed to satisfy. The server answered
/// «bot_state_owner_action_forbidden» each time, by name.
///
/// The owner saw an empty table and a button that did nothing. A whole day went
/// into ownership models, device identity, fingerprints, identity schemes and
/// migration - and the answer had been sent, and thrown away on arrival, from
/// the first minute.
///
/// An empty table is not an answer. A button that does nothing is not an answer.
/// </summary>
public sealed class ANAMEDRefusalReachesThePersonTests
{
    /// <summary>
    /// Where a person meets a bot-seat refusal: the dialogs and the shell menu.
    /// Not the service - its job is to raise the refusal, not to display it.
    /// </summary>
    private static readonly string[] Surfaces =
    [
        "BotSeatDialogs.cs",
        "ShellView.BotSeat.cs",
    ];

    [Fact]
    public void NOCALLToTheServerIsCaughtAndDiscardedInSilence()
    {
        // 🔴 DERIVED FROM THE CALLS, NOT FROM A LIST OF PLACES. Every catch whose
        // try block talks to the server has to say something - report it, show
        // it, or rethrow. Listing today's offenders would be right once.
        var found = new List<string>();
        var silent = new List<string>();

        foreach (string file in Surfaces)
        {
            string source = ReadAppSource(file).Replace("\r\n", "\n");
            foreach ((string body, int line) in CatchBlocksOverServerCalls(source))
            {
                found.Add(body);
                if (!SaysSomething(body))
                    silent.Add(file + ":" + line);
            }
        }

        // The instrument first: a scan that matched nothing proves nothing about
        // the code it did not read.
        // Pinned by what the scan MUST FIND, not by a count alone. A scanner that
        // quietly stopped matching would satisfy «no silent catches» perfectly -
        // it would have nothing left to judge - and a bare count is itself free
        // to be lowered. This block is the branch the whole rule was written for.
        Assert.Contains(
            found,
            body => body.Contains(
                "Байгаа суудлуудыг шалгаж чадсангүй", StringComparison.Ordinal));
        Assert.True(
            found.Count >= 3,
            "the scan found only " + found.Count + " catch blocks over server calls");
        Assert.Empty(silent);
    }

    [Fact]
    public void THESeatLookupBeforeCreatingSaysWhyItCouldNotLook()
    {
        // The instance that cost the day. Falling through and creating is still
        // right - a duplicate seat is recoverable and refusing to seat the
        // machine is not - but the person is told the lookup failed and why.
        // Read from the CATCH itself, not from the method around it: that method
        // has other catches that do report, and a whole-method search would have
        // been satisfied by one of those while this branch stayed silent.
        string source = ReadAppSource("BotSeatDialogs.cs")
            .Replace("\r\n", "\n");
        string block = Assert.Single(
            CatchBlocksOverServerCalls(source)
                .Where(item => item.Body.Contains("lookup", StringComparison.Ordinal))
                .Select(item => item.Body));

        Assert.Contains("BotSeatErrors.Describe(", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a catch body tells anybody anything - on screen, in the refusal
    /// record, by letting the exception continue, or by recording an outcome its
    /// caller reports.
    ///
    /// 🔴 THE LAST CLAUSE IS NOT A LOOPHOLE, IT IS THE RULE STATED PROPERLY. The
    /// defect is a refusal after which NOTHING HAPPENS. A catch that counts the
    /// seat it just found already free, and lets the caller print the tally, has
    /// told the person - through its caller. The first version of this rule
    /// called that a silent swallow, which would have pushed a correct branch
    /// into reporting noise on the one path where the refusal IS the goal.
    ///
    /// Written as a rule rather than an exemption list on purpose: a list of
    /// today's allowed offenders rots, and the next real one hides behind it.
    /// </summary>
    private static bool SaysSomething(string body)
    {
        string code = string.Join(
            "\n",
            body.Split('\n').Where(line =>
            {
                string text = line.TrimStart();
                return !text.StartsWith("//", StringComparison.Ordinal) &&
                    !text.StartsWith("*", StringComparison.Ordinal);
            }));

        return code.Contains("SetStatus(", StringComparison.Ordinal) ||
            code.Contains("resultText.Text", StringComparison.Ordinal) ||
            code.Contains("summaryText.Text", StringComparison.Ordinal) ||
            code.Contains("StudioMessageDialog.Show(", StringComparison.Ordinal) ||
            code.Contains("BotSeatErrors.Describe(", StringComparison.Ordinal) ||
            code.Contains("StudioBoundaryRefusals.", StringComparison.Ordinal) ||
            code.Contains("throw", StringComparison.Ordinal) ||
            // An outcome the caller reports: a tally, a flag, a collected note.
            Regex.IsMatch(code, @"\w+\+\+|\w+\s*(=|\+=)\s*[^=]") ||
            code.Contains(".Add(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Every catch block whose guarded region contains a call to the account
    /// service, with the line its catch sits on.
    /// </summary>
    private static IEnumerable<(string Body, int Line)> CatchBlocksOverServerCalls(string source)
    {
        foreach (Match match in Regex.Matches(source, @"catch\s*\(([^)]*)\)([^{]*)\{"))
        {
            // The guarded region: from the «try» that opens this chain to the
            // catch itself. A catch with no server call above it is somebody
            // else's rule.
            int tryAt = source.LastIndexOf("try", match.Index, StringComparison.Ordinal);
            if (tryAt < 0)
                continue;
            string guarded = source[tryAt..match.Index];
            if (!guarded.Contains("await account.", StringComparison.Ordinal))
                continue;

            int start = match.Index + match.Length;
            int depth = 1;
            int at = start;
            while (at < source.Length && depth > 0)
            {
                if (source[at] == '{')
                    depth++;
                else if (source[at] == '}')
                    depth--;
                at++;
            }

            yield return (source[start..(at - 1)], source[..match.Index].Count(c => c == '\n') + 1);
        }
    }

    private static string MethodBodyContaining(string source, string needle)
    {
        string normalised = source.Replace("\r\n", "\n");
        int at = normalised.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(at > 0, needle + " was not found");
        int start = normalised.LastIndexOf("\n    private ", at, StringComparison.Ordinal);
        Assert.True(start > 0, "no enclosing member for " + needle);
        int end = normalised.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(end > start, "no end for the member holding " + needle);
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
