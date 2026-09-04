using System.Globalization;

namespace ErkS.Studio;

/// <summary>
/// How many seats a licence allows, written so a person can read it.
///
/// An unlimited licence carries int.MaxValue in deviceRights, and the panel
/// showed "1 / 2147483647" - which reads as a bug rather than as "no limit".
///
/// THE FLAG DECIDES, NOT THE NUMBER. The server now publishes
/// deviceRightsUnlimited beside the count (SRV e7f62a0); this client had been
/// inferring the meaning from int.MaxValue itself, and an inferred meaning is
/// one each client infers differently - the platform already carries three
/// conventions for this same idea (a licence response where MaxDevices=0 means
/// unlimited, a dashboard row that says so in words, and this sentinel).
///
/// A server that predates the flag sends nothing, so the flag is false and the
/// raw number is shown. That is the honest answer - that server never said
/// "unlimited" - and the sentinel guess is deliberately NOT kept as a fallback,
/// because a fallback is how the fourth convention would start.
/// </summary>
internal static class StudioBotSeatCounts
{
    public const string UnlimitedLabel = "хязгааргүй";

    public static string DescribeRights(int deviceRights, bool deviceRightsUnlimited) =>
        deviceRightsUnlimited
            ? UnlimitedLabel
            : deviceRights.ToString(CultureInfo.CurrentCulture);

    /// <summary>"1 / 4", эсвэл "1 / хязгааргүй".</summary>
    public static string DescribeOccupancy(
        int occupiedSeats,
        int deviceRights,
        bool deviceRightsUnlimited) =>
        occupiedSeats.ToString(CultureInfo.CurrentCulture) +
        " / " +
        DescribeRights(deviceRights, deviceRightsUnlimited);
}
