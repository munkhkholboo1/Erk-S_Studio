using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The survey template is the owner's document, transcribed - not composed.
///
/// 🔴 A SURVEY IS A LEGAL INSTRUMENT IN A PLANNING PROCESS. The words citizens answer are
/// quoted back in the plan, so a smoother phrasing invented here would put words in the
/// owner's mouth and in the citizens'. Every question and option is checked against the
/// extraction of «санал асуулга.docx», vendored beside these tests.
///
/// 🔴 AND GENERATION DOES NOT CATCH INVENTION. A template that reads beautifully and asks
/// something the document never asked would pass every structural test in this repository;
/// only a comparison with the real document catches it. That is why the document is
/// vendored rather than summarised.
/// </summary>
public sealed class THESURVEYIsTheOWNERSDocumentNotMineTests
{
    private const string Zuunmod = "Зуунмод";

    [Fact]
    public void EVERYQuestionAndOptionAppearsInTheOWNERSDocument()
    {
        IReadOnlyList<string> document = DocumentLines();
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey(Zuunmod);

        // The positive control first: the document was actually read. Without it an empty
        // extraction would make every «appears in the document» check vacuous - and the
        // conclusion drawn would be that the transcription is faithful.
        Assert.Equal(74, document.Count);
        Assert.Contains(document, line => line.Contains("ИРГЭДИЙН САНАЛ АСУУЛГА", StringComparison.Ordinal));

        foreach (CitizenSurveyQuestion question in survey.Questions)
        {
            // ⚠ THE UNIT IS PART OF THE DOCUMENT'S LINE AND NOT PART OF THE QUESTION, AND
            // THIS TEST FOUND THAT. Their paper reads «Ам бүлийн тоо: _____ хүн» - one
            // line holding a question, a box and what the box counts. The model splits
            // them because the form has to draw a box and write «хүн» after it, so the
            // comparison puts them back together rather than the model flattening them.
            string onPaper = question.Kind == CitizenSurveyQuestionKinds.Number
                ? question.Text + " " + question.Unit
                : question.Text;

            Assert.True(
                document.Any(line => Same(line, onPaper)),
                "this question is not in the owner's document: " + onPaper);

            foreach (CitizenSurveyOption option in question.Options)
            {
                Assert.True(
                    document.Any(line => Same(line, option.Text) || StartsWithOption(line, option.Text)),
                    "this option is not in the owner's document: " + option.Text);
            }
        }
    }

    [Fact]
    public void THEDocumentsOwnQuestionCountIsWhatWeBuilt()
    {
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey(Zuunmod);

        // 🔴 SIXTEEN, COUNTED FROM THEIR PAPER: fourteen with answers to tick or write a
        // number in, and two that ask for sentences. A template that quietly dropped one
        // would produce a survey that looks complete and asks citizens less than the owner
        // intended - and nobody would notice until the results were short a column.
        Assert.Equal(16, survey.Questions.Count);
        Assert.Equal(2, survey.Questions.Count(q => q.Kind == CitizenSurveyQuestionKinds.FreeText));
        Assert.Equal(1, survey.Questions.Count(q => q.Kind == CitizenSurveyQuestionKinds.Number));

        // Ordered 1..16 with no gap and no repeat, because the order is what a citizen
        // reads down the page.
        Assert.Equal(
            Enumerable.Range(1, 16),
            survey.OrderedQuestions().Select(question => question.Order));
    }

    [Fact]
    public void THESettlementNameReachesEVERYQuestionThatNamesIt()
    {
        // 🔴 FOUR QUESTIONS CARRY THE TOWN, AND A FIXED LIST WOULD BE WRONG FOR THE SECOND
        // TOWN THAT USED IT. The owner asked for a survey per PROJECT, so the name is a
        // parameter; shipped as a constant, every other settlement's citizens would be
        // asked about Зуунмод.
        ProjectCitizenSurvey other =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Багануур");

        Assert.Equal("Багануур", other.SettlementName);
        Assert.Equal(
            4,
            other.Questions.Count(q => q.Text.Contains("Багануур", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            other.Questions,
            question => question.Text.Contains(Zuunmod, StringComparison.Ordinal));
    }

    [Fact]
    public void WITHNoSettlementTheTokenIsVISIBLERatherThanABlank()
    {
        // ⚠ AN EMPTY NAME MUST NOT PRODUCE « хотын төвийн…». A survey that silently reads
        // as though the town were missing looks like a defect to a citizen and like
        // finished work to whoever prepared it. The placeholder is left showing so it is
        // obvious something has to be filled in.
        ProjectCitizenSurvey unnamed =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("   ");

        Assert.Equal("", unnamed.SettlementName);
        Assert.Contains(
            unnamed.Questions,
            question => question.Text.Contains(CitizenSurveyTemplate.SettlementToken, StringComparison.Ordinal));
    }

    [Fact]
    public void THEOwnWordsLineIsKeptWhereTheDocumentDrawsOne()
    {
        // «Бусад: _______» is not the same as «Бусад». The written line is where a citizen
        // says the thing the form did not think to ask, and a template that offered the
        // tick without the line would throw that away before it was ever written.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey(Zuunmod);

        IReadOnlyList<CitizenSurveyQuestion> inviting = survey.Questions
            .Where(question => question.Options.Any(option => option.InvitesOwnWords))
            .ToList();

        // Their document draws a line after «Бусад» on the баг question, the workplace
        // question, the services question and the future question.
        Assert.Equal(4, inviting.Count);
        foreach (CitizenSurveyQuestion question in inviting)
            Assert.Single(question.Options.Where(option => option.InvitesOwnWords));
    }

    [Fact]
    public void ONLYTheServicesQuestionTakesSeveralAnswers()
    {
        // ⚠ MY DECISION, MARKED AS ONE. The services question asks which organisations -
        // plural - should be added and draws a long line after «Бусад», so it is set as
        // multiple choice; the rest are single. Question 14 is plural too, but its last
        // option is «Бүгдийг хослуулсан зохион байгуулалт», which only means anything as
        // one choice among the others. Pinned so that changing it is deliberate.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey(Zuunmod);

        CitizenSurveyQuestion multiple = Assert.Single(
            survey.Questions.Where(q => q.Kind == CitizenSurveyQuestionKinds.MultipleChoice));

        Assert.Contains("байгууллагуудыг нэмэгдүүлэх", multiple.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYChoiceQuestionActuallyOffersChoices()
    {
        // A choice question with no options is a dead end on paper: the citizen is asked
        // something and given nothing to tick.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey(Zuunmod);

        foreach (CitizenSurveyQuestion question in survey.Questions)
        {
            if (CitizenSurveyQuestionKinds.TakesOptions(question.Kind))
                Assert.True(question.Options.Count >= 2, "too few options: " + question.Text);
            else
                Assert.Empty(question.Options);
        }

        // And every option is distinct within its question - a repeated line splits the
        // same answer across two counts and understates both.
        foreach (CitizenSurveyQuestion question in survey.Questions)
        {
            Assert.Equal(
                question.Options.Count,
                question.Options.Select(option => option.Text).Distinct(StringComparer.Ordinal).Count());
        }

        // Ids are unique across the whole survey, because answers name them.
        var ids = survey.Questions.SelectMany(q => q.Options).Select(o => o.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ADDINGTheSurveyDidNOTRaiseTheFormatVersion()
    {
        // 🔴 THE BOUNDARY THIS COULD HAVE BROKEN, ASSERTED. AutoCAD and Revit each hold
        // their own SupportedProjectFormatVersion and REFUSE a project file numbered higher
        // than they know. The reader contract settles what to do - «Талбар нэмэх нь
        // хувилбар өсгөхгүй», because readers skip fields they do not know - so
        // shipping a survey must not stop the owner's plugins opening every project they
        // have, to add a section those plugins never read.
        Assert.Equal(3, ProjectWorkspace.CurrentFormatVersion);

        // And a project carries one without being given one, so an older file that has
        // never heard of a survey opens with an empty one rather than a null.
        var project = new ProjectWorkspace();
        Assert.NotNull(project.CitizenSurveys);
        Assert.Empty(project.CitizenSurveys.Surveys);
    }

    [Fact]
    public void EDITINGAQuestionKEEPSItsIdsBecauseAnswersNameThem()
    {
        // 🔴 THE CONTRACT'S LOAD-BEARING INVARIANT. A gathered response names the
        // option ids it ticked. Reissue those ids on the next publish - because a word was
        // corrected, or an option reordered - and every answer already collected stops
        // resolving. The tally would count them as unreadable and the result would shrink,
        // with the citizens who gave them already gone home.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey(Zuunmod);
        CitizenSurveyQuestion question = survey.Questions[0];
        string questionId = question.Id;
        List<string> optionIds = question.Options.Select(option => option.Id).ToList();

        // The ordinary edits: fix a word, retitle an option, move it up the page.
        question.Text = question.Text + " (засварлав)";
        question.Options[0].Text = "1-р баг";
        question.Order = 99;

        Assert.Equal(questionId, question.Id);
        Assert.Equal(optionIds, question.Options.Select(option => option.Id));

        // And a clone carries them too - copying a survey must not orphan its answers.
        CitizenSurveyQuestion copy = question.Clone();
        Assert.Equal(questionId, copy.Id);
        Assert.Equal(optionIds, copy.Options.Select(option => option.Id));
    }

    [Fact]
    public void TWOProjectsGetTheirOWNIdsSoAnswersCannotCrossOver()
    {
        // Two towns running the same template must not share option ids: an answer
        // collected in one project would otherwise resolve against the other's survey and
        // be counted there, which is the worst possible failure in a public consultation.
        ProjectCitizenSurvey first = CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");
        ProjectCitizenSurvey second = CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Багануур");

        var firstIds = first.Questions.SelectMany(q => q.Options).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var secondIds = second.Questions.SelectMany(q => q.Options).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(firstIds);
        Assert.Empty(firstIds.Intersect(secondIds, StringComparer.Ordinal));
        Assert.NotEqual(first.Id, second.Id);
    }

    /// <summary>The owner's document, from the copy that travels with these tests.</summary>
    private static IReadOnlyList<string> DocumentLines()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "contracts", "citizen-survey-zuunmod-2026-09-18.json");
        Assert.True(File.Exists(path), "the vendored copy of the owner's survey is missing");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("lines")
            .EnumerateArray()
            .Select(line => line.GetString() ?? "")
            .ToList();
    }

    /// <summary>
    /// The same line, allowing for the settlement name being written in and for the
    /// document's own runs of underscores after a written-in answer.
    /// </summary>
    private static bool Same(string documentLine, string built)
    {
        string a = Normalise(documentLine);
        string b = Normalise(built);
        return a.Equals(b, StringComparison.Ordinal);
    }

    private static bool StartsWithOption(string documentLine, string built)
    {
        // «Бусад: _______________» in the document is the option «Бусад» plus its line.
        string a = Normalise(documentLine);
        string b = Normalise(built);
        return b.Length > 0 && a.StartsWith(b, StringComparison.Ordinal);
    }

    private static string Normalise(string text)
    {
        string trimmed = (text ?? "").Replace("_", "").Replace(":", "").Trim();
        while (trimmed.Contains("  ", StringComparison.Ordinal))
            trimmed = trimmed.Replace("  ", " ");
        return trimmed;
    }
}
