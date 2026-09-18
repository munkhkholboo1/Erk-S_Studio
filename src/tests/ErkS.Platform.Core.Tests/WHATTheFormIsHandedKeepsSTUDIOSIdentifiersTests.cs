using System.Text.Json;
using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// What the public form is handed, and the identifiers it must not touch.
///
/// 🔴 THE SERVER STORES THESE IDS AND RETURNS THEM ON EVERY ANSWER. If one is altered
/// anywhere on the way, the answers come back naming something this survey does not have:
/// the arithmetic stays perfect and the meaning is gone. Studio would report them as
/// unreadable rather than lose them silently - but the consultation would still be wasted.
/// </summary>
public sealed class WHATTheFormIsHandedKeepsSTUDIOSIdentifiersTests
{
    private const string Project = "project-under-test";

    private static ProjectCitizenSurvey Owners()
    {
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");
        survey.IssuePublicCode("https://erk-s.mn");
        return survey;
    }

    [Fact]
    public void EVERYIdentifierCrossesUNCHANGED()
    {
        ProjectCitizenSurvey survey = Owners();
        CitizenSurveyPublishedDefinition published = CitizenSurveyPublication.For(survey, Project);

        Assert.Equal(survey.Id, published.SurveyId);
        Assert.Equal(survey.PublicCode, published.Code);
        Assert.Equal(
            survey.OrderedQuestions().Select(question => question.Id).ToList(),
            published.Questions.Select(question => question.QuestionId).ToList());

        foreach (CitizenSurveyQuestion question in survey.OrderedQuestions())
        {
            CitizenSurveyPublishedQuestion mirrored = published.Questions
                .Single(candidate => candidate.QuestionId == question.Id);
            Assert.Equal(
                question.Options.Select(option => option.Id).ToList(),
                mirrored.Options.Select(option => option.OptionId).ToList());
        }
    }

    [Fact]
    public void THEKINDSAreTheFOURAgreedWithTheServer()
    {
        CitizenSurveyPublishedDefinition published = CitizenSurveyPublication.For(Owners(), Project);

        var allowed = new[] { "single", "multi", "number", "text" };
        Assert.All(published.Questions, question => Assert.Contains(question.Kind, allowed));

        // The owner's form, COUNTED rather than remembered: this assertion caught me
        // telling the server there was no number question in it. There is one - the
        // household size - and a form built without a numeric control would have shown
        // it as a text box, turning a countable answer into a sentence.
        Assert.Equal(12, published.Questions.Count(question => question.Kind == "single"));
        Assert.Equal(1, published.Questions.Count(question => question.Kind == "multi"));
        Assert.Equal(1, published.Questions.Count(question => question.Kind == "number"));
        Assert.Equal(2, published.Questions.Count(question => question.Kind == "text"));
        Assert.Equal(16, published.Questions.Count);
    }

    [Fact]
    public void ANUNMAPPEDKindTHROWSRatherThanPublishingSomethingElse()
    {
        // BINARY BRANCH ABSORBS THE THIRD: a default arm would publish a new kind as a
        // text box, and the answers would come back as sentences to a question that was
        // meant to be counted - unrecoverably, because nobody was asked to tick anything.
        var survey = new ProjectCitizenSurvey
        {
            Questions = [new() { Id = "q", Order = 1, Text = "x", Kind = "SomethingNew" }],
        };

        Assert.Throws<InvalidDataException>(() => CitizenSurveyPublication.For(survey, Project));
    }

    [Fact]
    public void ABUSADLineTravelsAsAnOPTIONNotAsAWrittenQuestion()
    {
        CitizenSurveyPublishedDefinition published = CitizenSurveyPublication.For(Owners(), Project);

        IReadOnlyList<CitizenSurveyPublishedQuestion> withLines = published.Questions
            .Where(question => question.Options.Any(option => option.InvitesOwnWords))
            .ToList();

        Assert.Equal(4, withLines.Count);
        Assert.All(withLines, question => Assert.NotEqual("text", question.Kind));
    }

    [Fact]
    public void TWOWriteInLinesInONEQuestionAreREFUSED()
    {
        // \U0001F534 A SABOTAGE SWEEP FOUND NOTHING HOLDING THIS. The rule was written after
        // SRV pointed the hazard out, the comment explained it well, and no test failed
        // when the guard was deleted - which is the exact shape of a rule that quietly
        // stops working later. An answer carries ONE Text per question, so a second
        // «Бусад: ___» line loses whichever word the page read first, silently, in a
        // consultation document.
        var survey = new ProjectCitizenSurvey
        {
            Questions =
            [
                new()
                {
                    Id = "q", Order = 1, Text = "Хоёр бичих мөртэй асуулт",
                    Kind = CitizenSurveyQuestionKinds.MultipleChoice,
                    Options =
                    [
                        new() { Id = "o1", Text = "Нэг" },
                        new() { Id = "o2", Text = "Бусад", InvitesOwnWords = true },
                        new() { Id = "o3", Text = "Өөр бусад", InvitesOwnWords = true },
                    ],
                },
            ],
        };

        InvalidDataException refused =
            Assert.Throws<InvalidDataException>(() => CitizenSurveyPublication.For(survey, Project));

        // It names the question, because whoever edited the form has to find it.
        Assert.Contains("Хоёр бичих мөртэй асуулт", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ONEWriteInLineIsSTILLAllowed()
    {
        // The positive control: the refusal must not be «any write-in line at all»,
        // which would refuse the owner's own form.
        ProjectCitizenSurvey survey = Owners();

        CitizenSurveyPublishedDefinition published = CitizenSurveyPublication.For(survey, Project);

        Assert.Equal(
            4, published.Questions.Count(question =>
                question.Options.Any(option => option.InvitesOwnWords)));
    }

    [Fact]
    public void THEOWNERSWordingCrossesUnchangedToo()
    {
        ProjectCitizenSurvey survey = Owners();
        string json = CitizenSurveyPublication.ToJson(survey, Project);

        foreach (CitizenSurveyQuestion question in survey.OrderedQuestions())
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonElement mirrored = parsed.RootElement.GetProperty("Questions")
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("QuestionId").GetString() == question.Id);

            Assert.Equal(question.Text, mirrored.GetProperty("Text").GetString());
        }
    }

    [Fact]
    public void ACODEIsIssuedONCEAndNeverReplacedUnderAPrintedQR()
    {
        ProjectCitizenSurvey survey = Owners();
        string issued = survey.PublicCode;

        // 🔴 A SECOND MINT WOULD ORPHAN EVERY POSTER ALREADY ON A WALL.
        Assert.False(survey.IssuePublicCode("https://erk-s.mn"));
        Assert.Equal(issued, survey.PublicCode);

        Assert.Equal(CitizenSurveyCode.Length, issued.Length);
        Assert.True(issued.All(Uri.IsHexDigit), issued + " is not plain hex");
        Assert.True(CitizenSurveyQrCode.For(survey.PublicCode, survey.PublicFormUrl).IsDrawn);
    }

    [Fact]
    public void TWOSurveysNeverShareACode()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 200; index++)
            Assert.True(codes.Add(CitizenSurveyCode.Mint()));
    }

    [Fact]
    public void THEPROJECTCrossesWithTheSurvey()
    {
        // 🔴 SRV FOUND THIS: nothing in the published file said which project the
        // consultation belonged to, and the collect route is scoped by project. Without
        // it they could not resolve (project, survey) to a code at all.
        CitizenSurveyPublishedDefinition published =
            CitizenSurveyPublication.For(Owners(), Project);

        Assert.Equal(Project, published.ProjectId);
    }

    [Fact]
    public void ADEFINITIONWithNoProjectIsREFUSEDRatherThanPublished()
    {
        // 🔴 A ONE-WAY TRIP. The form would serve, citizens would answer, and the answers
        // could never be collected back - the route would have nothing to match on. That
        // failure surfaces only after a consultation is finished, which is the point at
        // which nothing can be done about it. Refused while somebody is still looking.
        foreach (string? missing in new[] { null, "", "   " })
        {
            Assert.Throws<InvalidDataException>(
                () => CitizenSurveyPublication.For(Owners(), missing));
        }
    }
}
