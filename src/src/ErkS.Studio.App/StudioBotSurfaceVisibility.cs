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
    /// Surfaces withheld from a seat. EMPTY, BY THE OWNER'S DECISION - not by
    /// oversight, and not waiting to be filled in.
    ///
    /// 🔴 THE OWNER MADE IT A GENERAL RULE, TWICE OVER (2026-09-12): «компани
    /// цэсийг алга болгох заавал шаардлага байхгүй. идэвхгүй байхад л болно», and
    /// then, watching the create button ask for a passport: «ботоос шинэ төсөл
    /// үүсгэх дарж болж байна. гэхдээ үндсэн эзэмшигчээр нэвтрэхийг шаардаж байна.
    /// энэ маш зөв үйлдэл». So what a seat is stopped at is the ACTION, which asks
    /// for the owner - never the surface.
    ///
    /// 🔴 AND THE REASONING IS THEIRS, NOT A PREFERENCE: a hidden surface reads
    /// as «this program cannot do that», while a surface that is present and asks
    /// for a sign-in says WHAT TO DO. The worker should meet a door, not a wall, and
    /// should be able to see who is behind it.
    ///
    /// ⚠ CONTENT IS A DIFFERENT QUESTION AND IS STILL WITHHELD: «компани болон
    /// төслийн мэдээлэл нь л харагдахгүй». The Companies page opens on a seat and
    /// holds no organisations. Surface is not content; one rule for both would
    /// produce the opposite defect.
    ///
    /// The list and the check are kept rather than deleted so that the day one
    /// surface genuinely must go, it is one line here and not a re-threading -
    /// but nothing is protected by it today, and it must not be read as a guard.
    /// </summary>
    public static readonly IReadOnlyList<string> HiddenFromABot = [];

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

    /// <param name="actingAsBot">
    /// The BOT is the one acting. NOT «this machine holds a seat»: the seat stays
    /// while the owner signs in on the same machine, and hiding their own
    /// administration from them was the same defect that hid their projects.
    /// </param>
    /// <param name="page">The page's own name, as the shell spells it.</param>
    public static bool IsVisible(bool actingAsBot, string? page)
    {
        if (!actingAsBot)
            return true;

        string name = (page ?? "").Trim();
        return !HiddenFromABot.Contains(name, StringComparer.Ordinal);
    }
}
