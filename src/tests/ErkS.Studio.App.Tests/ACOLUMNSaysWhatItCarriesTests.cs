using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The assignment table's last column names a DATE, because a date is what it
/// carries.
///
/// 🔴 IT USED TO BE HEADED «Томилсон» - «the one who appointed» - while the cell
/// held «2026-09-12». The label disagreed with its own data, and the name gave a
/// reader scanning for a person no reason to doubt what they were looking at.
///
/// 🔴 AND THE OTHER FIX WAS REFUSED ON PURPOSE. Filling the column with the
/// appointer would mean printing StudioCloudBotAssignment.AssignedByEmail - an
/// ADDRESS in a slot that names a person, which is the thing the owner rejected
/// outright: «НЭРЭЭР, имэйлээр БИШ. Нэр байхгүй бол тэр нь шийдэх ёстой цоорхой,
/// имэйлээр нөхөх шалтаг биш.» The server publishes no appointer name, so the
/// honest fix is the one that invents nothing.
/// </summary>
public sealed class ACOLUMNSaysWhatItCarriesTests
{
    [Fact]
    public void THEColumnHeaderNamesADateAndTheFieldAgrees()
    {
        string source = ReadAppSource("BotSeatDialogs.cs");

        // 🔴 THE ANCHOR IS ASSERTED BEFORE IT IS TRUSTED. A source-reading test
        // whose anchor has moved is silently green, which would make this file a
        // decoration rather than a guard.
        Assert.Contains(
            "nameof(AssignmentRow.AssignedOn)",
            source,
            StringComparison.Ordinal);

        string column = ColumnDeclaration(source, "AssignmentRow.AssignedOn");
        Assert.Contains("огноо", column, StringComparison.Ordinal);

        // The old pairing, in full: the header that named a person beside the
        // field that carried a date.
        Assert.DoesNotContain("Header = \"Томилсон\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void THEFieldIsFilledFromTheAssignmentDate()
    {
        // The other half of «the label agrees with its data»: renaming the column
        // proves nothing if the cell later starts carrying something else.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.Contains(
            "AssignedOn: item.AssignedAtUtc.ToLocalTime()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NOColumnOfThisTablePrintsAnADDRESS()
    {
        // 🔴 THE RULE THE REFUSED FIX WOULD HAVE BROKEN. The appointer is known
        // only as an e-mail, so binding it into any column here would put an
        // address where a name belongs.
        //
        // 🔴 THE CODE IS SCANNED, NOT THE PROSE - AND THIS TEST FAILED ON ITS
        // OWN SUBJECT FIRST. Written as «the word must not appear», it went red
        // the moment the fix's own comments EXPLAINED why the field is not shown.
        // A word-lock fails on the right answer as readily as the wrong one, so
        // the comments come out before the scan.
        string code = CodeOnly(ReadAppSource("BotSeatDialogs.cs"));

        Assert.DoesNotContain("AssignedByEmail", code, StringComparison.Ordinal);
    }

    [Fact]
    public void THEUnreadFieldIsMARKEDAsUnreadWhereItIsDeclared()
    {
        // 🔴 «UNREAD» IS A CLAIM ABOUT THE WHOLE REPOSITORY, SO IT IS CHECKED
        // THERE. A note saying «nothing reads this» is worth exactly as much as
        // the sweep behind it - and the next person to need an appointer will
        // find the field before they find any prose about it.
        string contracts = ReadAppSource("StudioCloudContracts.cs");
        string declaration = "public string AssignedByEmail { get; set; } = \"\";";

        Assert.Equal(1, Occurrences(contracts, declaration));

        int at = contracts.IndexOf(declaration, StringComparison.Ordinal);
        string preceding = contracts[Math.Max(0, at - 1400)..at];
        Assert.Contains("НЭРЭЭР, имэйлээр БИШ", preceding, StringComparison.Ordinal);
    }

    [Fact]
    public void NOTHINGInTheAppReadsTheAppointersAddress()
    {
        // The sweep the note above stands on, run rather than remembered. The
        // day somebody wires it up, this goes red and the question - «a name or
        // an address?» - gets asked again instead of being answered by default.
        var readers = new List<string>();
        foreach (string file in AppSourceFiles())
        {
            // Comments removed first: explaining WHY the field is not shown must
            // not read as showing it.
            string code = CodeOnly(File.ReadAllText(file, Encoding.UTF8));
            int found = Occurrences(code, "AssignedByEmail");
            if (found == 0)
                continue;

            // The declaration itself is the one permitted occurrence.
            bool declarationOnly =
                found == 1 &&
                code.Contains(
                    "public string AssignedByEmail { get; set; } = \"\";",
                    StringComparison.Ordinal);

            if (!declarationOnly)
                readers.Add(Path.GetFileName(file));
        }

        Assert.True(
            readers.Count == 0,
            "the appointer's address is now read by: " + string.Join(", ", readers) +
            ". If it is being shown, it is an address in a name slot.");
    }

    [Fact]
    public void THEInstrumentKeepsCodeAndDropsComments()
    {
        // 🔴 THE SCANNER IS TESTED BEFORE ITS RESULTS ARE BELIEVED. Two rules
        // above rest entirely on CodeOnly, and its most dangerous failure is the
        // quiet one: a stripper that returned the empty string would make every
        // «this word must not appear» rule pass forever, on any source at all.
        const string sample =
            "var a = Keep1;            // Drop1\n" +
            "/// <summary>Drop2</summary>\n" +
            "var b = Keep2; /* Drop3 */ var c = Keep3;\n" +
            "/* Drop4\n" +
            "   still Drop5 */ var d = Keep4;\n";

        string code = CodeOnly(sample);

        foreach (string kept in new[] { "Keep1", "Keep2", "Keep3", "Keep4" })
            Assert.Contains(kept, code, StringComparison.Ordinal);

        foreach (string dropped in new[] { "Drop1", "Drop2", "Drop3", "Drop4", "Drop5" })
            Assert.DoesNotContain(dropped, code, StringComparison.Ordinal);

        // And it must not answer everything with nothing when given a real file.
        Assert.Contains(
            "AssignmentRow",
            CodeOnly(ReadAppSource("BotSeatDialogs.cs")),
            StringComparison.Ordinal);
    }

    /// <summary>The GridViewColumn line that binds <paramref name="binding"/>.</summary>
    private static string ColumnDeclaration(string source, string binding)
    {
        foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Contains(binding, StringComparison.Ordinal) &&
                line.Contains("GridViewColumn", StringComparison.Ordinal))
            {
                return line;
            }
        }

        Assert.Fail("no GridViewColumn binds " + binding);
        return "";
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    /// <summary>
    /// The source with its comments removed, so a rule about what the code DOES
    /// is not answered by what the comments SAY.
    ///
    /// Deliberately crude - line and block comments, no string-literal awareness.
    /// A name that appears inside a string literal is a name the code carries,
    /// which is exactly what these rules are asking about.
    /// </summary>
    private static string CodeOnly(string source)
    {
        var kept = new StringBuilder(source.Length);
        var inBlock = false;

        foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
        {
            string rest = line;

            while (rest.Length > 0)
            {
                if (inBlock)
                {
                    int close = rest.IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0)
                    {
                        rest = "";
                        break;
                    }

                    inBlock = false;
                    rest = rest[(close + 2)..];
                    continue;
                }

                int lineComment = rest.IndexOf("//", StringComparison.Ordinal);
                int blockOpen = rest.IndexOf("/*", StringComparison.Ordinal);

                if (lineComment >= 0 && (blockOpen < 0 || lineComment < blockOpen))
                {
                    kept.Append(rest[..lineComment]);
                    rest = "";
                    break;
                }

                if (blockOpen >= 0)
                {
                    kept.Append(rest[..blockOpen]);
                    rest = rest[(blockOpen + 2)..];
                    inBlock = true;
                    continue;
                }

                kept.Append(rest);
                rest = "";
            }

            kept.Append('\n');
        }

        return kept.ToString();
    }

    private static IEnumerable<string> AppSourceFiles() =>
        Directory.EnumerateFiles(AppSourceDirectory(), "*.cs", SearchOption.AllDirectories);

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
