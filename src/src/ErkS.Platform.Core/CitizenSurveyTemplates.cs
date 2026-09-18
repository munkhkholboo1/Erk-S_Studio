namespace ErkS.Platform.Core;

/// <summary>One form Studio can start a survey from.</summary>
/// <param name="Availability">
/// How ready it is, in the owner's own terms. Shown beside the name because a form in
/// trial use and a finished one are not the same offer.
/// </param>
public sealed record CitizenSurveyTemplateInfo(
    string Id,
    string Name,
    string Description,
    string Availability);

/// <summary>Where a form stands. Never a quality claim - only what the owner has said.</summary>
public static class CitizenSurveyTemplateAvailability
{
    /// <summary>In real use on a live project while it is still being tried out.</summary>
    public const string Trial = "Туршилтын шатанд";

    /// <summary>Settled enough to start any project from.</summary>
    public const string Ready = "Бэлэн";
}

/// <summary>
/// The forms a project may start a survey from.
///
/// 🔴 ONE FORM TODAY, AND THE LIST EXISTS BECAUSE OF THE SECOND. The owner:
/// «миний чамд өгсөн санал асуулга Зуун-Модын хэсэгчилсэн ерөнхий төлөвлөгөөнд
/// зориулсан санал асуулга … одоогоор туршилтын шатанд байгаагаас гадна бодит хэрэглээ
/// нь энэ төсөлд байгаа тул тухайн санал асуулгад тохируулж хийгээрэй. цаашид аль ч
/// төсөлд хэрэглэж болох санал асуулгыг боловсруулж НЭМНЭ.» Nemne - added. So the general
/// form must arrive as a new row here, never as an edit to the row below: a project that
/// is already collecting answers against those questions cannot have them changed
/// underneath it.
///
/// ⚠ AND A SURVEY RECORDS WHICH ROW IT CAME FROM. Two rounds in one project may use two
/// different forms, and a result read months later has to be able to say which questions
/// were actually asked. Without that, the day a second form lands every older survey
/// silently starts looking like the newest one.
///
/// 🔴 AN UNKNOWN ID BUILDS NOTHING. Falling back to «the one form I have» would let a
/// mistyped id - or a project saved by a newer Studio that names a form this build does
/// not carry - collect citizens' answers against the WRONG questionnaire, and every
/// number computed from them would be wrong while looking complete.
/// </summary>
public static class CitizenSurveyTemplates
{
    /// <summary>The owner's own form: «санал асуулга.docx», 2026-09-18.</summary>
    public const string PartialMasterPlanId = "partial-master-plan";

    public static IReadOnlyList<CitizenSurveyTemplateInfo> All { get; } =
    [
        new(PartialMasterPlanId,
            "Хэсэгчилсэн ерөнхий төлөвлөгөө — иргэдийн санал асуулга",
            "Эзний бэлтгэсэн 16 асуулт. Зуунмодын ХЕТ-д зориулж бичигдсэн бөгөөд " +
            "суурины нэрийг төслөөс авч бичнэ.",
            CitizenSurveyTemplateAvailability.Trial),
    ];

    public static CitizenSurveyTemplateInfo? Find(string? templateId)
    {
        string wanted = (templateId ?? "").Trim();
        return wanted.Length == 0
            ? null
            : All.FirstOrDefault(form => form.Id.Equals(wanted, StringComparison.Ordinal));
    }

    /// <summary>
    /// Builds the named form for a settlement, or null if this build does not carry it.
    /// </summary>
    public static ProjectCitizenSurvey? Create(string? templateId, string? settlementName)
    {
        CitizenSurveyTemplateInfo? form = Find(templateId);
        if (form is null)
            return null;

        ProjectCitizenSurvey? survey = form.Id switch
        {
            PartialMasterPlanId =>
                CitizenSurveyTemplate.CreatePartialMasterPlanSurvey(settlementName),
            _ => null,
        };

        // Unreachable while every row above has an arm. Kept so that adding a row and
        // forgetting the arm returns nothing rather than an empty survey - and the test
        // EVERYOfferedFormCanActuallyBeBuilt goes red the same day.
        if (survey is null)
            return null;

        survey.TemplateId = form.Id;
        return survey;
    }
}
