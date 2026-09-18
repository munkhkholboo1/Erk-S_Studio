namespace ErkS.Platform.Core;

/// <summary>
/// Every citizens' survey a project runs.
///
/// 🔴 MANY, NOT ONE, BY THE OWNER'S OWN WORDS: «Төсөл дотор санал асуулга гэдэг цонх
/// альбом гэдэг шиг үүснэ. Дотор санал асуулга олон байж болох бөгөөд тухайн санал
/// асуулга руу ороход тухайн санал асуулгатай холбоотой мэдээлэл гарч ирдэг байна.» A
/// partial master plan asks the public more than once - a first round about how people
/// live now, a later one about the draft they were shown - and each round has to keep
/// its own answers. One survey per project would have merged two consultations held
/// months apart into a single result, which is exactly the thing a planning process
/// must never do.
///
/// Shaped after <see cref="ProjectBoardSeries"/>, which is the same thing one step
/// earlier: a page in the shell that holds a list the owner adds to.
/// </summary>
public sealed class ProjectCitizenSurveySeries
{
    public string Title { get; set; } = "Санал асуулга";

    public List<ProjectCitizenSurvey> Surveys { get; set; } = [];

    /// <summary>In the order the owner arranged them, newest work last.</summary>
    public IReadOnlyList<ProjectCitizenSurvey> OrderedSurveys() => (Surveys ?? [])
        .OrderBy(survey => survey.Order)
        .ThenBy(survey => survey.CreatedAtUtc)
        .ThenBy(survey => survey.Id, StringComparer.Ordinal)
        .ToList();

    public ProjectCitizenSurvey? Find(string? surveyId)
    {
        string wanted = (surveyId ?? "").Trim();
        return wanted.Length == 0
            ? null
            : (Surveys ?? []).FirstOrDefault(survey =>
                survey.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Puts the list back into a state the rest of the program can rely on.
    ///
    /// ⚠ IT RENUMBERS AND NEVER REMOVES. A survey with a duplicated order is a display
    /// problem; a survey quietly dropped is a consultation erased. The owner's rule for
    /// their own material holds here with more force than anywhere else in the program -
    /// what a citizen wrote is not ours to tidy away.
    /// </summary>
    public void Normalize()
    {
        Surveys ??= [];
        var order = 0;
        foreach (ProjectCitizenSurvey survey in OrderedSurveys())
        {
            survey.Order = ++order;
            survey.Normalize();
        }
    }
}
