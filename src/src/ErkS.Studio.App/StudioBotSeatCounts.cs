using System.Globalization;

namespace ErkS.Studio;

/// <summary>
/// How many seats a licence allows, written so a person can read it.
///
/// An unlimited licence arrives as int.MaxValue and the panel showed
/// "1 / 2147483647", which reads as a bug rather than as "no limit".
///
/// The server's contract does not name this value - deviceRights is a plain
/// int32 with no documented sentinel - so what follows is the client reading a
/// convention rather than an agreement. It is written down here so the next
/// reader knows which it is, and SRV has been asked to say it properly (a flag,
/// or a documented sentinel). Until then the reading is deliberately narrow:
/// only int.MaxValue itself means unlimited, so a large-but-real allowance is
/// still shown as the number it is.
/// </summary>
internal static class StudioBotSeatCounts
{
    public const string UnlimitedLabel = "хязгааргүй";

    public static string DescribeRights(int deviceRights) =>
        deviceRights == int.MaxValue
            ? UnlimitedLabel
            : deviceRights.ToString(CultureInfo.CurrentCulture);

    /// <summary>"эзэлсэн: 1 / 4", or "эзэлсэн: 1 / хязгааргүй".</summary>
    public static string DescribeOccupancy(int occupiedSeats, int deviceRights) =>
        occupiedSeats.ToString(CultureInfo.CurrentCulture) + " / " + DescribeRights(deviceRights);
}
