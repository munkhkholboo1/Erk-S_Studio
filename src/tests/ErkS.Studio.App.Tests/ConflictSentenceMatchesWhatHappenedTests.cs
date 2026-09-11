using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// «Press again and it will go up» is a promise, and it is only true when the
/// base actually moved.
///
/// 🔴 THE OWNER PRESSED SYNC TWICE, FIVE MINUTES APART, AND MET THE SAME
/// CONFLICT BOTH TIMES. The sentence ended «дахин синк хийхэд илгээгдэнэ» -
/// unconditionally - so a failed re-read produced advice that could only
/// reproduce the refusal. A sentence that recommends the action that just failed
/// is not a message: it is the loop.
/// </summary>
public sealed class ConflictSentenceMatchesWhatHappenedTests
{
    [Fact]
    public void AREBASEDConflictMayPromiseTheNextAttempt()
    {
        StudioConflictOutcome outcome = StudioConflictOutcome.Rebased(albumConflict: false);

        Assert.True(outcome.BaseMoved);
        Assert.Contains("шинэчлэгдсэн", outcome.Sentence, StringComparison.Ordinal);
        Assert.Contains("илгээгдэнэ", outcome.Sentence, StringComparison.Ordinal);

        // And it still says the work is intact, which is the first thing a
        // person needs to know when a sync stops.
        Assert.Contains("хэвээр байна", outcome.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ANUNCHANGEDBaseMayNOTPromiseAnything()
    {
        // 🔴 THE ASSERTION THE OLD SENTENCE WOULD HAVE FAILED. Nothing moved, so
        // «press again and it will be sent» is false - and worse than false,
        // because acting on it costs the person another round.
        StudioConflictOutcome outcome = StudioConflictOutcome.BaseUnchanged(
            albumConflict: false,
            "Сервертэй холбогдож чадсангүй.");

        Assert.False(outcome.BaseMoved);
        Assert.DoesNotContain("илгээгдэнэ", outcome.Sentence, StringComparison.Ordinal);

        // It says what a repeat would do, so the person does not discover it by
        // doing it.
        Assert.Contains("ижил зөрчилдөөн давтагдана", outcome.Sentence, StringComparison.Ordinal);

        // And it carries the reason the re-read failed, rather than leaving a
        // silence somebody has to guess at.
        Assert.Contains("Сервертэй холбогдож чадсангүй.", outcome.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void THEWorkIsCalledINTACTOnBothPaths()
    {
        // Whatever else changed, the person's own edits survived - proven for
        // the rebase path by RebasingKeepsUnsentWorkTests. Saying so on only one
        // path would leave the other reading like a loss.
        foreach (bool album in new[] { true, false })
        {
            Assert.Contains(
                "Таны засвар хэвээр байна",
                StudioConflictOutcome.BaseUnchanged(album, "").Sentence,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "Таны засвар хэвээр байна",
            StudioConflictOutcome.Rebased(albumConflict: true).Sentence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THESyncReportsWHETHERTheBaseMovedRatherThanAssuming()
    {
        // Read from source: the conflict path needs a live sync to exercise.
        // What is asserted is that the sentence is CHOSEN by a fact the code
        // recorded, not written unconditionally.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"), "private async Task SyncCurrentProjectAsync(");

        Assert.Contains("bool baseMoved = false;", body, StringComparison.Ordinal);
        Assert.Contains("baseMoved = true;", body, StringComparison.Ordinal);
        Assert.Contains("StudioConflictOutcome.Rebased(", body, StringComparison.Ordinal);
        Assert.Contains("StudioConflictOutcome.BaseUnchanged(", body, StringComparison.Ordinal);

        // The old unconditional promise is gone from the product.
        Assert.DoesNotContain(
            "дахин синк хийхэд илгээгдэнэ.\",",
            CodeOnly(body),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MANYLibraryChangesCoalesceIntoONEPassAndKeepTheLAST()
    {
        // 🔴 THE DEBT FROM THE HANG FIX, PAID. That commit asserted the SHAPE of
        // the coalescer by reading source; today's lesson is that source tests
        // see text, not behaviour. This runs the same shape.
        //
        // Two halves, and both are required: a coalescer that runs once but
        // swallows the last change trades a hang for a screen that is quietly
        // one version stale - which is worse, because nobody can see it.
        int passes = 0;
        int observed = 0;
        int state = 0;
        bool running = false;
        bool changedDuring = false;

        void Work()
        {
            passes++;
            observed = state;
        }

        void Queue()
        {
            if (running)
            {
                changedDuring = true;
                return;
            }

            running = true;
            try
            {
                do
                {
                    changedDuring = false;
                    Work();
                }
                while (changedDuring);
            }
            finally
            {
                running = false;
            }
        }

        // Three changes arriving before anything runs: one pass, latest state.
        state = 1;
        Queue();
        Assert.Equal(1, passes);
        Assert.Equal(1, observed);

        // A change that arrives WHILE the pass is running owes exactly one more.
        passes = 0;
        running = true;
        state = 2;
        Queue();
        Assert.Equal(0, passes);
        Assert.True(changedDuring);
        running = false;

        state = 3;
        Queue();
        Assert.Equal(1, passes);
        Assert.Equal(3, observed);
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

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
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
