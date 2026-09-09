namespace ErkS.Studio;

/// <summary>
/// What to say about the company a seat was opened for - if anything.
///
/// 🔴 IT IS NOT A COLUMN, AND THAT WAS A DELIBERATE REVERSAL. A column would
/// print an empty cell for every seat opened since the owner's decision, and an
/// empty cell reads as «this is missing» when the truth is «there is no such
/// thing»: a seat belongs to the account, and the company lives on the PROJECT
/// the bot is assigned to. Showing a gap where a link used to be is worse than
/// showing nothing, because it invites somebody to fill it.
///
/// So it is history, on the seat the owner selected, and absent when there is
/// none to tell.
/// </summary>
internal static class StudioBotSeatOrigin
{
    /// <summary>
    /// The line to show, or empty when there is nothing to say.
    /// </summary>
    /// <param name="organizationId">Empty when the seat was opened under no company.</param>
    /// <param name="organizationName">
    /// Empty when the SERVER could not resolve a name for that id - which is
    /// not the same as having no company, and is why the id is shown instead of
    /// nothing.
    /// </param>
    public static string Describe(string organizationId, string organizationName)
    {
        string id = (organizationId ?? "").Trim();
        if (id.Length == 0)
            return "";

        string name = (organizationName ?? "").Trim();
        return "Нээсэн үеийн байгууллага: " + (name.Length == 0 ? id : "«" + name + "»");
    }
}
