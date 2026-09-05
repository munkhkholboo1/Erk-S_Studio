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

    /// <summary>Give this machine back to its owner.</summary>
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
    public static IReadOnlyList<BotMenuEntry> For(bool seatedAsBot, bool ownerSessionInHand)
    {
        if (seatedAsBot && !ownerSessionInHand)
        {
            // The door, and only the door. Showing it grants nothing - it asks
            // for the passport, and the PIN cannot answer.
            return [BotMenuEntry.OwnerPassport];
        }

        return seatedAsBot
            ? [BotMenuEntry.ManageSeats, BotMenuEntry.LeaveBotState]
            : [BotMenuEntry.ManageSeats, BotMenuEntry.SeatThisDevice];
    }
}
