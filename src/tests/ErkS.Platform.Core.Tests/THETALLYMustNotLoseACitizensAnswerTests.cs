using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Counting the survey, without losing anybody.
///
/// 🔴 THIS IS THE ONE DEFECT THAT CANNOT BE REPAIRED LATER. A drawing can be re-exported
/// and an album rebuilt; a citizen who answered a questionnaire has gone home. So every
/// way an answer could vanish quietly is held here: an option nobody picked still appears,
/// a question somebody skipped does not dilute the others, and an answer naming something
/// the survey no longer has is COUNTED rather than dropped.
/// </summary>
public sealed class THETALLYMustNotLoseACitizensAnswerTests
{
    [Fact]
    public void THESHAREIsOfThoseWhoANSWEREDNotOfEveryoneWhoSubmitted()
    {
        // 🔴 THE CLASSIC ERROR, AND IT GETS WORSE FURTHER DOWN THE PAGE. Dividing by the
        // number of FORMS makes every later question look weaker than it is, because more
        // people have given up by then. Three people answered, two of them picked the
        // first option: that is 67% of the people who answered it, and a fourth person who
        // skipped the question must not turn it into 50%.
        ProjectCitizenSurvey survey = OneQuestion(CitizenSurveyQuestionKinds.SingleChoice);
        string a = survey.Questions[0].Options[0].Id;
        string b = survey.Questions[0].Options[1].Id;
        string questionId = survey.Questions[0].Id;

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, [
            Answered(questionId, a),
            Answered(questionId, a),
            Answered(questionId, b),
            Blank(),
        ]);

        CitizenSurveyQuestionTally tally = Assert.Single(result.Questions);
        Assert.Equal(4, result.ResponseCount);
        Assert.Equal(3, tally.Answered);
        Assert.Equal(1, tally.Skipped);
        Assert.Equal(2d / 3d, tally.Options.Single(o => o.OptionId == a).Share, 6);
        Assert.Equal(1d / 3d, tally.Options.Single(o => o.OptionId == b).Share, 6);
    }

    [Fact]
    public void AMULTIPLEChoiceQuestionMayAddPastAWholeAndThatIsCORRECT()
    {
        // 🔴 AND IT MUST NOT BE NORMALISED. Each share answers «of those who answered, how
        // many ticked this», and one person ticks several - so the column adds past 100%.
        // Scaling it to a whole would turn a «which of these do you want» question into a
        // pie chart nobody asked for, in a document a council reads.
        ProjectCitizenSurvey survey = OneQuestion(CitizenSurveyQuestionKinds.MultipleChoice);
        string a = survey.Questions[0].Options[0].Id;
        string b = survey.Questions[0].Options[1].Id;
        string questionId = survey.Questions[0].Id;

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, [
            Answered(questionId, a, b),
            Answered(questionId, a),
        ]);

        CitizenSurveyQuestionTally tally = Assert.Single(result.Questions);
        Assert.Equal(2, tally.Answered);
        Assert.Equal(1d, tally.Options.Single(o => o.OptionId == a).Share, 6);
        Assert.Equal(0.5d, tally.Options.Single(o => o.OptionId == b).Share, 6);
        Assert.True(tally.Options.Sum(option => option.Share) > 1d);

        // The kind travels with the tally, so whoever draws it knows not to draw a pie.
        Assert.Equal(CitizenSurveyQuestionKinds.MultipleChoice, tally.Kind);
    }

    [Fact]
    public void ANOPTIONNobodyChoseIsSTILLReported()
    {
        // ⚠ «Nobody wanted this» is often the finding. Dropping a zero would make the
        // result read as though the option had never been offered - a different statement
        // entirely, and one the owner cannot correct because the forms are gone.
        ProjectCitizenSurvey survey = OneQuestion(CitizenSurveyQuestionKinds.SingleChoice);
        string questionId = survey.Questions[0].Id;

        CitizenSurveyResult result = CitizenSurveyTally.Of(
            survey, [Answered(questionId, survey.Questions[0].Options[0].Id)]);

        CitizenSurveyQuestionTally tally = Assert.Single(result.Questions);
        Assert.Equal(2, tally.Options.Count);
        Assert.Equal(0, tally.Options.Single(o => o.OptionId == survey.Questions[0].Options[1].Id).Count);
    }

    [Fact]
    public void ANANSWERNamingSomethingTheSurveyNoLongerHasIsCOUNTEDNotDropped()
    {
        // 🔴 EDITING A SURVEY AFTER ANSWERS ARRIVE IS ORDINARY - a question gets reworded,
        // an option removed. Every such answer would otherwise disappear from the result
        // with nothing said, and the total would look complete. The count is the honest
        // statement: this result is missing something.
        ProjectCitizenSurvey survey = OneQuestion(CitizenSurveyQuestionKinds.SingleChoice);
        string questionId = survey.Questions[0].Id;

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, [
            Answered(questionId, survey.Questions[0].Options[0].Id),
            Answered(questionId, "an-option-that-was-deleted"),
            Answered("a-question-that-was-deleted", "whatever"),
        ]);

        Assert.Equal(2, result.UnreadableAnswerCount);

        // And the readable one is still counted properly rather than the whole tally
        // being abandoned.
        Assert.Equal(1, Assert.Single(result.Questions).Answered);
    }

    [Fact]
    public void THEWRITTENWordsBesideBusadAreKEPT()
    {
        // The written line is where somebody says the thing the form did not think to
        // ask. A tally that counted the tick and threw away the sentence would report
        // «Бусад: 2» and destroy the only open door in the questionnaire.
        ProjectCitizenSurvey survey = OneQuestion(CitizenSurveyQuestionKinds.SingleChoice);
        string questionId = survey.Questions[0].Id;
        string other = survey.Questions[0].Options[1].Id;

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, [
            new CitizenSurveyResponse
            {
                Answers = [new CitizenSurveyAnswer
                {
                    QuestionId = questionId,
                    OptionIds = [other],
                    Text = "Цэвэр усны шугам",
                }],
            },
        ]);

        CitizenSurveyQuestionTally tally = Assert.Single(result.Questions);
        Assert.Equal(1, tally.Options.Single(o => o.OptionId == other).Count);
        Assert.Equal(["Цэвэр усны шугам"], tally.OwnWords);
    }

    [Fact]
    public void AWRITTENNumberIsSummarisedWithItsRANGENotJustItsAverage()
    {
        // ⚠ AN AVERAGE ALONE HIDES THE CASE THAT MATTERS. «Ам бүлийн тоо» averaging 4 says
        // nothing about the household of 11 that the plan has to house - and in a planning
        // survey the tail is the finding. The smallest and largest travel with the mean.
        var survey = new ProjectCitizenSurvey
        {
            Questions = [new CitizenSurveyQuestion
            {
                Order = 1,
                Text = "Ам бүлийн тоо:",
                Kind = CitizenSurveyQuestionKinds.Number,
                Unit = "хүн",
            }],
        };
        string questionId = survey.Questions[0].Id;

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, [
            Numbered(questionId, 2),
            Numbered(questionId, 3),
            Numbered(questionId, 11),
        ]);

        CitizenSurveyQuestionTally tally = Assert.Single(result.Questions);
        Assert.Equal(3, tally.Answered);
        Assert.Equal(16d / 3d, tally.NumberAverage!.Value, 6);
        Assert.Equal(2d, tally.NumberSmallest!.Value, 6);
        Assert.Equal(11d, tally.NumberLargest!.Value, 6);
    }

    [Fact]
    public void ASURVEYNobodyAnsweredYetCountsToZeroWithoutDividingByIt()
    {
        // The state every survey starts in, and a division by zero here would take down
        // the results page on the day the QR code is first printed.
        ProjectCitizenSurvey survey = OneQuestion(CitizenSurveyQuestionKinds.SingleChoice);

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, []);

        Assert.Equal(0, result.ResponseCount);
        Assert.Null(result.FirstSubmittedAtUtc);
        CitizenSurveyQuestionTally tally = Assert.Single(result.Questions);
        Assert.Equal(0, tally.Answered);
        Assert.All(tally.Options, option => Assert.Equal(0d, option.Share));
    }

    [Fact]
    public void NOSurveyAtAllIsNotACrash()
    {
        CitizenSurveyResult result = CitizenSurveyTally.Of(null, null);

        Assert.Equal(0, result.ResponseCount);
        Assert.Empty(result.Questions);
        Assert.Equal(0, result.UnreadableAnswerCount);
    }

    private static ProjectCitizenSurvey OneQuestion(string kind) => new()
    {
        Questions = [new CitizenSurveyQuestion
        {
            Order = 1,
            Text = "Туршилтын асуулт",
            Kind = kind,
            Options =
            [
                new CitizenSurveyOption { Text = "Нэг" },
                new CitizenSurveyOption { Text = "Бусад", InvitesOwnWords = true },
            ],
        }],
    };

    private static CitizenSurveyResponse Answered(string questionId, params string[] optionIds) =>
        new()
        {
            Answers = [new CitizenSurveyAnswer
            {
                QuestionId = questionId,
                OptionIds = optionIds.ToList(),
            }],
        };

    private static CitizenSurveyResponse Numbered(string questionId, double number) =>
        new()
        {
            Answers = [new CitizenSurveyAnswer { QuestionId = questionId, Number = number }],
        };

    private static CitizenSurveyResponse Blank() => new();
}
