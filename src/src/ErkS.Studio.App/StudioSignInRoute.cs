namespace ErkS.Studio;

/// <summary>
/// Where «Нэвтрэх» goes.
///
/// 🔴 THERE WERE TWO DOORS AND ONE OF THEM WAS INCOMPLETE. On a seated machine
/// with no owner session the account menu showed «Эзэмшигчээр нэвтрэх…» AND
/// «Нэвтрэх». The first ran the full return; the second only signed in - the bot
/// token stayed in hand, the seat's assignments, scopes and member line stayed
/// cached, and the lock was not taken off. That is the half-state the product was
/// repaired for: «гэтэл бот төлөв хэвээрээ».
///
/// 🔴 AND THE INCOMPLETE DOOR CLOSED THE COMPLETE ONE BEHIND IT. With an owner
/// session now in hand, <see cref="StudioBotMenuPlan"/> stops offering
/// OwnerPassport - so the full return disappears from the menu and the way back
/// is «Ботын төлөвт буцах…» followed by the owner door: two steps, neither
/// obvious. To the person holding the machine that is indistinguishable from the
/// trap that cost them a full day - «ботын төлөвөөс гаргах товч нь өөрөө бот
/// төхөөрөмж байна гэсэн алдаа заагаад байхаар яаж энэ төлвөөс гарах болж
/// байна» - and it is not something to explain away afterwards.
///
/// 🔴 THE ANSWER IS DERIVED FROM THE MENU, NOT RESTATED BESIDE IT. The rule is
/// «wherever the menu offers the owner door, the plain sign-in must reach the
/// same place». Written as its own condition it would be true today and drift the
/// first time either side changed - and the drift would be invisible, because
/// both conditions would still look right. Asking the menu makes the two agree by
/// construction.
/// </summary>
internal static class StudioSignInRoute
{
    /// <summary>
    /// Whether a plain «Нэвтрэх» has to go through the seated owner door rather
    /// than the ordinary sign-in.
    /// </summary>
    /// <param name="seatedAsBot">Whether this machine holds a seat record.</param>
    /// <param name="ownerSessionInHand">Whether an owner is already signed in.</param>
    public static bool GoesThroughTheSeatedOwnerDoor(
        bool seatedAsBot,
        bool ownerSessionInHand) =>
        StudioBotMenuPlan
            .For(seatedAsBot, ownerSessionInHand)
            .Contains(BotMenuEntry.OwnerPassport);
}
