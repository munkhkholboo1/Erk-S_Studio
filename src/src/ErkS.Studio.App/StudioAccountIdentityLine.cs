namespace ErkS.Studio;

/// <summary>Who the account block is naming.</summary>
internal enum AccountIdentityKind
{
    /// <summary>Nobody is acting. No session, and this machine holds no seat.</summary>
    SignedOut,

    /// <summary>A person, on an ordinary machine.</summary>
    Person,

    /// <summary>
    /// A person, on a machine that also holds a seat. TWO facts, and they belong
    /// in two places: the line that names who is acting says the person, and the
    /// machine's seat is said underneath.
    /// </summary>
    PersonOnSeatedDevice,

    /// <summary>The seat, still sealed. The PIN has not opened it.</summary>
    SeatLocked,

    /// <summary>The seat, acting. The PIN opened it and no person is signed in.</summary>
    SeatActing,
}

/// <summary>
/// Who the account block names, given what is true about this machine.
///
/// 🔴 THE LINE ASKED THE WRONG QUESTION. It asked «does this machine hold a
/// seat» and printed «Бот: &lt;name&gt;» whenever the answer was yes - so an owner
/// who signed in on their own seated machine was shown the bot's name, with
/// their own moved into a tooltip. They read it as the sign-in not having
/// taken: «би дөнгөж сая өөрийн үндсэн бүртгэлээр нэвтэрсэн, гэтэл бот төлөв
/// хэвээрээ».
///
/// The seat is a fact about the DEVICE; this line answers WHO IS ACTING. They
/// are different questions and only one of them can have the name.
///
/// A function of three facts, so the answer can be stated in a test rather than
/// discovered by signing in - the same reason StudioBotMenuPlan exists, and the
/// same rule that was wrong three times while it lived inside a method that
/// builds WPF controls.
/// </summary>
internal static class StudioAccountIdentityLine
{
    /// <param name="ownerSessionInHand">
    /// A person's session exists. On a seated machine this can only have come
    /// from a full passport sign-in: seating erases the owner credential, and
    /// the seat's own token is not a session.
    /// </param>
    /// <param name="deviceHoldsSeat">This machine holds a bot seat, signed in or not.</param>
    /// <param name="seatUnlocked">The PIN has opened the seat's credential.</param>
    public static AccountIdentityKind For(
        bool ownerSessionInHand,
        bool deviceHoldsSeat,
        bool seatUnlocked)
    {
        if (!deviceHoldsSeat)
            return ownerSessionInHand ? AccountIdentityKind.Person : AccountIdentityKind.SignedOut;

        // The person wins the name. Both can be true for a moment - the seat is
        // opened with the PIN and the owner then signs in - and in that moment
        // the one whose credential the work goes out under is the person.
        if (ownerSessionInHand)
            return AccountIdentityKind.PersonOnSeatedDevice;

        return seatUnlocked ? AccountIdentityKind.SeatActing : AccountIdentityKind.SeatLocked;
    }

    /// <summary>
    /// Whether the machine's seat has to be said somewhere on this block.
    ///
    /// Derived rather than written twice: a seated machine says so wherever the
    /// account is shown, and that promise must not depend on which of the three
    /// seated kinds happens to be current.
    /// </summary>
    public static bool ShowsDeviceSeat(AccountIdentityKind kind) =>
        kind is AccountIdentityKind.PersonOnSeatedDevice
            or AccountIdentityKind.SeatLocked
            or AccountIdentityKind.SeatActing;
}
