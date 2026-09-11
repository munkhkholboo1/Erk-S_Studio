namespace ErkS.Studio;

/// <summary>What a profile row asks for before it will let somebody in.</summary>
internal enum StudioProfileCredential
{
    /// <summary>The whole passport: the owner's own sign-in.</summary>
    Passport,

    /// <summary>Four digits. Enough for a seat, never enough for the owner.</summary>
    Pin,
}

/// <summary>
/// One row of the profile list.
/// </summary>
/// <param name="Name">Who this is, by name.</param>
/// <param name="Kind">The short tag beside it - empty for the owner.</param>
/// <param name="Credential">What choosing it will ask for.</param>
/// <param name="IsCurrent">Whether this is who the work is going out as right now.</param>
internal sealed record StudioProfileChoice(
    string Name,
    string Kind,
    StudioProfileCredential Credential,
    bool IsCurrent);

/// <summary>
/// The profiles this machine can switch between.
///
/// 🔴 THE OWNER REPLACED THREE SEPARATE BUTTONS WITH ONE LIST. «Ер нь яах гэж ийм
/// тусдаа товч хийгээд байгаа юм бэ? Би зүгээр л өөрийнхөө профайлыг сонгоод тэр
/// дээрээ пасспортоо хийгээд бот төлвөөс бүрэн чөлөөлөгдөж үндсэн эзэмшигчээрээ
/// нэвтэрчихмээр байнашт» - and «бот бол пин кодоо хийнэ. эзэмшигч бол пассаа
/// хийнэ».
///
/// The device's state stops being something to MANAGE and becomes a CONSEQUENCE
/// of who you chose to be. That is why the trap of 2026-09-11 cannot form in this
/// shape: there is no separate «manage» step, so there is no precondition on it to
/// block the way out.
///
/// 🔴 AND THE LIST IS SHORT, WHICH IS WHY THIS IS NOT A MULTI-ACCOUNT SWITCHER. A
/// machine remembers ONE person and holds at most ONE seat, so there are at most
/// two rows. Building a general account switcher would settle a design nobody has
/// asked for - the note on StudioRememberedProfile says exactly that, and it still
/// holds.
/// </summary>
internal static class StudioProfileChoices
{
    /// <summary>The tag beside a seat's name. Same word the masthead uses.</summary>
    public const string BotKind = StudioActingIdentityBadge.BotMark;

    /// <param name="ownerName">The remembered person's name, or empty.</param>
    /// <param name="botName">The seat's name, or empty when this machine holds none.</param>
    /// <param name="actingAsBot">Whether the work currently goes out as the seat.</param>
    public static IReadOnlyList<StudioProfileChoice> For(
        string? ownerName,
        string? botName,
        bool actingAsBot)
    {
        var rows = new List<StudioProfileChoice>(2);

        // The owner is offered whenever this machine remembers one - including
        // from inside bot state, because that is the way back and it must not
        // depend on anything the seat can take away.
        if (!string.IsNullOrWhiteSpace(ownerName))
        {
            rows.Add(new StudioProfileChoice(
                StudioActingIdentityBadge.NamedOrUnknown(ownerName),
                "",
                StudioProfileCredential.Passport,
                IsCurrent: !actingAsBot));
        }

        // 🔴 THE SEAT IS OFFERED ONLY IF ONE EXISTS. «бот үүсгээгүй бол байхгүй» -
        // an empty row inviting somebody to become a bot they never made is the
        // «manage» step under another name.
        if (!string.IsNullOrWhiteSpace(botName))
        {
            rows.Add(new StudioProfileChoice(
                StudioActingIdentityBadge.NamedOrUnknown(botName),
                BotKind,
                StudioProfileCredential.Pin,
                IsCurrent: actingAsBot));
        }

        return rows;
    }

    /// <summary>
    /// Whether choosing this row changes anything.
    ///
    /// Selecting who you already are must not ask for a credential, and must not
    /// run a switch: a no-op that erases the session and rebuilds it is how a
    /// person loses their place for pressing their own name.
    /// </summary>
    public static bool IsASwitch(StudioProfileChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return !choice.IsCurrent;
    }
}
