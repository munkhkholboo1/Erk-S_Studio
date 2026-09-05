using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// What the location pickers' headings say - a pass-through, extracted so it can
/// be MEASURED rather than only described.
///
/// The view's first guard was a list of forbidden words: «Хороо», «Баг»,
/// «Дүүрэг». That catches the obvious regression and misses the one that
/// matters, because it searches by NAME. A heading computed under a different
/// spelling, pulled from a resource, or built by concatenation would slip past
/// it untouched - the same shape as looking for a defect by its wording instead
/// of by its mechanism.
///
/// Being a function of the choices makes the mechanism testable: hand it a
/// heading no rule could have invented and see whether that exact string comes
/// back. Nothing needs to know which words are legitimate.
/// </summary>
internal static class SiteLocationLabels
{
    /// <summary>
    /// The heading, exactly as the catalogue published it. Not shortened, not
    /// capitalised, not translated - Erdenet's wards are «Баг» and one place's
    /// are «Тосгон», and the next word to appear will not be in any list here.
    /// </summary>
    public static string HeadingFor(AdministrativeUnitChoices choices) =>
        choices is null ? "" : choices.LabelMn;

    /// <summary>
    /// Whether to show it at all. A level with no heading has no parent chosen
    /// yet, so there is nothing truthful to call it.
    /// </summary>
    public static bool HeadingIsShown(AdministrativeUnitChoices choices) =>
        HeadingFor(choices).Length > 0;

    /// <summary>
    /// What to say when a level offers nothing.
    ///
    /// 🔴 THE TWO EMPTY LISTS ARE NOT THE SAME. A level whose parent has not
    /// been chosen is simply waiting. A level whose parent WAS chosen and which
    /// still lists nothing is a real answer: three sums - Хатгал, Бэрх,
    /// Гурванбаян - carry «Баг» as their child label with no bags published.
    ///
    /// Both end in an empty combo box, so without saying which is which the
    /// reader is told the catalogue failed when it is complete and correct.
    /// </summary>
    public static string EmptyNoticeFor(AdministrativeUnitChoices choices)
    {
        if (choices is null || choices.IsWaitingForParent)
            return "";
        return choices.IsEmptyByData
            ? $"Энэ нэгжид бүртгэгдсэн «{HeadingFor(choices)}» алга байна " +
              "(каталог бүрэн, зүгээр л хоосон)."
            : "";
    }
}
