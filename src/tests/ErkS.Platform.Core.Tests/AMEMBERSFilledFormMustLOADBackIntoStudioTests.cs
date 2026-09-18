using System.Text.Json;
using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The form a member fills in, and the file they hand back.
///
/// 🔴 THE WHOLE POINT OF THE TRIAL IS THAT THE ANSWERS ARRIVE. The owner is having the
/// project's own members fill the survey in to check that the processing works, so the one
/// thing that must not break is the round trip: what the page writes has to be what Studio
/// reads. A form that renders beautifully and produces a file nobody can load would waste
/// everybody's afternoon and look like success until the import.
/// </summary>
public sealed class AMEMBERSFilledFormMustLOADBackIntoStudioTests
{
    private static ProjectCitizenSurvey Owners() =>
        CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");

    [Fact]
    public void THEFILEShapeTheFormWritesIsTheONEStudioReads()
    {
        // 🔴 THE KEY NAMES ARE THE CONTRACT. The page assembles JSON by hand in
        // JavaScript; a property renamed in C# would leave it writing keys nobody reads,
        // and the failure is SILENT - every form loads as an empty answer.
        string html = CitizenSurveyFormHtml.Build(Owners());

        var sample = new CitizenSurveyResponseDocument
        {
            SurveyId = "s",
            Responses =
            [
                new CitizenSurveyResponse
                {
                    Answers = [new CitizenSurveyAnswer { QuestionId = "q", Number = 1 }],
                },
            ],
        };
        using JsonDocument serialized = JsonDocument.Parse(JsonSerializer.Serialize(sample));

        foreach (JsonProperty property in serialized.RootElement.EnumerateObject())
            Assert.Contains(property.Name + ":", html, StringComparison.Ordinal);
        foreach (JsonProperty property in serialized.RootElement
                     .GetProperty("Responses")[0].EnumerateObject())
        {
            Assert.Contains(property.Name + ":", html, StringComparison.Ordinal);
        }

        foreach (string key in new[] { "QuestionId", "OptionIds", "Text", "Number" })
            Assert.Contains(key + ":", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WHATAMemberHandsBackDeserialisesAndMerges()
    {
        // Built the way the page builds it, then read by the real store - the round trip
        // the trial depends on, end to end, without a browser.
        ProjectCitizenSurvey survey = Owners();
        CitizenSurveyQuestion first = survey.TickedQuestions()[0];

        string handedBack = $$"""
            {
              "SurveyId": "{{survey.Id}}",
              "Cursor": "",
              "CollectedAtUtc": "2026-09-18T10:00:00+00:00",
              "Responses": [
                {
                  "Id": "member-1",
                  "SubmittedAtUtc": "2026-09-18T10:00:00+00:00",
                  "Answers": [
                    { "QuestionId": "{{first.Id}}",
                      "OptionIds": ["{{first.Options[0].Id}}"],
                      "Text": "", "Number": null }
                  ]
                }
              ]
            }
            """;

        CitizenSurveyResponseDocument? read =
            JsonSerializer.Deserialize<CitizenSurveyResponseDocument>(handedBack);

        Assert.NotNull(read);
        Assert.Equal(survey.Id, read!.SurveyId);

        var held = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        Assert.Equal(1, CitizenSurveyResponseStore.Merge(held, read.Responses, read.Cursor));

        // And it reaches the analysis, which is what is actually being checked.
        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, held.Responses);
        CitizenSurveyQuestionTally counted =
            result.Questions.Single(question => question.QuestionId == first.Id);

        Assert.Equal(1, counted.Answered);
        Assert.Equal(0, result.UnreadableAnswerCount);
    }

    [Fact]
    public void THEOWNERSWordingReachesThePageUnchanged()
    {
        // Contract invariant 7, on the offline route. Nothing substitutes phrases here,
        // and the escaping must not quietly reword anything either.
        ProjectCitizenSurvey survey = Owners();
        string html = CitizenSurveyFormHtml.Build(survey);

        foreach (CitizenSurveyQuestion question in survey.OrderedQuestions())
        {
            Assert.Contains(question.Text, html, StringComparison.Ordinal);
            foreach (CitizenSurveyOption option in question.Options)
                Assert.Contains(option.Text, html, StringComparison.Ordinal);
        }

        Assert.Contains(survey.Title, html, StringComparison.Ordinal);
    }

    [Fact]
    public void THEWRITTENQuestionsComeLastOnThePageToo()
    {
        ProjectCitizenSurvey survey = Owners();
        string html = CitizenSurveyFormHtml.Build(survey);

        int lastTicked = survey.TickedQuestions()
            .Max(question => html.IndexOf(question.Text, StringComparison.Ordinal));
        int firstWritten = survey.WrittenQuestions()
            .Min(question => html.IndexOf(question.Text, StringComparison.Ordinal));

        Assert.True(firstWritten > lastTicked, "a written question was drawn among the ticked ones");
        Assert.Contains("Бичмэл хэсэг", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ONECHOICEQuestionGetsRadiosAndTheMULTIPLEOneGetsBoxes()
    {
        // Radios where only one answer is allowed is not decoration: checkboxes on a
        // single-choice question let a member tick three, and the tally would count a
        // person once per option - shares over 100% on a question that cannot exceed it.
        ProjectCitizenSurvey survey = Owners();
        string html = CitizenSurveyFormHtml.Build(survey);

        CitizenSurveyQuestion single = survey.OrderedQuestions()
            .First(question => question.Kind == CitizenSurveyQuestionKinds.SingleChoice);
        CitizenSurveyQuestion many = survey.OrderedQuestions()
            .First(question => question.Kind == CitizenSurveyQuestionKinds.MultipleChoice);

        Assert.Contains($"type=\"radio\" name=\"{single.Id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"type=\"checkbox\" name=\"{many.Id}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void THEPAGEPOSTSWhenServedAndSavesAFileWhenOpenedFromAFolder()
    {
        // ONE FILE, TWO MODES. The people answering have phones and a browser, so the
        // served page must submit by itself - a phone cannot hand a downloaded file to
        // anybody. The offline branch stays because the server may not be up.
        string html = CitizenSurveyFormHtml.Build(Owners());

        Assert.Contains("location.protocol", html, StringComparison.Ordinal);
        Assert.Contains("method: 'POST'", html, StringComparison.Ordinal);
        Assert.Contains("a.download", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ARETRIEDSubmissionCarriesTheSAMEIdSoNobodyIsCountedTwice()
    {
        // A phone on a weak signal retries. A fresh id per attempt would enter that
        // person again, and no count downstream could tell the copies from real people.
        string html = CitizenSurveyFormHtml.Build(Owners());

        Assert.Contains("window.erksResponseId = window.erksResponseId ||", html, StringComparison.Ordinal);
        Assert.Contains("Id: window.erksResponseId", html, StringComparison.Ordinal);
    }

    [Fact]
    public void THEPAGEAsksTheNetworkForNOTHING()
    {
        // It is opened from a shared folder or a memory stick, possibly on a machine with
        // no internet at all. One external reference and the form renders wrong - or, on a
        // fonts CDN, tells somebody outside that the survey is being filled in.
        string html = CitizenSurveyFormHtml.Build(Owners());

        foreach (string reach in new[] { "http://", "https://", "//cdn", "<script src" })
            Assert.DoesNotContain(reach, html, StringComparison.OrdinalIgnoreCase);
    }
}
