namespace ErkS.Studio;

/// <summary>What the account menu offers about bot seats.</summary>
internal enum BotMenuEntry
{
    /// <summary>Ask for the owner's whole passport. Never the PIN.</summary>
    OwnerPassport,

    /// <summary>Create, invite, release, delete - the licence owner's actions.</summary>
    ManageSeats,

    /// <summary>Turn this machine into a bot.</summary>
    SeatThisDevice,

    /// <summary>
    /// Put this machine back into bot state now, without restarting it.
    ///
    /// 🔴 THE MISSING HALF OF A DOOR THAT ONLY OPENED ONE WAY. Signing in as
    /// the owner took the lock off a seated machine, and nothing put it back:
    /// the only code that installs it runs at start-up, so the way back into
    /// bot state was to close Studio and open it again. The owner said so
    /// exactly - «буцаад бот горимруугаа орох боломж алга».
    /// </summary>
    EnterBotState,

    /// <summary>
    /// Give this machine back to its owner - the seat is released and the
    /// device stops being a bot at all. Destructive, and next to an entry that
    /// merely switches, so its label must name what it gives up.
    /// </summary>
    LeaveBotState,
}

/// <summary>
/// Which bot entries the account menu shows, given what is true about this
/// machine right now.
///
/// This rule has been wrong three times, each time in a way no test caught,
/// because it lived inside a method that builds WPF controls:
///   - it offered seat management to a machine acting as the bot;
///   - it then offered NOTHING to a seated machine, including the way out, so a
///     device unlocked with its PIN had no exit at all;
///   - and the menu that carried it was built once at start-up, so signing in as
///     the owner changed the rule and not the menu.
/// Pulled out here it is a function of two facts, and the facts can be stated in
/// a test.
/// </summary>
internal static class StudioBotMenuPlan
{
    /// <param name="seatedAsBot">This machine holds a bot seat.</param>
    /// <param name="ownerSessionInHand">
    /// An owner session exists. On a seated machine this can only have come from
    /// a full passport sign-in: seating erases the owner credential, and the
    /// seat's own token is not a session. So it is the proof, not a hint.
    /// </param>
    /// <param name="machineHasBeenASeat">
    /// This machine carries the durable trace of having been a seat - its
    /// registered device key - whether or not a seat record is still on disk.
    ///
    /// 🔴 THE OWNER'S MACHINE WAS TRAPPED BECAUSE THIS FACT HAD NO VOTE. Their
    /// local seat record was gone while the SERVER still held the device in bot
    /// state, so every read of «are we seated» said no, the menu showed the
    /// unseated entries, and the way out was not among them. Asked to leave,
    /// they were told to manage the seat; asked to manage it, they were told to
    /// leave first. They described it exactly: «анх үүсэхдээ сайхан шилжинэ …
    /// ахиж нээхэд шилжиж орж чадахгүй».
    ///
    /// The exit is offered on the WIDER fact on purpose. Hiding it is what built
    /// the trap, and offering it to a machine that turns out to be free costs a
    /// sentence saying so - which is the cheaper of the two mistakes by a day.
    /// </param>
    public static IReadOnlyList<BotMenuEntry> For(
        bool seatedAsBot,
        bool ownerSessionInHand,
        bool machineHasBeenASeat = false)
    {
        if (!seatedAsBot && machineHasBeenASeat && ownerSessionInHand)
        {
            // No seat on disk, but this machine has been one. The server may
            // still hold it, and only this entry can find out.
            return [BotMenuEntry.ManageSeats, BotMenuEntry.SeatThisDevice, BotMenuEntry.LeaveBotState];
        }

        if (seatedAsBot && !ownerSessionInHand)
        {
            // The door, and only the door. Showing it grants nothing - it asks
            // for the passport, and the PIN cannot answer.
            return [BotMenuEntry.OwnerPassport];
        }

        return seatedAsBot
            // Both ways are offered, and they are not the same size: one
            // switches this session, the other gives the seat up. A machine
            // that only offered the second made leaving the only way back.
            ? [BotMenuEntry.ManageSeats, BotMenuEntry.EnterBotState, BotMenuEntry.LeaveBotState]
            : [BotMenuEntry.ManageSeats, BotMenuEntry.SeatThisDevice];
    }
}
