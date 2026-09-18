using System.Text.Json;
using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// What a phone actually sent, read back by the code that will read it in production.
///
/// 🔴 EVERY TEST BEFORE THIS ONE ASSERTED TEXT. They pinned that the page CONTAINS a POST
/// branch, a response key, the right keys in the assembled document - all true, all
/// checked by reading source. None of them proved the script RUNS, and the script is the
/// riskiest artefact in the feature: it executes on a stranger's phone, in a browser
/// nobody here chose, and its output is the only record of what a citizen said.
///
/// So this fixture was captured from a real browser run of the real generated form: the
/// page was served over http, filled in the way a person fills it - one option per
/// question, two ticks on the multiple-choice one, a household size, a «Бусад» line and
/// both written answers - and the document it POSTed is stored verbatim beside its own
/// published definition. Not one id here was typed by hand.
///
/// ⚠ THE FAILURE IT GUARDS AGAINST IS INVISIBLE. If a key were spelled differently, or an
/// id were rewritten in transit, this reader would produce answers that resolve to
/// nothing - and a survey whose answers all fail to resolve looks exactly like a survey
/// nobody answered. There is no error, no empty state, no complaint: just a consultation
/// that appears to have been ignored by the public.
/// </summary>
public sealed class WHATAPhoneSENTIsWhatSTUDIOReadsTests
{
    private static CitizenSurveyPublishedDefinition Published() =>
        JsonSerializer.Deserialize<CitizenSurveyPublishedDefinition>(
            SharedContractCopies.Read(SharedContractCopies.CitizenSurveyPublished))!;

    private static CitizenSurveyResponseDocument Submitted() =>
        JsonSerializer.Deserialize<CitizenSurveyResponseDocument>(
            SharedContractCopies.Read(SharedContractCopies.CitizenSurveyBrowserSubmission))!;

    /// <summary>
    /// The survey rebuilt from the definition the form was generated from.
    ///
    /// Mechanical, and deliberately so: the mapping only moves ids and kinds across, so
    /// the test measures the product rather than a reconstruction that could be wrong.
    /// </summary>
    private static ProjectCitizenSurvey SurveyFromDefinition()
    {
        CitizenSurveyPublishedDefinition definition = Published();
        var survey = new ProjectCitizenSurvey
        {
            Id = definition.SurveyId,
            PublicCode = definition.Code,
            Title = definition.Title,
        };

        var order = 0;
        foreach (CitizenSurveyPublishedQuestion question in definition.Questions)
        {
            survey.Questions.Add(new CitizenSurveyQuestion
            {
                Id = question.QuestionId,
                Order = ++order,
                Text = question.Text,
                Kind = question.Kind switch
                {
                    CitizenSurveyPublication.SingleChoiceWireKind =>
                        CitizenSurveyQuestionKinds.SingleChoice,
                    CitizenSurveyPublication.MultipleChoiceWireKind =>
                        CitizenSurveyQuestionKinds.MultipleChoice,
                    CitizenSurveyPublication.NumberWireKind => CitizenSurveyQuestionKinds.Number,
                    _ => CitizenSurveyQuestionKinds.FreeText,
                },
                Options = question.Options
                    .Select(option => new CitizenSurveyOption
                    {
                        Id = option.OptionId,
                        Text = option.Text,
                        InvitesOwnWords = option.InvitesOwnWords,
                    })
                    .ToList(),
            });
        }

        return survey;
    }

    [Fact]
    public void THESUBMISSIONBelongsToTheDefinitionBesideIt()
    {
        // The pair has to stay a pair. Re-capturing one alone would leave a fixture that
        // resolves nothing, and the tests below would then be measuring themselves.
        Assert.Equal(Published().SurveyId, Submitted().SurveyId);
    }

    [Fact]
    public void EVERYANSWERResolvesAgainstTheSurveyItWasAskedFrom()
    {
        // 🔴 THE HEADLINE. Sixteen answers came off a browser and every one of them names
        // a question and options this survey actually has. UnreadableAnswerCount is the
        // measure that would expose any drift - and it must be zero.
        ProjectCitizenSurvey survey = SurveyFromDefinition();
        CitizenSurveyResponseDocument submitted = Submitted();

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, submitted.Responses);

        Assert.Equal(0, result.UnreadableAnswerCount);
        Assert.Equal(1, result.ResponseCount);
        Assert.Equal(16, result.Questions.Count);
        Assert.Equal(16, result.Questions.Count(question => question.Answered == 1));
    }

    [Fact]
    public void AWRITEINTravelsWITHItsTickRatherThanInsteadOfIt()
    {
        // The design decision that keeps four questions inside the statistics: «Бусад» is
        // an option that also takes a line, so the tick still counts AND the words are
        // kept. If the page had sent the words instead of the tick, that question's base
        // would be one short and the option would read as unchosen.
        ProjectCitizenSurvey survey = SurveyFromDefinition();
        CitizenSurveyResponseDocument submitted = Submitted();

        CitizenSurveyAnswer written = submitted.Responses[0].Answers
            .Single(answer => answer.Text == "Өөр баг");

        Assert.NotEmpty(written.OptionIds);

        CitizenSurveyQuestion question = survey.Questions
            .Single(candidate => candidate.Id == written.QuestionId);
        CitizenSurveyOption ticked = question.Options
            .Single(option => option.Id == written.OptionIds[0]);

        Assert.True(ticked.InvitesOwnWords, "the words were attached to an option with no line");

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, submitted.Responses);
        CitizenSurveyQuestionTally counted = result.Questions
            .Single(tally => tally.QuestionId == question.Id);

        Assert.Equal(1, counted.Answered);
        Assert.Contains(counted.OwnWords, word => word == "Өөр баг");
    }

    [Fact]
    public void THENUMERICAnswerArrivesAsANumberNotAsText()
    {
        // A number box rendered as a text field would still submit - as a sentence. The
        // average, the range and the histogram would all quietly disappear.
        ProjectCitizenSurvey survey = SurveyFromDefinition();
        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, Submitted().Responses);

        CitizenSurveyQuestionTally numeric = result.Questions
            .Single(question => question.Kind == CitizenSurveyQuestionKinds.Number);

        Assert.Equal(5, numeric.NumberAverage);
        Assert.Equal(5, numeric.NumberLargest);
    }

    [Fact]
    public void THEMULTIPLEChoiceAnswerKeepsBOTHTicks()
    {
        ProjectCitizenSurvey survey = SurveyFromDefinition();
        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, Submitted().Responses);

        CitizenSurveyQuestionTally many = result.Questions
            .Single(question => question.Kind == CitizenSurveyQuestionKinds.MultipleChoice);

        Assert.Equal(2, many.Options.Count(option => option.Count == 1));

        // ⚠ One person, two options: the shares add past a whole and each is still 100%
        // of those who answered. That is correct here and would be a defect anywhere else.
        Assert.Equal(1, many.Answered);
    }

    [Fact]
    public void THEWRITTENBlockArrivesAsSentencesWithNoOptions()
    {
        ProjectCitizenSurvey survey = SurveyFromDefinition();
        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, Submitted().Responses);

        IReadOnlyList<CitizenSurveyQuestionTally> written = result.Questions
            .Where(question => question.Kind == CitizenSurveyQuestionKinds.FreeText)
            .ToList();

        Assert.Equal(2, written.Count);
        Assert.All(written, question => Assert.Single(question.OwnWords));
    }

    [Fact]
    public void ITMERGESAndASecondReadOfTheSameFileAddsNOBODY()
    {
        // The whole point of the response id, on the path it was captured from.
        var held = new CitizenSurveyResponseDocument { SurveyId = Submitted().SurveyId };

        Assert.Equal(1, CitizenSurveyResponseStore.Merge(held, Submitted().Responses, null));
        Assert.Equal(0, CitizenSurveyResponseStore.Merge(held, Submitted().Responses, null));
        Assert.Single(held.Responses);
    }
}
