namespace ErkS.Studio;

/// <summary>Why a machine may not be turned into a bot, and what to do about it.</summary>
public enum BotSeatingRefusal
{
    /// <summary>Nothing in the way.</summary>
    None,

    /// <summary>One device, one seat.</summary>
    AlreadySeated,

    /// <summary>Seats belong to the licence owner, so an owner session is needed.</summary>
    OwnerNotSignedIn,

    /// <summary>
    /// The device key is not registered with the server. This one is fatal
    /// AFTERWARDS rather than now, which is why it has to be caught here.
    /// </summary>
    DeviceKeyNotRegistered,
}

/// <summary>
/// What must be true before this machine becomes a bot.
///
/// The device key requirement is the reason this exists. A seated machine has
/// no Cloud ERA session left - entering bot state erases the owner credential -
/// so it cannot register a key afterwards, and without a registered key it
/// cannot prove itself to the server after a restart. The seat then needs the
/// owner to release it and start again. Every other check here can be repaired
/// by the person in front of the screen; that one cannot.
///
/// It lives outside the shell because that is where it went wrong: registration
/// was a side effect of EnsureSignedInAsync, which returns early when somebody
/// is already signed in - so a person who seated a device without signing in
/// afresh skipped it entirely, silently, and only found out after a restart.
/// A requirement that rides along inside another step is a requirement nothing
/// can measure.
/// </summary>
public static class StudioBotSeatingRequirements
{
    public static BotSeatingRefusal Check(
        bool alreadySeated,
        bool ownerSignedIn,
        bool deviceKeyRegistered)
    {
        if (alreadySeated)
            return BotSeatingRefusal.AlreadySeated;
        if (!ownerSignedIn)
            return BotSeatingRefusal.OwnerNotSignedIn;
        if (!deviceKeyRegistered)
            return BotSeatingRefusal.DeviceKeyNotRegistered;
        return BotSeatingRefusal.None;
    }

    /// <summary>
    /// The refusal in the words the person needs: what happened, and what to do
    /// next. "Болсонгүй" is not something anyone can act on.
    /// </summary>
    public static string Describe(BotSeatingRefusal refusal) => refusal switch
    {
        BotSeatingRefusal.AlreadySeated =>
            "Энэ төхөөрөмж аль хэдийн ботын суудалтай байна. " +
            "Эхлээд ботын төлөвөөс гаргана уу.",
        BotSeatingRefusal.OwnerNotSignedIn =>
            "Ботын суудлыг зөвхөн лиценз эзэмшигч үүсгэнэ. " +
            "Эзэмшигчийн бүртгэлээр нэвтэрнэ үү.",
        BotSeatingRefusal.DeviceKeyNotRegistered =>
            "Энэ төхөөрөмжийн түлхүүр сервер дээр бүртгэгдээгүй байна. " +
            "Бүртгэлээсээ гарч дахин нэвтэрвэл түлхүүр бүртгэгдэнэ — " +
            "суудалтай болсны дараа үүнийг засах боломжгүй, эзэмшигч суудлыг " +
            "чөлөөлж дахин эхлэхээс өөр зам үлдэхгүй.",
        _ => "",
    };
}
