using System.Globalization;

namespace ErkS.Platform.Core;

/// <summary>
/// How long ago something happened, in Mongolian.
/// </summary>
/// <remarks>
/// Written for the presence dot: hovering a colleague who is offline says when
/// they were last connected, which is the whole reason the server sends a
/// timestamp instead of the word "Offline". A conclusion cannot be turned back
/// into a fact, so the fact travels and the phrasing happens here.
/// </remarks>
public static class MongolianRelativeTime
{
    /// <summary>
    /// A phrase like "3 цагийн өмнө", or an absolute date once "ago" stops
    /// being useful.
    /// </summary>
    /// <param name="moment">When it happened.</param>
    /// <param name="now">Now, passed in so this can be tested.</param>
    public static string Describe(DateTimeOffset moment, DateTimeOffset now)
    {
        TimeSpan elapsed = now - moment;

        // A clock a little ahead of the server's is ordinary, and "in 2
        // minutes" would read as a fault. Anything not yet past counts as now.
        if (elapsed < TimeSpan.FromMinutes(1))
            return "дөнгөж сая";

        if (elapsed < TimeSpan.FromHours(1))
            return $"{(int)elapsed.TotalMinutes} минутын өмнө";

        if (elapsed < TimeSpan.FromDays(1))
            return $"{(int)elapsed.TotalHours} цагийн өмнө";

        if (elapsed < TimeSpan.FromDays(30))
            return $"{(int)elapsed.TotalDays} өдрийн өмнө";

        // Past a month "N өдрийн өмнө" stops helping anyone picture it, and the
        // date is both shorter and more precise.
        return moment.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The presence tooltip: when this person was last connected.
    /// </summary>
    /// <remarks>
    /// "Холбогдсон" rather than "ажиллаж байсан", because what is actually
    /// known is that their Studio was open and talking to the server. Whether
    /// anyone was at the keyboard is not something this can see.
    /// </remarks>
    public static string DescribeLastSeen(DateTimeOffset? lastSeen, DateTimeOffset now) =>
        lastSeen is null
            ? "Мэдээлэл алга"
            : $"{Describe(lastSeen.Value, now)} холбогдсон";
}
