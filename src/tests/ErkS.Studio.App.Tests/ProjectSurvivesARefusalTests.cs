using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A refused project is not a lost project.
///
/// 🔴 THE FOURTH MEMBER OF A FAMILY THAT KEEPS COSTING PEOPLE THEIR WORK. The
/// shape: a boundary refusal meaning «not now, not from here» is read as «gone
/// forever», and local state is destroyed on the strength of it. The seat
/// resume was the first; three project call sites were the next three, sharing
/// one sentence letter for letter.
///
/// The server cannot support the conclusion those sites drew. GET
/// /projects/{id} answers `project_not_found` when FindForActor returns null,
/// and it returns null for four different worlds - deleted, membership ended,
/// a seat not assigned this project, and a wrong id. The commonest is the seat,
/// and it is not an ending at all.
/// </summary>
public sealed class ProjectSurvivesARefusalTests
{
    [Fact]
    public void NOStatusCodeAloneEndsAProject()
    {
        // 🔴 THE ASSERTION THAT HOLDS THE WHOLE FAMILY SHUT. Every refusal the
        // project routes can produce today is ambiguous, so every one of them
        // leaves the workspace open.
        foreach (string code in new[]
        {
            "project_not_found", "bot_project_out_of_scope", "sso_project_out_of_scope",
            "project_identity_scope_required", "project_information_forbidden",
            "sso_handoff_expired", "", "something_this_build_has_never_heard_of",
        })
        {
            StudioProjectAccessVerdict verdict =
                StudioProjectAccessRefusal.Read(code, "Серверийн өгүүлбэр.", seatedAsBot: false);
            Assert.False(verdict.ProjectEnded, code + " closed the project");
        }
    }

    [Fact]
    public void THEVocabularyOfEndingsIsEMPTYAndThatIsTheMEASUREMENT()
    {
        // Not an oversight - a fact about the server, asserted so that adding a
        // code here is a deliberate act with a test to answer to. The day SRV
        // distinguishes «never heard of it» from «not yours» on this route, this
        // test is the one that has to be updated on purpose.
        Assert.Empty(StudioProjectAccessRefusal.CodesThatEndTheProject);
    }

    [Fact]
    public void THESentenceAgreesWithTheDECISIONItTravelsWith()
    {
        // 🔴 THESE WERE SEPARABLE ONCE AND THEY DRIFTED: the sentence said the
        // project had been removed from the person's list while the decision it
        // described was «keep it open». They are one object now, and this is the
        // rule that object exists to keep.
        StudioProjectAccessVerdict kept =
            StudioProjectAccessRefusal.Read("project_not_found", "Алга.", seatedAsBot: false);

        Assert.False(kept.ProjectEnded);
        Assert.Contains("нээлттэй хэвээр", kept.Sentence, StringComparison.Ordinal);

        // And it never tells somebody their project was taken off their list
        // while it is still on it.
        Assert.DoesNotContain("хасагдлаа", kept.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("дууссан", kept.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void THEServersOwnWordsReachThePersonWhenItUsedAny()
    {
        // A. The server writes a specific, actionable Mongolian sentence and
        // Studio used to replace it with its own generic one.
        StudioProjectAccessVerdict verdict = StudioProjectAccessRefusal.Read(
            "bot_project_out_of_scope",
            "Энэ төсөл энэ суудалд томилогдоогүй байна.",
            seatedAsBot: true);

        Assert.Contains(
            "Энэ төсөл энэ суудалд томилогдоогүй байна.", verdict.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ASentenceSTUDIOManufacturedIsNotAttributedToTheSERVER()
    {
        // ThrowIfFailedAsync writes «Cloud ERA server алдаа: 500 …» when the body
        // is empty. Presenting that as «Серверийн хариу» tells the person the
        // server said something it never said. A code is what a structured error
        // body carries, so no code means no server sentence.
        StudioProjectAccessVerdict verdict = StudioProjectAccessRefusal.Read(
            "", "Cloud ERA server алдаа: 500 Internal Server Error", seatedAsBot: false);

        Assert.DoesNotContain("Серверийн хариу", verdict.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ASEATEDMachineIsToldTheTwoWaysOut()
    {
        // «Nothing you can do» is what the old sentence amounted to. A seat has
        // exactly two ways forward: be assigned the project, or stop being a seat
        // for now.
        StudioProjectAccessVerdict verdict =
            StudioProjectAccessRefusal.Read("bot_project_out_of_scope", "Хамрахгүй.", seatedAsBot: true);

        Assert.Contains("томилуулах", verdict.Sentence, StringComparison.Ordinal);
        Assert.Contains("ботын төлөвөөс гарч", verdict.Sentence, StringComparison.Ordinal);

        // And a person signed in as themselves is NOT told to leave bot state.
        StudioProjectAccessVerdict person =
            StudioProjectAccessRefusal.Read("project_not_found", "Алга.", seatedAsBot: false);
        Assert.DoesNotContain("ботын төлөвөөс", person.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYSiteThatClosesAProjectAfterARefusalASKSFirst()
    {
        // 🔴 DERIVED, NOT LISTED. The first version of this rule lived in three
        // files as three copies of one sentence, and that is how it stayed wrong
        // in three places at once. Every caller of the destructive method is read
        // out of the source and held to the named set - so a fourth copy is red
        // the day somebody writes it.
        //
        // Read by the CALL GRAPH of the destructive function, not by a
        // vocabulary of destructive-looking verbs: a hand-written verb list found
        // three of these five and missed two.
        IReadOnlyCollection<string> owners =
            OwnersAcrossTheApp("CloseCurrentCloudProjectAfterAccessEnded(");

        Assert.Equal(
            new[]
            {
                // Asks the shared verdict; closes only if it says so.
                "CheckCurrentProjectAccessAsync",
                // The destructive method's own declaration.
                "CloseCurrentCloudProjectAfterAccessEnded",
                // Asks the shared verdict; closes only if it says so.
                "RefreshCurrentProjectCloudAccessAsync",
                // Absence from the accessible list, guarded against an unread
                // seat-assignment list.
                "RefreshProjectsAsync",
                // The OWNER'S OWN DELETION - not a refusal at all, and the one
                // place where «gone» is established by the person who did it.
                "RunSelectedProjectLifecycleActionAsync",
                // Asks the shared verdict; closes only if it says so.
                "SynchronizeCurrentProjectAsync",
            },
            owners.Order());
    }

    [Fact]
    public void THERefusalSitesAllReadTheSHAREDVerdict()
    {
        // The three that used to decide for themselves now ask, and every
        // verdict is used for the DECISION and not merely for the sentence.
        foreach (string file in new[] { "ShellView.cs", "ShellView.Collaboration.cs" })
        {
            string source = ReadAppSource(file);
            Assert.True(
                Occurrences(source, "StudioProjectAccessRefusal.Read(") > 0,
                file + " no longer asks the shared verdict");
            Assert.Equal(
                Occurrences(source, "StudioProjectAccessRefusal.Read("),
                Occurrences(source, "verdict.ProjectEnded"));

            // And the sentence that caused the damage is gone from the product.
            Assert.DoesNotContain(
                "Төслийн access дууссан тул төсөл таны Studio жагсаалтаас хасагдлаа",
                source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NOCloseIsReachedWithoutACONDITIONInFrontOfIt()
    {
        // 🔴 THE HOLE THE OTHER TWO TESTS LEFT, FOUND BY SABOTAGE. Naming the
        // methods that may close catches a NEW method; counting verdicts catches
        // a verdict read and ignored. Neither sees an UNCONDITIONAL close added
        // inside a method that already legitimately closes - and that is the
        // cheapest way for this defect to come back, because those methods are
        // exactly where somebody handling a refusal would be typing.
        //
        // So «guarded» is read structurally rather than by vocabulary: every
        // call must be the body of an if/else, or sit inside a block whose own
        // brace is the body of one. A close in the open air of a catch block is
        // red no matter what it is called or which method it lives in.
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
        int checkedCalls = 0;
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(file.FullName, Encoding.UTF8);
            for (int at = source.IndexOf("CloseCurrentCloudProjectAfterAccessEnded(", StringComparison.Ordinal);
                at >= 0;
                at = source.IndexOf("CloseCurrentCloudProjectAfterAccessEnded(", at + 1, StringComparison.Ordinal))
            {
                // The declaration itself is not a call.
                if (source.LastIndexOf("private void ", at, StringComparison.Ordinal) is int declaredAt &&
                    declaredAt > 0 && at - declaredAt <= "private void ".Length)
                {
                    continue;
                }

                checkedCalls++;
                Assert.True(
                    IsGuarded(source, at),
                    file.Name + " closes the project without a condition in front of it, at offset " + at);
            }
        }

        // The positive control: three absences above are true of a product that
        // no longer closes projects at all.
        Assert.True(checkedCalls >= 5, "this test read " + checkedCalls + " call sites");
    }

    /// <summary>
    /// Whether the call at <paramref name="index"/> is the body of a condition.
    ///
    /// Two shapes count, because both are in the product: a braceless
    /// «if (…) Close(…);» whose condition is the line above, and a braced block
    /// whose opening brace is itself the body of an if or else.
    /// </summary>
    private static bool IsGuarded(string source, int index)
    {
        if (PrecedingStatement(source, index) is string previous &&
            (previous.StartsWith("if (", StringComparison.Ordinal) ||
             previous.StartsWith("else", StringComparison.Ordinal)))
        {
            return true;
        }

        // Walk back to the brace that opens the block this call sits in.
        int depth = 0;
        for (int at = index; at >= 0; at--)
        {
            if (source[at] == '}')
                depth++;
            else if (source[at] == '{')
            {
                if (depth == 0)
                    return PrecedingStatement(source, at) is string head &&
                        (head.StartsWith("if (", StringComparison.Ordinal) ||
                         head.StartsWith("else", StringComparison.Ordinal));
                depth--;
            }
        }
        return false;
    }

    /// <summary>
    /// The whole statement immediately above <paramref name="index"/>, comments
    /// and blank lines skipped and CONTINUATION LINES JOINED.
    ///
    /// 🔴 THE FIRST VERSION READ ONE LINE AND CALLED THE OWNER'S OWN DELETION
    /// UNGUARDED. Its condition spans three lines, so the line above the brace
    /// was the tail of a boolean expression - «…OrdinalIgnoreCase))» - which
    /// starts with no keyword at all. A reader that assumes its subject fits on
    /// one line reports the shape of its own limitation.
    /// </summary>
    private static string? PrecedingStatement(string source, int index)
    {
        string[] before = source[..index].Split('\n');
        var statement = new List<string>();
        for (int line = before.Length - 2; line >= 0 && line >= before.Length - 40; line--)
        {
            string text = before[line].Trim();
            if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
                continue;

            statement.Insert(0, text);

            // A statement ends where the one before it did. Anything collected
            // after that boundary is the whole of this one.
            if (line == 0)
                break;
            string above = before[line - 1].Trim();
            while (line > 1 && (above.Length == 0 || above.StartsWith("//", StringComparison.Ordinal)))
            {
                line--;
                above = before[line - 1].Trim();
            }
            if (above.EndsWith(";", StringComparison.Ordinal) ||
                above.EndsWith("{", StringComparison.Ordinal) ||
                above.EndsWith("}", StringComparison.Ordinal) ||
                above.EndsWith(":", StringComparison.Ordinal))
            {
                break;
            }
        }

        return statement.Count == 0 ? null : string.Join(" ", statement);
    }

    [Fact]
    public void ANUnreadAssignmentListDoesNotCostAWorkspace()
    {
        // 🔴 THE MEMBER WITH THE RIGHT SENTENCE AND THE WRONG ACTION. When the
        // seat's assignments have not been read, every project fails the
        // visibility test, the accessible set is empty, and the open project used
        // to be closed underneath a sentence that correctly said the list was
        // simply unread.
        string source = ReadAppSource("ShellView.cs");
        int guard = source.IndexOf(
            "if (SeatedAsBot && botAssignedProjectIds is null)", StringComparison.Ordinal);
        Assert.True(guard > 0, "the unread-assignments case decides nothing again");

        // The guarded branch reports and does NOT close. Sliced at its own
        // closing brace rather than a fixed window, which breaks when a comment
        // grows.
        int elseAt = source.IndexOf("\n                else", guard, StringComparison.Ordinal);
        Assert.True(elseAt > guard, "the guard has no alternative branch");
        string kept = source[guard..elseAt];
        Assert.DoesNotContain("CloseCurrentCloudProjectAfterAccessEnded(", kept, StringComparison.Ordinal);
        Assert.Contains("SetStatus(", kept, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static IReadOnlyCollection<string> OwnersAcrossTheApp(string needle)
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
        var found = new List<string>();
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(file.FullName, Encoding.UTF8);
            for (int at = source.IndexOf(needle, StringComparison.Ordinal);
                at >= 0;
                at = source.IndexOf(needle, at + 1, StringComparison.Ordinal))
            {
                string owner = EnclosingMethod(source, at);
                if (!found.Contains(owner, StringComparer.Ordinal))
                    found.Add(owner);
            }
        }

        Assert.NotEmpty(found);
        return found;
    }

    private static string EnclosingMethod(string source, int index)
    {
        int best = -1;
        foreach (string opening in new[]
        {
            "\n    private ", "\n    public ", "\n    internal ", "\n    protected ",
        })
        {
            int at = source.LastIndexOf(opening, index, StringComparison.Ordinal);
            if (at > best)
                best = at;
        }

        Assert.True(best > 0, "no enclosing member was found for the call at " + index);
        int paren = source.IndexOf('(', best);
        int arrow = source.IndexOf("=>", best, StringComparison.Ordinal);
        int head = paren < 0 || (arrow >= 0 && arrow < paren) ? arrow : paren;
        Assert.True(head > best, "the enclosing declaration could not be read");

        string[] words = source[best..head]
            .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(words);
        return words[^1];
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
