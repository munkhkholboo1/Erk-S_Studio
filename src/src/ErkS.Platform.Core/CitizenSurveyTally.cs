namespace ErkS.Platform.Core;

/// <summary>How many people chose one option, and what share of those who answered that is.</summary>
/// <param name="Share">
/// Of the people who answered THIS question, not of everyone who submitted a form.
/// </param>
public readonly record struct CitizenSurveyOptionTally(
    string OptionId,
    string Text,
    int Count,
    double Share);

/// <summary>What the answers to one question came to.</summary>
/// <param name="Answered">People who answered this question.</param>
/// <param name="Skipped">People who submitted a form and left this one blank.</param>
/// <param name="OwnWords">
/// What people wrote - a free-text answer, or the line beside «Бусад». Every one of them,
/// in the order received.
/// </param>
public sealed record CitizenSurveyQuestionTally(
    string QuestionId,
    string Text,
    string Kind,
    int Answered,
    int Skipped,
    IReadOnlyList<CitizenSurveyOptionTally> Options,
    IReadOnlyList<string> OwnWords,
    double? NumberAverage,
    double? NumberSmallest,
    double? NumberLargest);

/// <summary>What a whole survey came to.</summary>
/// <param name="UnreadableAnswerCount">
/// Answers naming a question or an option this survey no longer has.
///
/// 🔴 COUNTED RATHER THAN DROPPED. Editing a survey after answers arrive is ordinary - a
/// question gets reworded, an option is removed - and every such answer would otherwise
/// vanish from the result with nothing said. A citizens' survey that quietly loses
/// answers is the one kind of defect that cannot be repaired later, because the person
/// has gone home. If this is not zero, the tally is incomplete and says so.
/// </param>
public sealed record CitizenSurveyResult(
    int ResponseCount,
    DateTimeOffset? FirstSubmittedAtUtc,
    DateTimeOffset? LastSubmittedAtUtc,
    IReadOnlyList<CitizenSurveyQuestionTally> Questions,
    int UnreadableAnswerCount);

/// <summary>
/// Counts a citizens' survey.
///
/// 🔴 THE SHARE IS OF THOSE WHO ANSWERED THE QUESTION, NOT OF EVERYONE. Someone who skipped
/// question nine must not make the answers to question nine look weaker - dividing by the
/// number of forms would do exactly that, and the further down the form a question sits the
/// more it would be understated. The skipped count is reported separately so the reader can
/// see how many stayed silent instead of having it folded invisibly into the percentages.
///
/// 🔴 AND A MULTIPLE-CHOICE QUESTION'S SHARES ADD PAST 100%, WHICH IS CORRECT. Each share
/// answers «of the people who answered, how many ticked this», and one person ticks
/// several. Normalising them to a whole would invent a pie chart out of a question nobody
/// asked - so the kind travels with the tally and the reader is told which it is.
///
/// ⚠ AN OPTION NOBODY CHOSE IS STILL REPORTED, at zero. Dropping it would make a result
/// read as though the option had never been offered, which is a different finding
/// entirely - and in a planning consultation «nobody wanted this» is often the point.
/// </summary>
public static class CitizenSurveyTally
{
    public static CitizenSurveyResult Of(
        ProjectCitizenSurvey? survey,
        IEnumerable<CitizenSurveyResponse>? responses)
    {
        IReadOnlyList<CitizenSurveyQuestion> questions =
            survey?.OrderedQuestions() ?? [];
        List<CitizenSurveyResponse> forms = (responses ?? [])
            .Where(response => response is not null)
            .ToList();

        var unreadable = 0;
        var byQuestion = new Dictionary<string, List<CitizenSurveyAnswer>>(StringComparer.Ordinal);
        foreach (CitizenSurveyQuestion question in questions)
            byQuestion[question.Id] = [];

        foreach (CitizenSurveyResponse form in forms)
        {
            foreach (CitizenSurveyAnswer answer in form.Answers ?? [])
            {
                if (answer is null)
                    continue;
                if (!byQuestion.TryGetValue(answer.QuestionId ?? "", out List<CitizenSurveyAnswer>? bucket))
                {
                    // A question that has since been removed or renamed.
                    unreadable++;
                    continue;
                }

                bucket.Add(answer);
            }
        }

        var tallies = new List<CitizenSurveyQuestionTally>(questions.Count);
        foreach (CitizenSurveyQuestion question in questions)
        {
            List<CitizenSurveyAnswer> answers = byQuestion[question.Id];
            var known = new HashSet<string>(
                question.Options.Select(option => option.Id),
                StringComparer.Ordinal);

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var words = new List<string>();
            var numbers = new List<double>();
            var answered = 0;

            foreach (CitizenSurveyAnswer answer in answers)
            {
                var said = false;

                foreach (string optionId in answer.OptionIds ?? [])
                {
                    if (!known.Contains(optionId ?? ""))
                    {
                        // An option this survey no longer offers.
                        unreadable++;
                        continue;
                    }

                    counts[optionId!] = counts.GetValueOrDefault(optionId!) + 1;
                    said = true;
                }

                string written = (answer.Text ?? "").Trim();
                if (written.Length > 0)
                {
                    words.Add(written);
                    said = true;
                }

                if (answer.Number is { } number && double.IsFinite(number))
                {
                    numbers.Add(number);
                    said = true;
                }

                if (said)
                    answered++;
            }

            var options = question.Options
                .Select(option =>
                {
                    int count = counts.GetValueOrDefault(option.Id);
                    return new CitizenSurveyOptionTally(
                        option.Id,
                        option.Text,
                        count,
                        // ⚠ Guarded rather than trusted: a question nobody answered has a
                        // denominator of zero, and a result page that crashed on an
                        // untouched question would take the whole report with it.
                        answered == 0 ? 0d : (double)count / answered);
                })
                .ToList();

            tallies.Add(new CitizenSurveyQuestionTally(
                question.Id,
                question.Text,
                question.Kind,
                answered,
                Math.Max(0, forms.Count - answered),
                options,
                words,
                numbers.Count == 0 ? null : numbers.Average(),
                numbers.Count == 0 ? null : numbers.Min(),
                numbers.Count == 0 ? null : numbers.Max()));
        }

        return new CitizenSurveyResult(
            forms.Count,
            forms.Count == 0 ? null : forms.Min(form => form.SubmittedAtUtc),
            forms.Count == 0 ? null : forms.Max(form => form.SubmittedAtUtc),
            tallies,
            unreadable);
    }
}
