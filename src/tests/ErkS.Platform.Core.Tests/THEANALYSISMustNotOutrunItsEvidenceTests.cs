using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The reading of a survey, and the line it must not cross.
///
/// 🔴 THIS TEXT GOES INTO A PLANNING DOCUMENT. A council reads it and citizens are bound
/// by what follows from it, so a confident sentence drawn from nine answers is worse than
/// no analysis at all - it carries the authority of a number without the weight of one.
/// Every finding therefore names its base, a thin question says so instead of speaking,
/// and nothing claims a cause.
/// </summary>
public sealed class THEANALYSISMustNotOutrunItsEvidenceTests
{
    [Fact]
    public void ATHINQuestionSAYSSoInsteadOfSpeaking()
    {
        // 🔴 SILENCE WOULD READ AS AGREEMENT. A question skipped by the analysis looks, on
        // the page, exactly like a question with nothing remarkable in it - so a thin base
        // produces a finding of its own rather than nothing.
        CitizenSurveyResult result = Counted(("Тийм", 5), ("Үгүй", 3));

        CitizenSurveyFinding finding = Assert.Single(CitizenSurveyFindings.Read(result));

        Assert.Equal(CitizenSurveyFindingWeights.TooFew, finding.Weight);
        Assert.Contains("8", finding.Evidence, StringComparison.Ordinal);
        Assert.Contains(
            CitizenSurveyFindings.MinimumBase.ToString(), finding.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYFindingCarriesTheCountAndTheShareItWasReadFrom()
    {
        // A percentage with no base is an opinion wearing a number. 80/100 and 4/5 are the
        // same share and mean entirely different things to a plan.
        CitizenSurveyResult result = Counted(("Тийм", 80), ("Үгүй", 20));

        CitizenSurveyFinding strong = CitizenSurveyFindings.Read(result)
            .First(finding => finding.Weight == CitizenSurveyFindingWeights.Strong);

        Assert.Contains("80", strong.Evidence, StringComparison.Ordinal);
        Assert.Contains("100", strong.Evidence, StringComparison.Ordinal);
        Assert.Contains("%", strong.Evidence, StringComparison.Ordinal);
        Assert.Contains("Тийм", strong.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void ADIVIDEDAnswerIsNotReportedAsAMajority()
    {
        // 🔴 THE FAILURE THAT WOULD DO REAL DAMAGE. 52 against 48 is not «citizens want
        // this», and a plan justified by that sentence would be justified by nothing.
        CitizenSurveyResult result = Counted(("Тийм", 52), ("Үгүй", 48));

        IReadOnlyList<CitizenSurveyFinding> findings = CitizenSurveyFindings.Read(result);

        Assert.DoesNotContain(findings, finding => finding.Weight == CitizenSurveyFindingWeights.Strong);
        CitizenSurveyFinding divided = Assert.Single(findings);
        Assert.Equal("Санал хуваагдсан", divided.Headline);
        Assert.Contains("олонхийн санал гэж үзэж болохгүй", divided.Consideration, StringComparison.Ordinal);
    }

    [Fact]
    public void ANOPTIONNobodyChoseIsItsOwnFinding()
    {
        // ⚠ THE ONE A «TOP ANSWERS» SUMMARY ALWAYS LOSES: a refused option never appears in
        // a list of winners, so the fact that something offered was wanted by nobody would
        // silently leave the report.
        CitizenSurveyResult result = Counted(("Тийм", 70), ("Үгүй", 30), ("Мэдэхгүй", 0));

        CitizenSurveyFinding refused = CitizenSurveyFindings.Read(result)
            .Single(finding => finding.Headline.Contains("сонгоогүй", StringComparison.Ordinal));

        Assert.Contains("Мэдэхгүй", refused.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void ALARGEShareOfDontKnowIsReportedAsAnInformationGap()
    {
        CitizenSurveyResult result = Counted(("Сайн", 40), ("Тодорхой мэдээлэлгүй", 60));

        CitizenSurveyFinding gap = CitizenSurveyFindings.Read(result)
            .Single(finding => finding.Headline.Contains("Мэдээлэл дутмаг", StringComparison.Ordinal));

        Assert.Contains("60", gap.Evidence, StringComparison.Ordinal);
        Assert.Equal(CitizenSurveyFindingWeights.Attention, gap.Weight);
    }

    [Fact]
    public void THETAILOfAHouseholdSizeIsNamedBecauseTheAverageHidesIt()
    {
        // 🔴 AN AVERAGE HOUSEHOLD OF FOUR SAYS NOTHING ABOUT THE FAMILY OF TWELVE the plan
        // still has to house - and the average is precisely the statistic that hides them.
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

        var forms = new List<CitizenSurveyResponse>();
        for (var i = 0; i < 39; i++)
            forms.Add(Numbered(questionId, 3));
        forms.Add(Numbered(questionId, 12));

        IReadOnlyList<CitizenSurveyFinding> findings =
            CitizenSurveyFindings.Read(CitizenSurveyTally.Of(survey, forms));

        CitizenSurveyFinding tail = findings
            .Single(finding => finding.Headline.Contains("том утга", StringComparison.Ordinal));
        Assert.Contains("12", tail.Evidence, StringComparison.Ordinal);
        Assert.Equal(CitizenSurveyFindingWeights.Attention, tail.Weight);
    }

    [Fact]
    public void WRITTENAnswersAreCountedAndNOTSummarised()
    {
        // ⚠ DELIBERATE. Free sentences are where somebody says the thing the form did not
        // think to ask; a machine-made précis of them would put words in their mouth in the
        // one document that is supposed to carry theirs.
        var survey = new ProjectCitizenSurvey
        {
            Questions = [new CitizenSurveyQuestion
            {
                Order = 1,
                Text = "Таны санал:",
                Kind = CitizenSurveyQuestionKinds.FreeText,
            }],
        };
        string questionId = survey.Questions[0].Id;

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, [
            Written(questionId, "Цэцэрлэг дутагдалтай"),
            Written(questionId, "Ус, дулааны шугам хуучирсан"),
        ]);

        CitizenSurveyFinding finding = Assert.Single(CitizenSurveyFindings.Read(result));
        Assert.Contains("2", finding.Headline, StringComparison.Ordinal);
        Assert.Contains("бүтнээр нь уншиж", finding.Consideration, StringComparison.Ordinal);

        // The sentences themselves are carried, not replaced by the count.
        Assert.Equal(2, result.Questions[0].OwnWords.Count);
    }

    [Fact]
    public void ACROSSTabSplitsOneQuestionByAnotherAndKeepsWhoItCouldNotPlace()
    {
        // 🔴 THE SENTENCE A PLAN IS WRITTEN FROM. «38% would move into flats» is the whole
        // population; which AGE said so is the finding. And the people who left the
        // splitting question blank are counted rather than dropped - columns that add to
        // less than the total invite a subtraction that is wrong.
        var age = new CitizenSurveyQuestion
        {
            Order = 1,
            Text = "Таны нас:",
            Kind = CitizenSurveyQuestionKinds.SingleChoice,
            Options = [new CitizenSurveyOption { Text = "18–24" }, new CitizenSurveyOption { Text = "60-аас дээш" }],
        };
        var move = new CitizenSurveyQuestion
        {
            Order = 2,
            Text = "Орон сууцанд амьдрах уу?",
            Kind = CitizenSurveyQuestionKinds.SingleChoice,
            Options = [new CitizenSurveyOption { Text = "Тийм" }, new CitizenSurveyOption { Text = "Үгүй" }],
        };
        var survey = new ProjectCitizenSurvey { Questions = [age, move] };

        CitizenSurveyCrossTabResult tab = CitizenSurveyCrossTab.Of(survey, [
            Both(age.Id, age.Options[0].Id, move.Id, move.Options[0].Id),
            Both(age.Id, age.Options[0].Id, move.Id, move.Options[0].Id),
            Both(age.Id, age.Options[1].Id, move.Id, move.Options[1].Id),
            // Answered the question but not the one it is split by.
            Chose(move.Id, move.Options[0].Id),
        ], age.Id, move.Id)!;

        Assert.Equal(2, tab.Segments.Count);
        CitizenSurveySegmentTally young = tab.Segments.Single(s => s.SegmentLabel == "18–24");
        CitizenSurveySegmentTally old = tab.Segments.Single(s => s.SegmentLabel == "60-аас дээш");

        Assert.Equal(2, young.Respondents);
        Assert.Equal(1d, young.Tally.Options.Single(o => o.Text == "Тийм").Share, 6);
        Assert.Equal(1d, old.Tally.Options.Single(o => o.Text == "Үгүй").Share, 6);
        Assert.Equal(1, tab.UnsegmentedRespondents);
    }

    [Fact]
    public void AMULTIPLEChoiceQuestionMayNOTSplitTheOthers()
    {
        // ⚠ ONE PERSON WHO TICKED THREE OPTIONS BELONGS TO THREE SLICES, so the columns
        // would count them three times and add past the number of people who answered.
        var many = new CitizenSurveyQuestion
        {
            Order = 1,
            Text = "Аль нь шаардлагатай вэ?",
            Kind = CitizenSurveyQuestionKinds.MultipleChoice,
            Options = [new CitizenSurveyOption { Text = "Сургууль" }, new CitizenSurveyOption { Text = "Эмнэлэг" }],
        };
        var single = new CitizenSurveyQuestion
        {
            Order = 2,
            Text = "Таны хүйс:",
            Kind = CitizenSurveyQuestionKinds.SingleChoice,
            Options = [new CitizenSurveyOption { Text = "Эрэгтэй" }],
        };
        var survey = new ProjectCitizenSurvey { Questions = [many, single] };

        Assert.Null(CitizenSurveyCrossTab.Of(survey, [], many.Id, single.Id));
        Assert.DoesNotContain(
            CitizenSurveyCrossTab.SplittingQuestions(survey),
            question => question.Id == many.Id);
        Assert.Contains(
            CitizenSurveyCrossTab.SplittingQuestions(survey),
            question => question.Id == single.Id);
    }

    [Fact]
    public void ABARNeverOutrunsItsTrackAndTheValueMovesOutWhenItWillNotFit()
    {
        // The two mistakes a chart makes that a screenshot of plausible data hides:
        // overflowing the track, and clipping the value it was supposed to show.
        CitizenSurveyResult result = Counted(("Их", 90), ("Бага", 10));
        CitizenSurveyQuestionTally tally = result.Questions[0];

        IReadOnlyList<CitizenSurveyBarMark> bars =
            CitizenSurveyChartLayout.Bars(tally, 400, _ => 40);

        Assert.All(bars, bar => Assert.InRange(bar.LengthPx, 0, 400));
        Assert.False(bars.Single(b => b.Label == "Их").LabelOutside);
        Assert.True(bars.Single(b => b.Label == "Бага").LabelOutside);

        // Options keep the order the citizen read them in, not size order.
        Assert.Equal(["Их", "Бага"], bars.Select(bar => bar.Label));
    }

    [Fact]
    public void THEBarThicknessIsCappedSoTheRowKeepsItsAir()
    {
        Assert.Equal(
            CitizenSurveyChartLayout.MaxBarThicknessPx,
            CitizenSurveyChartLayout.BarThickness(200));
        Assert.Equal(18, CitizenSurveyChartLayout.BarThickness(20));
        Assert.Equal(0, CitizenSurveyChartLayout.BarThickness(0));
    }

    [Fact]
    public void AWRITTENNumberGetsAWholeBucketPerPersonCounted()
    {
        Assert.Equal(
            [(2, 1), (3, 2), (11, 1)],
            CitizenSurveyChartLayout.NumberBuckets([2, 3, 3, 11]));
    }

    private static CitizenSurveyResult Counted(params (string Text, int Count)[] options)
    {
        var question = new CitizenSurveyQuestion
        {
            Order = 1,
            Text = "Туршилтын асуулт",
            Kind = CitizenSurveyQuestionKinds.SingleChoice,
            Options = options
                .Select(option => new CitizenSurveyOption { Text = option.Text })
                .ToList(),
        };
        var survey = new ProjectCitizenSurvey { Questions = [question] };

        var forms = new List<CitizenSurveyResponse>();
        for (var index = 0; index < options.Length; index++)
        {
            for (var n = 0; n < options[index].Count; n++)
                forms.Add(Chose(question.Id, question.Options[index].Id));
        }

        return CitizenSurveyTally.Of(survey, forms);
    }

    private static CitizenSurveyResponse Chose(string questionId, string optionId) => new()
    {
        Answers = [new CitizenSurveyAnswer { QuestionId = questionId, OptionIds = [optionId] }],
    };

    private static CitizenSurveyResponse Both(
        string firstQuestion, string firstOption, string secondQuestion, string secondOption) => new()
    {
        Answers =
        [
            new CitizenSurveyAnswer { QuestionId = firstQuestion, OptionIds = [firstOption] },
            new CitizenSurveyAnswer { QuestionId = secondQuestion, OptionIds = [secondOption] },
        ],
    };

    private static CitizenSurveyResponse Numbered(string questionId, double number) => new()
    {
        Answers = [new CitizenSurveyAnswer { QuestionId = questionId, Number = number }],
    };

    private static CitizenSurveyResponse Written(string questionId, string text) => new()
    {
        Answers = [new CitizenSurveyAnswer { QuestionId = questionId, Text = text }],
    };
}
