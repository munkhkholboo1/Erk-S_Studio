using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErkS.Platform.Core;

/// <summary>One offered answer, as the public form receives it.</summary>
public sealed record CitizenSurveyPublishedOption(
    string OptionId,
    string Text,
    bool InvitesOwnWords);

/// <summary>One question, as the public form receives it.</summary>
public sealed record CitizenSurveyPublishedQuestion(
    string QuestionId,
    string Text,
    string Kind,
    bool Required,
    IReadOnlyList<CitizenSurveyPublishedOption> Options);

/// <summary>The whole survey, as the public form receives it.</summary>
/// <param name="ProjectId">
/// Which project's consultation this is.
///
/// 🔴 WITHOUT IT THE ANSWERS CANNOT BE COLLECTED BACK. The collect route is scoped
/// by project - it is how the server decides who is allowed to read a consultation's
/// answers at all - and nothing else in the published file says which project this
/// belongs to. SRV found this: they had no way to resolve (project, survey) to a code.
/// </param>
public sealed record CitizenSurveyPublishedDefinition(
    string ProjectId,
    string SurveyId,
    string Code,
    string Title,
    string Purpose,
    bool IsOpen,
    IReadOnlyList<CitizenSurveyPublishedQuestion> Questions);

/// <summary>
/// The survey handed to whoever serves the public form.
///
/// \U0001F534 STUDIO OWNS EVERY IDENTIFIER IN HERE, BY AGREEMENT WITH THE SERVER (SRV, 2026-09-18):
/// \u00ab\u043a\u043e\u0434\u044b\u0433 \u0442\u0430 \u04af\u04af\u0441\u0433\u044d, \u0441\u0435\u0440\u0432\u0435\u0440 \u0442\u04af\u04af\u043d\u0438\u0439\u0433 \u0445\u0430\u0434\u0433\u0430\u043b\u0430\u0445\u0430\u0430\u0441 \u04e9\u04e9\u0440 \u044e\u0443 \u0447 \u0445\u0438\u0439\u0445\u0433\u04af\u0439\u00bb. That is the same rule as
/// invariant 1 carried one step further, and it buys something concrete: the QR can be
/// printed TODAY, because the address it encodes does not wait on a deployment.
///
/// \u26a0 THE KINDS ARE A CLOSED SET OF FOUR and the short names are the wire's, not ours.
/// A fifth kind cannot appear by accident - the mapping is exhaustive and a new kind makes
/// <see cref="Kind"/> throw rather than quietly publishing a question the form cannot draw.
/// A question rendered as the wrong control is not cosmetic: checkboxes on a single-choice
/// question let one person tick three, and every share on it then exceeds what it can be.
///
/// \u26a0 \u00ab\u0411\u0443\u0441\u0430\u0434: ___\u00bb IS AN OPTION WITH A LINE, NOT A WRITTEN QUESTION.
/// <see cref="CitizenSurveyPublishedOption.InvitesOwnWords"/> carries that, so the form can
/// show a text line beside the tick while the tick still counts. Publishing those four as
/// free text would have taken four counted questions out of the statistics.
/// </summary>
public static class CitizenSurveyPublication
{
    public const string SingleChoiceWireKind = "single";
    public const string MultipleChoiceWireKind = "multi";
    public const string NumberWireKind = "number";
    public const string FreeTextWireKind = "text";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static CitizenSurveyPublishedDefinition For(
        ProjectCitizenSurvey? survey,
        string? projectId)
    {
        ArgumentNullException.ThrowIfNull(survey);

        // 🔴 A DEFINITION WITH NO PROJECT IS A ONE-WAY TRIP. The form would serve, the
        // citizens would answer, and the answers could never be collected back - the
        // collect route is scoped by project and would have nothing to match. Refused here,
        // while somebody is looking at a screen, rather than discovered weeks later with a
        // finished consultation stranded on a server.
        string project = (projectId ?? "").Trim();
        if (project.Length == 0)
        {
            throw new InvalidDataException(
                "Төслийн ID алга — энэ тодорхойлолтоор нийтэлбэл хариуг буцааж авах " +
                "боломжгүй болно.");
        }


        var questions = new List<CitizenSurveyPublishedQuestion>();
        foreach (CitizenSurveyQuestion question in survey.OrderedQuestions())
        {
            var options = question.Options
                .Select(option => new CitizenSurveyPublishedOption(
                    option.Id, option.Text, option.InvitesOwnWords))
                .ToList();

            // \U0001F534 ASSERT THE LIMITATION RATHER THAN HOPING NOBODY MEETS IT. An answer
            // carries ONE Text field per question, so a question offering two write-in
            // lines would keep whichever the page read last and lose the other without
            // saying so. SRV found this by reading the form. Until the answer shape can
            // hold a line per option, a second one is refused at publication - loudly,
            // while somebody is still looking at a screen.
            if (options.Count(option => option.InvitesOwnWords) > 1)
            {
                throw new InvalidDataException(
                    $"«{question.Text}» асуулт нэгээс олон бичих мөртэй байна. " +
                    "Нэг асуултад ихдээ нэг «Бусад: ___» мөр байж болно — " +
                    "эс бөгөөс бичсэн үгнүүдийн зөвхөн нэг нь хадгалагдана.");
            }

            questions.Add(new CitizenSurveyPublishedQuestion(
                question.Id,
                question.Text,
                Kind(question.Kind),

                // \u26a0 NOTHING IS REQUIRED, AND THAT IS DELIBERATE. A public consultation that
                // refuses to accept a partly filled form loses everything the person did
                // say; the tally already reports each question's own base, so a skipped
                // answer costs a count, not a submission.
                Required: false,
                options));
        }

        return new CitizenSurveyPublishedDefinition(
            project,
            survey.Id,
            survey.PublicCode,
            survey.Title,
            survey.Purpose,
            survey.IsOpen,
            questions);
    }

    public static string ToJson(ProjectCitizenSurvey? survey, string? projectId) =>
        JsonSerializer.Serialize(For(survey, projectId), Options);

    private static string Kind(string? studioKind) => studioKind switch
    {
        CitizenSurveyQuestionKinds.SingleChoice => SingleChoiceWireKind,
        CitizenSurveyQuestionKinds.MultipleChoice => MultipleChoiceWireKind,
        CitizenSurveyQuestionKinds.Number => NumberWireKind,
        CitizenSurveyQuestionKinds.FreeText => FreeTextWireKind,

        // Never a fallback. A kind nobody mapped would be published as something the form
        // draws wrongly, and a wrongly drawn control corrupts the answers rather than
        // failing - which is the one outcome nothing downstream can detect.
        _ => throw new InvalidDataException(
            $"Асуултын төрөл нийтлэгдэх жагсаалтад алга: «{studioKind}»"),
    };
}

/// <summary>
/// Mints the code that appears in the public address.
///
/// \U0001F534 TWELVE HEX CHARACTERS, AND BOTH HALVES OF THAT ARE A CHOICE. Long enough that
/// nobody finds another consultation's form by trying, short enough that the QR stays
/// coarse - a denser symbol is harder to scan from a phone held at arm's length, which is
/// exactly how these are read.
/// </summary>
public static class CitizenSurveyCode
{
    public const int Length = 12;

    public static string Mint() => Guid.NewGuid().ToString("N")[..Length];
}
