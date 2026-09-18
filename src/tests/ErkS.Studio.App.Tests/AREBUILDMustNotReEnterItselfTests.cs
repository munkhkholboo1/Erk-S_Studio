using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows.Controls;
using Xunit;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Rebuilding a panel must not be able to re-enter itself.
///
/// 🔴 THIS KILLED THE WHOLE WINDOW, WITH NO ERROR AT ALL. The survey page rebuilds its
/// detail panel on every refresh, and that rebuild created a fresh ComboBox, subscribed to
/// SelectionChanged, and THEN assigned SelectedValue. Assigning it on a fresh box raises
/// the event, the handler called the refresh again, the refresh built another box - and a
/// StackOverflowException cannot be caught: the process is terminated by the runtime. No
/// catch block runs, no dialog appears, nothing is logged. Studio simply vanished when the
/// owner opened the page.
///
/// ⚠ AND IT WAS HIDDEN BEHIND A DISCONNECTED WIRE. Nothing called that refresh until the
/// missing call was fixed, so the page was merely blank. Connecting it turned a dead page
/// into a fatal one - a dead path hides its own defects, and repairing one is exactly when
/// they arrive.
///
/// These tests pin the PREMISE (that the assignment raises the event) as well as the rule,
/// because the rule is only necessary while the premise holds - and if WPF ever stopped
/// raising it, a test asserting only the ordering would go on demanding a dance nobody
/// needs, with no explanation of why.
/// </summary>
public sealed class AREBUILDMustNotReEnterItselfTests
{
    [Fact]
    public void ASSIGNINGSelectedValueRAISESSelectionChanged()
    {
        // The premise the whole hazard rests on, measured rather than assumed.
        OnSta(() =>
        {
            var box = new ComboBox
            {
                ItemsSource = new[] { new Row("a", "A"), new Row("b", "B") },
                DisplayMemberPath = "Label",
                SelectedValuePath = "Id",
            };

            var raised = 0;
            box.SelectionChanged += (_, _) => raised++;
            box.SelectedValue = "b";

            Assert.Equal(1, raised);
        });
    }

    [Fact]
    public void SUBSCRIBINGBeforeSelectingReENTERSTheRebuild()
    {
        // The defect in miniature, bounded so the test does not take the runner down with
        // it. Depth climbing past 1 IS the crash - in the real page nothing stopped it.
        OnSta(() =>
        {
            var depth = 0;
            var deepest = 0;

            void Rebuild()
            {
                depth++;
                deepest = Math.Max(deepest, depth);
                if (depth < 8)
                {
                    var box = new ComboBox
                    {
                        ItemsSource = new[] { new Row("a", "A") },
                        SelectedValuePath = "Id",
                    };
                    box.SelectionChanged += (_, _) => Rebuild();
                    box.SelectedValue = "a";
                }

                depth--;
            }

            Rebuild();

            Assert.True(deepest > 1, "the hazard did not reproduce; this test no longer measures it");
        });
    }

    [Fact]
    public void SELECTINGBeforeSubscribingDoesNOTReEnter()
    {
        // The rule the page now follows.
        OnSta(() =>
        {
            var depth = 0;
            var deepest = 0;

            void Rebuild()
            {
                depth++;
                deepest = Math.Max(deepest, depth);
                if (depth < 8)
                {
                    var box = new ComboBox
                    {
                        ItemsSource = new[] { new Row("a", "A") },
                        SelectedValuePath = "Id",
                    };
                    box.SelectedValue = "a";
                    box.SelectionChanged += (_, _) => Rebuild();
                }

                depth--;
            }

            Rebuild();

            Assert.Equal(1, deepest);
        });
    }

    [Fact]
    public void THESURVEYPageSelectsBeforeItSubscribesANDKeepsAGuard()
    {
        // 🔴 BOTH HALVES, BECAUSE EITHER ALONE ROTS. The ordering fixes today's control;
        // the guard covers the next one somebody adds to this same rebuild.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        int selects = page.IndexOf("surveySplitBox.SelectedValue =", StringComparison.Ordinal);
        int subscribes = page.IndexOf("surveySplitBox.SelectionChanged +=", StringComparison.Ordinal);

        Assert.True(selects >= 0 && subscribes >= 0, "the split picker changed shape");
        Assert.True(
            selects < subscribes,
            "the split picker subscribes before it selects - that is the crash");

        Assert.Contains("if (refreshingSurveyDetail)", page, StringComparison.Ordinal);
        Assert.Contains("refreshingSurveyDetail = true;", page, StringComparison.Ordinal);
        Assert.Contains("finally", page, StringComparison.Ordinal);
    }

    private sealed record Row(string Id, string Label);

    private static void OnSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static string CodeOnly(string source)
    {
        Assert.DoesNotContain("/" + "*", source, StringComparison.Ordinal);

        var kept = new List<string>();
        foreach (string line in source.Split((char)10))
        {
            string bare = line.TrimEnd((char)13);
            if (bare.TrimStart().StartsWith("//", StringComparison.Ordinal))
                continue;
            int at = bare.IndexOf("//", StringComparison.Ordinal);
            kept.Add(at >= 0 ? bare[..at] : bare);
        }

        return string.Join(((char)10).ToString(), kept);
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindSourceRoot().FullName, "ErkS.Studio.App", fileName),
            Encoding.UTF8);

    private static DirectoryInfo FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src");
            if (Directory.Exists(candidate))
                return new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.Fail("the source tree was not found; this test reads it");
        return new DirectoryInfo(".");
    }
}
