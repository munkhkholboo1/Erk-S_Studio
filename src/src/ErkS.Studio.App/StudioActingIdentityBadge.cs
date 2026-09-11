namespace ErkS.Studio;

/// <summary>
/// What the masthead says about WHO IS ACTING right now.
/// </summary>
/// <param name="Mark">A short tag - «БОТ» - or empty for the owner.</param>
/// <param name="Name">The acting identity's name.</param>
/// <param name="IsBot">Whether the work goes out as a bot seat.</param>
internal sealed record StudioActingIdentity(string Mark, string Name, bool IsBot);

/// <summary>
/// Which identity the top of the window names.
///
/// 🔴 THE OWNER WORKED FOR HOURS BELIEVING THEY WERE THEMSELVES. The masthead read
/// «Erk-S Studio / CLOUD ERA» in both states and the account button showed THEIR
/// name, while a status line further down said the machine was in bot state and
/// the server refused them as a seat. They asked for the fix in one sentence:
/// «дээд талын логоны ард бот уу? үндсэн хэрэглэгч үү гэдгийг ялгадаг болгочих».
///
/// The rule behind it is theirs too, and it is stronger than the request:
/// «бот төлөвт байгаа юм бол бот шиг харагдах ёстой. гарсан бол гарсан.» There is
/// no mixed state to display, so there is no mixed state to compute.
///
/// 🔴 THE IDENTITY GOES ON THE MASTHEAD, NOT IN A WARNING LINE. A person should
/// never have to read a notice to find out who they are - the notice is what was
/// there, and it lost against a name in the corner.
/// </summary>
internal static class StudioActingIdentityBadge
{
    /// <summary>The tag shown beside a bot's name. Short, because it sits in a corner.</summary>
    public const string BotMark = "БОТ";

    /// <summary>
    /// What a name field says when nothing named it.
    ///
    /// 🔴 NAMED, NOT LEFT BLANK. The owner's rule about identity is that a missing
    /// name is a gap to close, not a hole to fill with an address - and a blank
    /// reads as «nobody», which is a different and wrong answer.
    /// </summary>
    public const string Unknown = "(нэр тодорхойгүй)";

    /// <param name="seatedAsBot">This machine is acting as a bot seat.</param>
    /// <param name="botDisplayName">The seat's own name, as the seat carries it.</param>
    /// <param name="ownerDisplayName">The signed-in person's name.</param>
    public static StudioActingIdentity Of(
        bool seatedAsBot,
        string? botDisplayName,
        string? ownerDisplayName)
    {
        if (seatedAsBot)
        {
            // The bot's own name, never the owner's - even when an owner is
            // signed in beside it. Showing the person's name on a seated machine
            // is precisely what cost the owner their day.
            return new StudioActingIdentity(BotMark, Named(botDisplayName), IsBot: true);
        }

        return new StudioActingIdentity("", Named(ownerDisplayName), IsBot: false);
    }

    /// <summary>
    /// A person's name, or the fact that there isn't one - never an address.
    ///
    /// 🔴 THE SAME FACT HAD TWO ANSWERS, AND ONLY ONE OBEYED THE OWNER. «What is
    /// this person called» was answered by a name on one route and by an e-mail
    /// address on another, depending on which screen asked - the fourth time
    /// today that one question has been found with two answers. The owner's rule
    /// is one sentence: «НЭРЭЭР, имэйлээр БИШ. Нэр байхгүй бол тэр нь шийдэх
    /// ёстой цоорхой, имэйлээр нөхөх шалтаг биш.»
    ///
    /// An address is not a worse name, it is a different KIND of fact - and the
    /// server proved the point by normalising accounts: on a telephone account
    /// the «address» is a row of digits. Printing that under «who» is worse than
    /// printing nothing.
    /// </summary>
    public static string NamedOrUnknown(string? value) => Named(value);

    private static string Named(string? value)
    {
        string name = (value ?? "").Trim();
        return name.Length == 0 ? Unknown : name;
    }
}
