namespace ErkS.Platform.Core;

/// <summary>How much weight a finding carries. Never a judgement, always a measurement.</summary>
public static class CitizenSurveyFindingWeights
{
    /// <summary>Too few answers to say anything. Reported, never hidden.</summary>
    public const string TooFew = "TooFew";

    /// <summary>Worth writing down.</summary>
    public const string Note = "Note";

    /// <summary>Worth acting on in the plan.</summary>
    public const string Attention = "Attention";

    /// <summary>A clear majority said the same thing.</summary>
    public const string Strong = "Strong";
}

/// <summary>One thing the answers say, with the numbers it was read from.</summary>
/// <param name="Evidence">
/// The count and share it rests on. Never omitted - a finding without its base is an
/// opinion wearing a percentage.
/// </param>
/// <param name="Consideration">What it means for the plan, in the plainest terms.</param>
public sealed record CitizenSurveyFinding(
    string QuestionId,
    string QuestionText,
    string Headline,
    string Evidence,
    string Consideration,
    string Weight);

/// <summary>
/// Reads the answers and says what they show.
///
/// 🔴 THE OWNER ASKED FOR A READING, NOT A GUESS: «асуултууд дээр тулгуурлаж бодит үр дүнг
/// тодорхойлдог болгоорой. асуулгын үр дүнд ийм хариу гарсан тул ийм ийм анхаарах
/// шаардлагатай зүйл байна … сонжоо шиг л гэсэн үг л дээ.»
///
/// 🔴 AND THE DANGER IS INVENTING ONE. This text goes into a planning document that a
/// council reads and citizens are bound by. A sentence like «иргэд эсрэг байна» drawn from
/// nine answers would be worse than no analysis at all, because it carries the authority
/// of a number without the weight of one. So three rules hold everywhere here:
///
///   every finding names the count AND the share it was read from;
///   below <see cref="MinimumBase"/> answers a question yields ONE finding that says so,
///     rather than a confident sentence or a silence that looks like agreement;
///   nothing is asserted about CAUSE. The survey can say what people chose, never why -
///     so the considerations say what to weigh, not what to conclude.
///
/// ⚠ THE THRESHOLDS BELOW ARE CHOICES, NOT FACTS. Sixty per cent for «a clear majority»
/// and thirty answers for «enough to read» are conventions picked to be defensible, not
/// values measured from anything. They are named constants so the owner can move them,
/// and every finding prints its own numbers so a reader can disagree with the label and
/// still see the data.
/// </summary>
public static class CitizenSurveyFindings
{
    /// <summary>Answers a question needs before any reading is offered.</summary>
    public const int MinimumBase = 30;

    /// <summary>A share at or above this is reported as a clear majority.</summary>
    public const double StrongShare = 0.60;

    /// <summary>A share at or above this leads, if it also clears the runner-up.</summary>
    public const double LeadingShare = 0.40;

    /// <summary>How far the leader must clear the runner-up before it is called a lead.</summary>
    public const double LeadFactor = 1.5;

    /// <summary>Below this gap the answers are reported as divided.</summary>
    public const double DividedFactor = 1.2;

    /// <summary>A «no clear information» answer at or above this is itself a finding.</summary>
    public const double UninformedShare = 0.25;

    /// <summary>Text that marks an option as «I do not know», in the owner's own wording.</summary>
    private const string UninformedMarker = "Тодорхой мэдээлэлгүй";

    public static IReadOnlyList<CitizenSurveyFinding> Read(CitizenSurveyResult? result)
    {
        if (result is null || result.Questions.Count == 0)
            return [];

        var findings = new List<CitizenSurveyFinding>();
        foreach (CitizenSurveyQuestionTally question in result.Questions)
        {
            switch (question.Kind)
            {
                case CitizenSurveyQuestionKinds.FreeText:
                    ReadWrittenAnswers(question, findings);
                    break;
                case CitizenSurveyQuestionKinds.Number:
                    ReadNumbers(question, findings);
                    break;
                default:
                    ReadChoices(question, findings);
                    break;
            }
        }

        return findings;
    }

    private static void ReadChoices(
        CitizenSurveyQuestionTally question,
        List<CitizenSurveyFinding> findings)
    {
        if (question.Options.Count == 0)
            return;

        if (question.Answered < MinimumBase)
        {
            findings.Add(TooFewToRead(question));
            return;
        }

        IReadOnlyList<CitizenSurveyOptionTally> ranked = question.Options
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Text, StringComparer.Ordinal)
            .ToList();
        CitizenSurveyOptionTally top = ranked[0];
        CitizenSurveyOptionTally? second = ranked.Count > 1 ? ranked[1] : null;

        string evidence = Evidence(top.Count, top.Share, question.Answered);

        if (top.Share >= StrongShare)
        {
            findings.Add(new CitizenSurveyFinding(
                question.QuestionId,
                question.Text,
                $"«{top.Text}» — тодорхой давамгайлсан хариулт",
                evidence,
                $"Хариулсан хүмүүсийн дийлэнх нь «{top.Text}» сонгосон тул төлөвлөгөөнд " +
                "тусгах эрэмбэ өндөр. Эсрэг саналыг доорх задаргаанаас шалгана уу.",
                CitizenSurveyFindingWeights.Strong));
        }
        else if (second is not null &&
                 top.Share >= LeadingShare &&
                 second.Value.Count > 0 &&
                 top.Count >= second.Value.Count * LeadFactor)
        {
            findings.Add(new CitizenSurveyFinding(
                question.QuestionId,
                question.Text,
                $"«{top.Text}» тэргүүлж байна",
                evidence + $"; дараагийнх «{second.Value.Text}» {Share(second.Value.Share)}",
                $"Тэргүүлэх хариулт тодорхой боловч дийлэнх болоогүй тул «{second.Value.Text}» " +
                "сонгосон хэсгийг төлөвлөгөөнд бас тусгах шаардлагатай.",
                CitizenSurveyFindingWeights.Attention));
        }
        else if (second is not null &&
                 second.Value.Count > 0 &&
                 top.Count < second.Value.Count * DividedFactor)
        {
            findings.Add(new CitizenSurveyFinding(
                question.QuestionId,
                question.Text,
                "Санал хуваагдсан",
                $"«{top.Text}» {Share(top.Share)}, «{second.Value.Text}» {Share(second.Value.Share)} " +
                $"({question.Answered} хүн хариулсан)",
                "Хоёр хариулт ойролцоо тул нэгийг нь олонхийн санал гэж үзэж болохгүй. " +
                "Энэ асуудлаар нэмэлт хэлэлцүүлэг хийх нь зүйтэй.",
                CitizenSurveyFindingWeights.Attention));
        }

        CitizenSurveyOptionTally uninformed = question.Options.FirstOrDefault(option =>
            option.Text.Contains(UninformedMarker, StringComparison.OrdinalIgnoreCase));
        if (uninformed.Count > 0 && uninformed.Share >= UninformedShare)
        {
            findings.Add(new CitizenSurveyFinding(
                question.QuestionId,
                question.Text,
                "Мэдээлэл дутмаг байна",
                Evidence(uninformed.Count, uninformed.Share, question.Answered),
                "Дөрөвний нэгээс олон хүн тодорхой мэдээлэлгүй гэж хариулсан тул " +
                "төлөвлөлтийн талаарх мэдээллийг иргэдэд хүргэх ажил шаардлагатай.",
                CitizenSurveyFindingWeights.Attention));
        }

        // ⚠ AN OPTION NOBODY CHOSE IS A FINDING TOO, and the one most easily lost: it
        // never appears in a «top answers» list, so a summary built from winners alone
        // would silently drop the fact that something offered was refused by everyone.
        IReadOnlyList<CitizenSurveyOptionTally> refused = question.Options
            .Where(option => option.Count == 0)
            .ToList();
        if (refused.Count > 0 && refused.Count < question.Options.Count)
        {
            findings.Add(new CitizenSurveyFinding(
                question.QuestionId,
                question.Text,
                "Хэн ч сонгоогүй хувилбар байна",
                string.Join(", ", refused.Select(option => $"«{option.Text}»")) +
                $" — {question.Answered} хүнээс нэг нь ч сонгоогүй",
                "Эдгээр хувилбарыг төлөвлөгөөнд тусгах үндэслэл сул байна.",
                CitizenSurveyFindingWeights.Note));
        }
    }

    private static void ReadNumbers(
        CitizenSurveyQuestionTally question,
        List<CitizenSurveyFinding> findings)
    {
        if (question.Answered < MinimumBase)
        {
            findings.Add(TooFewToRead(question));
            return;
        }

        if (question.NumberAverage is not { } average ||
            question.NumberLargest is not { } largest ||
            question.NumberSmallest is not { } smallest)
        {
            return;
        }

        findings.Add(new CitizenSurveyFinding(
            question.QuestionId,
            question.Text,
            $"Дундаж {average:0.0}",
            $"{question.Answered} хүн хариулсан; хамгийн бага {smallest:0.#}, хамгийн их {largest:0.#}",
            "Дундажаар төлөвлөхөд том өрхүүд багтахгүй байх эрсдэлтэй тул " +
            "хамгийн их утгыг сууцны хэмжээ тооцоход харгалзана уу.",
            CitizenSurveyFindingWeights.Note));

        // 🔴 THE TAIL IS THE FINDING IN A HOUSING SURVEY. An average household of four
        // says nothing about the family of eleven the plan still has to house, and an
        // average is exactly the statistic that hides them.
        if (largest >= average * 2 && largest > smallest)
        {
            findings.Add(new CitizenSurveyFinding(
                question.QuestionId,
                question.Text,
                "Дундажаас хамаагүй том утга бүртгэгдсэн",
                $"хамгийн их {largest:0.#}, дундаж {average:0.0}",
                "Дундаж нь эдгээр өрхийг далдалж байна. Сууцны хэв шинжийг тогтоохдоо " +
                "тэдгээрийг тусад нь авч үзэх шаардлагатай.",
                CitizenSurveyFindingWeights.Attention));
        }
    }

    private static void ReadWrittenAnswers(
        CitizenSurveyQuestionTally question,
        List<CitizenSurveyFinding> findings)
    {
        if (question.OwnWords.Count == 0)
            return;

        // ⚠ NOT SUMMARISED, AND DELIBERATELY. Free sentences are where a citizen says the
        // thing the form did not think to ask; a machine-made summary of them would put
        // words in their mouth in a document that is supposed to carry theirs. The count
        // is reported and the reading is left to a person.
        findings.Add(new CitizenSurveyFinding(
            question.QuestionId,
            question.Text,
            $"{question.OwnWords.Count} бичмэл санал ирсэн",
            $"{question.OwnWords.Count} хүн өөрийн үгээр бичсэн",
            "Бичмэл саналыг нэгтгэлээр орлуулах боломжгүй тул доорх жагсаалтыг " +
            "бүтнээр нь уншиж төлөвлөгөөнд тусгана уу.",
            CitizenSurveyFindingWeights.Note));
    }

    private static CitizenSurveyFinding TooFewToRead(CitizenSurveyQuestionTally question) =>
        new(question.QuestionId,
            question.Text,
            "Дүгнэлт хийхэд хариулт хангалтгүй",
            $"{question.Answered} хүн хариулсан ({MinimumBase}-аас цөөн)",
            "Энэ асуултаар дүгнэлт гаргахгүй. Хариултын тоо нэмэгдсэний дараа " +
            "дахин уншина уу.",
            CitizenSurveyFindingWeights.TooFew);

    private static string Evidence(int count, double share, int answered) =>
        $"{Share(share)} ({count}/{answered} хариулсан)";

    private static string Share(double share) => $"{share * 100:0.#}%";
}
