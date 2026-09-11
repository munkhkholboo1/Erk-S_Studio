namespace ErkS.Studio;

/// <summary>
/// Which surfaces a machine acting as a bot seat is shown.
///
/// 🔴 THE OWNER REDUCED A WHOLE DEVICE-LOCK DESIGN TO ONE SENTENCE: «Зүгээр л
/// үндсэн хэрэглэгчийн удирдлагын хэсгийг нуучихдаг болгочихмоор байхын. Компани
/// болон төслийн мэдээлэл нь л харагдахгүй. Нэг үгээр бол ӨӨР ХЭРЭГЛЭГЧ ШИГ Л
/// БАЙНА.» And: «өөр хэрэглэгчид миний байгууллагын жагсаалт байхгүйтай адил».
///
/// So this is not a new permission system - it is the collaborator model the
/// platform already has, with the management surfaces left out. A bot works on
/// the projects it is assigned and sees none of the owner's administration.
///
/// 🔴 HIDING IS NOT THE PROTECTION AND MUST NEVER BE MISTAKEN FOR IT. The server
/// refuses what a seat may not do, and says why. This only stops offering a road
/// that ends in a refusal - which is a different job, and the reason a change
/// here can never be read as a security fix.
/// </summary>
internal static class StudioBotSurfaceVisibility
{
    /// <summary>
    /// The owner's administration: the organisation library and the project's own
    /// information. Named by the page, so the rule can be read without opening
    /// the shell.
    /// </summary>
    public static readonly IReadOnlyList<string> HiddenFromABot = ["Companies", "Foundation"];

    /// <summary>
    /// Every page the shell can show. Kept here so a page added to the shell and
    /// not thought about here goes RED in a test rather than quietly appearing on
    /// a bot's screen - which is how a management surface leaks.
    /// </summary>
    public static readonly IReadOnlyList<string> AllPages =
    [
        "Home", "Projects", "Companies", "Foundation", "Participants", "Sources",
        "Albums", "Portfolio", "Boards", "Research", "Records", "Reports", "Archive",
    ];

    /// <param name="seatedAsBot">This machine is acting as a bot seat.</param>
    /// <param name="page">The page's own name, as the shell spells it.</param>
    public static bool IsVisible(bool seatedAsBot, string? page)
    {
        if (!seatedAsBot)
            return true;

        string name = (page ?? "").Trim();
        return !HiddenFromABot.Contains(name, StringComparer.Ordinal);
    }
}
