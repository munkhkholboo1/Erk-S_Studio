using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The server writes a better sentence than Studio can, and it has to survive
/// the trip to the person.
///
/// 🔴 IT KNOWS WHICH OF FOUR THINGS WENT WRONG BEHIND ONE STATUS CODE, it writes
/// in Mongolian, and it names what to do next. Studio replaced it with a generic
/// sentence of its own in the project family, which is how «this project is not
/// assigned to this seat» reached somebody as «your access has ended» - and cost
/// them their open workspace.
/// </summary>
public sealed class TheServersWordsReachThePersonTests
{
    [Fact]
    public void ACodeWithNOSentenceIsNotTreatedAsSpeech()
    {
        // Both halves are required. A code with an empty message leaves nothing
        // to show the person, and «Серверийн хариу: » with nothing after it
        // reads as a bug - which it would be.
        Assert.False(StudioRefusalSentence.ServerSpoke("project_not_found", "   "));
        Assert.False(StudioRefusalSentence.ServerSpoke("", "Ямар нэг юм."));
        Assert.True(StudioRefusalSentence.ServerSpoke("project_not_found", "Алга."));
    }

    [Fact]
    public void NOTHINGCallsItTheSERVERSAnswerWithoutAsking()
    {
        // 🔴 C(2), AND THE FIRST VERSION OF THIS TEST WAS WORTHLESS. It scanned
        // for the SHAPE of the predicate - «is the error code non-empty» in the
        // four spellings I could think of - and found ZERO files, including the
        // one that holds the rule. It would have stayed green with the rule
        // copied into three more places. The expected value was «exactly one»,
        // so it caught itself; had it been «at most one» it never would have.
        //
        // What is actually testable is the CLAIM rather than the predicate: the
        // phrase «Серверийн хариу» is Studio telling a person these are the
        // server's words. Every file that makes that claim has to ask the one
        // place that answers it.
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
        var claimants = new List<string>();
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            string code = CodeOnly(File.ReadAllText(file.FullName, Encoding.UTF8));
            if (!code.Contains("Серверийн хариу", StringComparison.Ordinal))
                continue;

            claimants.Add(file.Name);
            Assert.True(
                code.Contains("StudioRefusalSentence.ServerSpoke(", StringComparison.Ordinal),
                file.Name + " calls a sentence the server's without asking whether it spoke");
        }

        // The positive control: «every claimant asks» is also true when nobody
        // claims anything, and this rule is worth nothing in that world.
        Assert.NotEmpty(claimants);
    }

    [Fact]
    public void THESeatDialogsASKRatherThanDecide()
    {
        // The positive control for the scan above: «only one file spells the
        // rule» is also true of a codebase where nobody consults it. The seat
        // path had the rule right first and now asks for it.
        string seats = ReadAppSource("BotSeatDialogs.cs");
        Assert.Contains("StudioRefusalSentence.ServerSpoke(", seats, StringComparison.Ordinal);

        string projects = ReadAppSource("StudioProjectAccessRefusal.cs");
        Assert.Contains("StudioRefusalSentence.ServerSpoke(", projects, StringComparison.Ordinal);
    }

    [Fact]
    public void NOHelperIsWrittenThatNobodyCalls()
    {
        // 🔴 THE DEFECT FAMILY THIS WHOLE SESSION KEPT FINDING, APPLIED TO MY
        // OWN WORK. A first version of the shared helper carried a second method
        // that appended the record's path - invented while writing it, wanted by
        // nobody, and it would have read to the next person as a facility the
        // product uses.
        //
        // Every public member of the sentence helper has to be reached from
        // somewhere else in the app.
        string helper = ReadAppSource("StudioRefusalSentence.cs");
        var members = new List<string>();
        foreach (string line in helper.Split('\n'))
        {
            string text = line.Trim();
            if (!text.StartsWith("public static ", StringComparison.Ordinal))
                continue;
            int open = text.IndexOf('(', StringComparison.Ordinal);
            if (open < 0)
                continue;
            string name = text[..open].Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
            if (!members.Contains(name, StringComparer.Ordinal))
                members.Add(name);
        }

        Assert.NotEmpty(members);
        foreach (string member in members)
        {
            Assert.True(
                CallersOutsideTheHelper(member) > 0,
                "StudioRefusalSentence." + member + " is called by nobody");
        }
    }

    private static int CallersOutsideTheHelper(string member)
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
        int found = 0;
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            if (file.Name.Equals("StudioRefusalSentence.cs", StringComparison.Ordinal))
                continue;
            string code = CodeOnly(File.ReadAllText(file.FullName, Encoding.UTF8));
            found += code.Split("StudioRefusalSentence." + member + "(").Length - 1;
        }
        return found;
    }

    private static string CodeOnly(string source) =>
        string.Join(
            "\n",
            source.Split('\n').Where(line =>
            {
                string text = line.TrimStart();
                return !text.StartsWith("//", StringComparison.Ordinal) &&
                    !text.StartsWith("*", StringComparison.Ordinal);
            }));

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
