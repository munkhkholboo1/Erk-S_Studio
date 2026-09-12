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
    /// <param name="seatEnteredByEmail">
    /// The owner who put this machine into bot state, as the seat recorded it.
    /// </param>
    /// <param name="signedInEmail">Whoever is signed in on this machine now.</param>
    public static bool IsTheBotActing(
        bool deviceHoldsBotSeat,
        string? seatEnteredByEmail,
        string? signedInEmail)
    {
        if (!deviceHoldsBotSeat)
            return false;

        string signedIn = Normalise(signedInEmail);
        if (signedIn.Length == 0)
            return true;

        // 🔴 IT IS NOT «SOMEBODY SIGNED IN», IT IS «THAT OWNER». The owner drew
        // the line themselves: «өөр эзэмшигчийн ботыг өөр эзэмшигчийн эрхтэй хольж
        // хутгаж болохгуй шүү». A seat belongs to the licence holder who created it;
        // for anybody else it must not exist at all, so a different owner signing in
        // here leaves the machine acting as the bot and takes none of its rights.
        string seatOwner = Normalise(seatEnteredByEmail);

        // ⚠ AND WHEN THE SEAT NEVER RECORDED ITS OWNER, THIS CANNOT ASK. The field
        // existed from 2026-09-04 and was written for the first time on 2026-09-12:
        // it had been read out of the account AFTER the transition that erases the
        // account, so every seat made before that fix carries an empty string.
        // Failing closed there would answer «the bot is acting» to the owner on their
        // own machine - the exact fault fixed hours ago, reinstated. So an unrecorded
        // seat keeps the older behaviour, and every seat made from now on can answer.
        // This is a known hole with a known shape, not an oversight.
        if (seatOwner.Length == 0)
            return false;

        return !seatOwner.Equals(signedIn, StringComparison.Ordinal);
    }

    /// <summary>
    /// One spelling for one address. The same trim-and-lower the rest of the app
    /// uses on account emails; comparing them raw would make case a boundary.
    /// </summary>
    private static string Normalise(string? email) =>
        (email ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Whether a machine may be asked to manage seats.
    ///
    /// Derived from the one question rather than spelled a second way: the old
    /// «!seated || signed in» was the same truth table until the owner's decision
    /// split one of its rows, and two spellings of one question is how they come to
    /// disagree about a case neither author had in mind.
    ///
    /// 🔴 STRICTER THAN IT WAS, ON PURPOSE: a DIFFERENT owner signing in on this
    /// machine may no longer manage its seat. The old formula said yes to anybody
    /// with a session.
    /// </summary>
    public static bool MayManageSeats(
        bool deviceHoldsBotSeat,
        string? seatEnteredByEmail,
        string? signedInEmail) =>
        !IsTheBotActing(deviceHoldsBotSeat, seatEnteredByEmail, signedInEmail);
}
