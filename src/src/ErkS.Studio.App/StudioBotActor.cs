namespace ErkS.Studio;

/// <summary>
/// Who is acting on a machine that holds a bot seat.
///
/// 🔴 THIS EXISTS BECAUSE THE QUESTION HAD NO NAME AND THE OWNER LOST THEIR PROJECT
/// LIST. «Seated» means the MACHINE holds a bot seat. Switching back to the owner
/// deliberately does not release that seat - «Ботын төлөвт буцах…» takes it again
/// with the PIN - so a machine stays seated while the owner is the one working on
/// it. Every rule that asked «is this machine seated?» when it meant «is the bot
/// acting?» therefore answered about the wrong person, and the project filter
/// answered «show nothing» to the owner on their own computer.
///
/// 🔴 IT WAS ALREADY BEING COMPUTED IN THREE SPELLINGS. The profile menu wrote
/// «SeatedAsBot &amp;&amp; !account.IsSignedIn» inline, seat management wrote
/// «!SeatedAsBot || account.IsSignedIn», and the project filter wrote
/// «SeatedAsBot» - the same question twice and a different question once, with
/// nothing to say they were meant to agree. One question, one name, one place.
///
/// ⚠ THIS IS NOT A PERMISSION. The server refuses what a seat may not do. This only
/// decides what is OFFERED, and a change here can never be read as a security fix -
/// the same standing caveat as <see cref="StudioBotSurfaceVisibility"/>.
/// </summary>
internal static class StudioBotActor
{
    /// <summary>
    /// Whether the bot is the one acting.
    ///
    /// 🔴 THE OWNER'S OWN SESSION IS WHAT SETTLES IT, NOT THE PIN. The PIN opens the
    /// seat; the passport opens the owner. So an owner session in hand on a seated
    /// machine means the owner is acting - which is the case that used to answer
    /// wrongly, and the only one of the four that changed.
    /// </summary>
    /// <param name="deviceHoldsBotSeat">
    /// This machine's own state, from the device seat store. Survives the owner
    /// signing in, which is the whole reason this method is needed.
    /// </param>
    /// <param name="ownerSessionInHand">An owner is signed in on this machine.</param>
    public static bool IsTheBotActing(bool deviceHoldsBotSeat, bool ownerSessionInHand) =>
        deviceHoldsBotSeat && !ownerSessionInHand;

    /// <summary>
    /// Whether a machine may be asked to manage seats.
    ///
    /// Derived from the one question rather than spelled a second way: the old
    /// «!seated || signed in» is the same truth table, and two spellings of one
    /// question is how they come to disagree about a case neither author had in mind.
    /// </summary>
    public static bool MayManageSeats(bool deviceHoldsBotSeat, bool ownerSessionInHand) =>
        !IsTheBotActing(deviceHoldsBotSeat, ownerSessionInHand);
}
