using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Before anybody has answered, the page says so once.
///
/// 🔴 THE OWNER SAW SIXTEEN IDENTICAL CARDS. With no responses at all, every question
/// falls below the reading threshold and each produces its own «not enough answers to
/// draw a conclusion» finding. That card is exactly right once answers are arriving - it
/// stops a confident sentence being written from nine people - and it is unreadable
/// before any have arrived: the screen filled with sixteen copies of a non-statement,
/// while the one sentence a reader actually needed, that collection has not started, was
/// nowhere on the page.
///
/// ⚠ AND THE SENTENCE EXISTED. The page already had «Хариулт ирээгүй тул дүгнэлт алга.»
/// behind a test for findings.Count == 0 - a condition that can only be true for a survey
/// with NO QUESTIONS. It was written, it was correct, and it was unreachable. That is the
/// same family as the refresh nobody called: code whose own author believed it ran.
/// </summary>
public sealed class NOBODYHasAnsweredIsONEFactNotSIXTEENTests
{
    [Fact]
    public void ANUNANSWEREDSurveyYieldsAFindingPerQUESTIONNotNone()
    {
        // The premise that made the old branch dead. Pinned here so that the page's
        // condition cannot quietly drift back to counting findings.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");

        CitizenSurveyResult empty = CitizenSurveyTally.Of(survey, []);
        IReadOnlyList<CitizenSurveyFinding> findings = CitizenSurveyFindings.Read(empty);

        Assert.Equal(0, empty.ResponseCount);
        Assert.NotEmpty(findings);
        Assert.All(findings, finding =>
            Assert.Equal(CitizenSurveyFindingWeights.TooFew, finding.Weight));

        // 🔴 THE POINT: the count is NOT zero, so «no findings» never meant «no answers».
        Assert.True(
            findings.Count >= survey.TickedQuestions().Count,
            "the reading no longer produces a card per unanswered question");
    }

    [Fact]
    public void THEPAGEAsksTheRESPONSECountRatherThanTheFindingCount()
    {
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("result.ResponseCount == 0", page, StringComparison.Ordinal);

        // And the unreachable condition is no longer the one carrying the empty state.
        int responses = page.IndexOf("result.ResponseCount == 0", StringComparison.Ordinal);
        int counted = page.IndexOf("findings.Count == 0", StringComparison.Ordinal);
        Assert.True(
            counted < 0 || responses < counted,
            "the empty state hangs off the finding count again, which cannot be zero here");
    }

    [Fact]
    public void ONEAnswerMustNotBringBackTheWallOfCards()
    {
        // 🔴 THE FIRST FIX ONLY MOVED THE DEFECT. Zero responses was special-cased; the
        // owner's own first submission put the page straight back into sixteen identical
        // «not enough answers» cards, because the reading threshold is thirty. Between 1
        // and 29 the wall returned in full.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");
        CitizenSurveyQuestion first = survey.TickedQuestions()[0];

        var one = new CitizenSurveyResponse
        {
            Id = "r1",
            Answers =
            [
                new CitizenSurveyAnswer
                {
                    QuestionId = first.Id,
                    OptionIds = [first.Options[0].Id],
                },
            ],
        };

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, [one]);
        IReadOnlyList<CitizenSurveyFinding> findings = CitizenSurveyFindings.Read(result);

        Assert.Equal(1, result.ResponseCount);
        Assert.NotEmpty(findings);
        Assert.All(findings, finding =>
            Assert.Equal(CitizenSurveyFindingWeights.TooFew, finding.Weight));

        // So the page cannot key the summary off the response count alone.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));
        Assert.Contains("nothingReadableYet", page, StringComparison.Ordinal);
        Assert.Contains(
            "finding.Weight == CitizenSurveyFindingWeights.TooFew",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WIDTHCappedBlocksStayAtTheLeftMargin()
    {
        // ⚠ A MaxWidth inside a stretching StackPanel lets WPF CENTRE the child. On a wide
        // window the finding cards and question blocks drifted into the middle of the page
        // while their own section headings stayed at the margin, reading as two unrelated
        // columns. Every capped block is pinned left.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        // 🔴 EACH SITE, NOT A TOTAL. A sabotage sweep showed the first draft counting both
        // things and comparing the totals - and the file has other Left alignments, on
        // buttons and the QR image, so the count stayed satisfied while a capped block
        // lost its pin. Locking a total instead of each place is how a guard keeps
        // passing after the thing it guarded moved.
        var capped = 0;
        for (int at = page.IndexOf("MaxWidth = 760", StringComparison.Ordinal);
             at >= 0;
             at = page.IndexOf("MaxWidth = 760", at + 1, StringComparison.Ordinal))
        {
            capped++;
            // ⚠ THE WINDOW ENDS WHERE THE BLOCK DOES. A fixed character count reached
            // into the NEXT capped block and found ITS pin - a sabotage sweep dropped one
            // pin and this test stayed green. An anchored read that overruns its subject
            // is not anchored to anything.
            int closes = page.IndexOf("};", at, StringComparison.Ordinal);
            string window = closes < 0 ? page[at..] : page[at..closes];
            Assert.True(
                window.Contains("HorizontalAlignment = HorizontalAlignment.Left", StringComparison.Ordinal),
                $"a width-capped block at offset {at} has no left pin - it will drift to the centre");
        }

        Assert.True(capped > 0, "the width cap disappeared; this test no longer measures it");
    }

    private static int Count(string text, string needle) =>
        text.Split(needle).Length - 1;

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
            System.Text.Encoding.UTF8);

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
