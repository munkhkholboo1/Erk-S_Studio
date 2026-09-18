using System.Text;
using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Most of the form is ticked; what is written by hand stands apart, at the end.
///
/// 🔴 THE OWNER ASKED, AND THE ANSWER IS A MEASUREMENT, NOT A PROMISE: «санал
/// асуулгад гараар бичдэг хэсэг байна уу? бид тест шиг ихэнх асуулгыг дандаа
/// сонголтоор хийдэг болгох нь зүйтэй. үнэхээр гараар бичдэг хэсэг байвал тусд нь
/// оруулаарай … асуулгын төгсгөлд ч юм у» The numbers below are the answer, pinned so
/// that editing the form cannot quietly change what was reported.
///
/// ⚠ AND THE TWO BLOCKS ARE COUNTED DIFFERENTLY, which is why the split is a rule rather
/// than a heading. A tick becomes a share of those who answered; a sentence can only be
/// read. Folding a written question into the charts would either invent a bar per unique
/// sentence or drop the answer entirely - and dropping it is silent.
/// </summary>
public sealed class THEWRITTENPartStandsApartAtTheENDTests
{
    [Fact]
    public void THEOWNERSFormIsFOURTEENTickedAndTWOWritten()
    {
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");

        Assert.Equal(16, survey.Questions.Count);
        Assert.Equal(14, survey.TickedQuestions().Count);
        Assert.Equal(2, survey.WrittenQuestions().Count);
    }

    [Fact]
    public void THEIROwnPaperAlreadyPutsTheWrittenONESLast()
    {
        // Nothing was reordered to satisfy the request - their document ends that way.
        // If that ever stops being true, the reordering below is doing real work and
        // somebody should know it rather than discovering it in a printed form.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");

        IReadOnlyList<CitizenSurveyQuestion> asWritten = survey.OrderedQuestions();
        Assert.Equal(
            CitizenSurveyQuestionKinds.FreeText, asWritten[^1].Kind);
        Assert.Equal(
            CitizenSurveyQuestionKinds.FreeText, asWritten[^2].Kind);
    }

    [Fact]
    public void NORMALIZEMovesWrittenQuestionsLastAndKeepsTheirOrderInsideEachBlock()
    {
        var survey = new ProjectCitizenSurvey
        {
            Questions =
            [
                new() { Id = "a", Order = 1, Text = "тик 1", Kind = CitizenSurveyQuestionKinds.SingleChoice },
                new() { Id = "w1", Order = 2, Text = "бичмэл 1", Kind = CitizenSurveyQuestionKinds.FreeText },
                new() { Id = "b", Order = 3, Text = "тик 2", Kind = CitizenSurveyQuestionKinds.Number },
                new() { Id = "w2", Order = 4, Text = "бичмэл 2", Kind = CitizenSurveyQuestionKinds.FreeText },
                new() { Id = "c", Order = 5, Text = "тик 3", Kind = CitizenSurveyQuestionKinds.MultipleChoice },
            ],
        };

        survey.Normalize();

        Assert.Equal(
            new[] { "a", "b", "c", "w1", "w2" },
            survey.OrderedQuestions().Select(question => question.Id).ToArray());
    }

    [Fact]
    public void THESPLITLosesNothingAndDuplicatesNothing()
    {
        // The two accessors are the only route the page uses, so a question in neither
        // would vanish from the screen while still being counted in the totals - the
        // worst shape of all, because the page would look complete.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");

        List<string> split = survey.TickedQuestions().Select(question => question.Id)
            .Concat(survey.WrittenQuestions().Select(question => question.Id))
            .ToList();

        Assert.Equal(survey.Questions.Count, split.Count);
        Assert.Equal(split.Count, split.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            survey.OrderedQuestions().Select(question => question.Id).OrderBy(id => id, StringComparer.Ordinal),
            split.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void AWRITTENLineInsideAChoiceQuestionIsSTILLAChoiceQuestion()
    {
        // ⚠ THE DISTINCTION THE OWNER'S FORM ACTUALLY TURNS ON. Four of their choice
        // questions end in «Бусад: ___» - a written line inside a ticked question. That
        // is not a written QUESTION: the ticks still count, and only the few who chose
        // «Бусад» wrote anything. Treating those four as written would have moved
        // four counted questions out of the statistics entirely.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");

        IReadOnlyList<CitizenSurveyQuestion> withOwnWords = survey.OrderedQuestions()
            .Where(question => question.Options.Any(option => option.InvitesOwnWords))
            .ToList();

        Assert.Equal(4, withOwnWords.Count);
        Assert.All(withOwnWords, question =>
            Assert.NotEqual(CitizenSurveyQuestionKinds.FreeText, question.Kind));
        Assert.All(withOwnWords, question =>
            Assert.Contains(survey.TickedQuestions(), ticked => ticked.Id == question.Id));
    }

    [Fact]
    public void THEPAGEDrawsTheWrittenBlockApartFromTheCharts()
    {
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        // It asks the model which questions are written rather than deciding again here.
        Assert.Contains("WrittenQuestions()", page, StringComparison.Ordinal);

        // And the chart loop skips them instead of charting sentences.
        Assert.Contains("!written.Contains(", page, StringComparison.Ordinal);
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
