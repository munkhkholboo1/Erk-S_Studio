namespace ErkS.Platform.Core;

/// <summary>One slice of the public, and how that slice answered.</summary>
/// <param name="Respondents">People in this slice who submitted a form.</param>
public sealed record CitizenSurveySegmentTally(
    string SegmentOptionId,
    string SegmentLabel,
    int Respondents,
    CitizenSurveyQuestionTally Tally);

/// <summary>One question's answers, split by another question's answers.</summary>
/// <param name="UnsegmentedRespondents">
/// People who answered the question but not the one it is split by.
///
/// 🔴 REPORTED, BECAUSE THEY ANSWERED. Splitting by age drops everyone who left age
/// blank, and a table whose columns quietly add to less than the total invites the reader
/// to trust a subtraction that is wrong. They are counted here instead.
/// </param>
public sealed record CitizenSurveyCrossTabResult(
    string ByQuestionId,
    string ByQuestionText,
    string OfQuestionId,
    string OfQuestionText,
    IReadOnlyList<CitizenSurveySegmentTally> Segments,
    int UnsegmentedRespondents);

/// <summary>
/// Splits one question's answers by another's.
///
/// 🔴 THIS IS WHERE A PLANNING SURVEY EARNS ITS KEEP, and the owner asked for it:
/// «асуулгын төрлүүдээр бас ангилж үр дүнг харуулна. гол санаа нь санал асуулгаас гаргаж
/// болох бүх үр дүнг гаргадаг байна.» A whole-population number says «38% would move into
/// flats»; the split says which age, which баг, and which kind of housing they live in
/// now - and those are the sentences a plan is actually written from.
///
/// ⚠ WHICH QUESTION MAY SPLIT THE OTHERS IS THE OWNER'S CHOICE, NOT A GUESS FROM THE
/// WORDING. Any single-choice question can serve, because deciding by hand that «нас» and
/// «хүйс» are demographic and «сууц» is not would be reading meaning out of shape - and
/// the second town's form will word them differently. Studio offers the list; the person
/// picks.
///
/// 🔴 AND IT COUNTS THROUGH <see cref="CitizenSurveyTally"/> RATHER THAN COUNTING AGAIN.
/// Every rule that matters - the share being of those who answered, an unchosen option
/// still appearing, an answer to a removed option being counted as unreadable - is
/// already decided and tested there. A second counter written here would drift from it,
/// and the drift would show up as two different numbers for the same question on one
/// screen.
/// </summary>
public static class CitizenSurveyCrossTab
{
    /// <summary>
    /// The questions that can split the others: single choice, with options.
    ///
    /// Multiple choice is excluded - a person who ticked three options belongs to three
    /// slices at once, so the columns would double-count people and add past the total.
    /// </summary>
    public static IReadOnlyList<CitizenSurveyQuestion> SplittingQuestions(
        ProjectCitizenSurvey? survey) =>
        (survey?.OrderedQuestions() ?? [])
            .Where(question =>
                question.Kind == CitizenSurveyQuestionKinds.SingleChoice &&
                question.Options.Count > 0)
            .ToList();

    public static CitizenSurveyCrossTabResult? Of(
        ProjectCitizenSurvey? survey,
        IEnumerable<CitizenSurveyResponse>? responses,
        string? byQuestionId,
        string? ofQuestionId)
    {
        if (survey is null)
            return null;

        CitizenSurveyQuestion? by = survey.Questions
            .FirstOrDefault(question => question.Id.Equals(byQuestionId, StringComparison.Ordinal));
        CitizenSurveyQuestion? of = survey.Questions
            .FirstOrDefault(question => question.Id.Equals(ofQuestionId, StringComparison.Ordinal));
        if (by is null || of is null || by.Id.Equals(of.Id, StringComparison.Ordinal))
            return null;
        if (by.Kind != CitizenSurveyQuestionKinds.SingleChoice)
            return null;

        List<CitizenSurveyResponse> forms = (responses ?? [])
            .Where(response => response is not null)
            .ToList();

        // The survey handed to the tally holds ONLY the question being split, so every
        // share it reports is of that question alone.
        var justTheQuestion = new ProjectCitizenSurvey { Questions = [of] };

        var segments = new List<CitizenSurveySegmentTally>(by.Options.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);

        foreach (CitizenSurveyOption option in by.Options)
        {
            List<CitizenSurveyResponse> slice = forms
                .Where(form => ChoseOption(form, by.Id, option.Id))
                .ToList();
            foreach (CitizenSurveyResponse form in slice)
                placed.Add(form.Id);

            CitizenSurveyResult counted = CitizenSurveyTally.Of(justTheQuestion, slice);
            segments.Add(new CitizenSurveySegmentTally(
                option.Id,
                option.Text,
                slice.Count,
                counted.Questions[0]));
        }

        // Anybody who answered the split question but not the splitting one.
        int unsegmented = forms.Count(form =>
            !placed.Contains(form.Id) && Answered(form, of.Id));

        return new CitizenSurveyCrossTabResult(
            by.Id,
            by.Text,
            of.Id,
            of.Text,
            segments,
            unsegmented);
    }

    private static bool ChoseOption(CitizenSurveyResponse form, string questionId, string optionId) =>
        (form.Answers ?? []).Any(answer =>
            answer is not null &&
            answer.QuestionId.Equals(questionId, StringComparison.Ordinal) &&
            (answer.OptionIds ?? []).Contains(optionId, StringComparer.Ordinal));

    private static bool Answered(CitizenSurveyResponse form, string questionId) =>
        (form.Answers ?? []).Any(answer =>
            answer is not null &&
            answer.QuestionId.Equals(questionId, StringComparison.Ordinal) &&
            ((answer.OptionIds ?? []).Count > 0 ||
             !string.IsNullOrWhiteSpace(answer.Text) ||
             answer.Number is not null));
}
