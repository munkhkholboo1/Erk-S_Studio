namespace ErkS.Platform.Core;

/// <summary>
/// What this device can say about whether a colleague is there.
/// </summary>
public enum MemberPresenceState
{
    /// <summary>
    /// Nobody has ever heard from them. Not the same as offline: they may be
    /// working right now on a version that does not report, or on a licence
    /// that was never activated.
    /// </summary>
    Unknown,

    /// <summary>Heard from within the threshold.</summary>
    Online,

    /// <summary>Heard from, but longer ago than the threshold.</summary>
    Offline,
}

/// <summary>
/// Turns a last-seen timestamp into one of three answers.
///
/// The server sends the timestamp rather than a decision, because a decision
/// made when the response was built is already stale by the time it is read,
/// and because "3 цагийн өмнө" cannot be recovered from the word "Offline".
/// The threshold arrives from the server's rules, so it can change without
/// updating anyone's Studio.
/// </summary>
public static class MemberPresence
{
    /// <summary>
    /// Used until the server's rules say otherwise. Long enough that a slow
    /// network does not blink someone offline, short enough to be believable.
    /// </summary>
    public static readonly TimeSpan DefaultOnlineWithin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often to ask the server again, given how long it counts someone as
    /// present.
    /// </summary>
    /// <remarks>
    /// Asking less often than the window means everyone drops to offline
    /// between fetches and springs back on the next one - the whole team
    /// blinking together, which is what a reader takes for an outage rather
    /// than for stale data. Half the window leaves room for one missed
    /// request.
    /// </remarks>
    public static TimeSpan RefreshInterval(TimeSpan onlineWithin, TimeSpan? requested = null)
    {
        TimeSpan window = onlineWithin > TimeSpan.Zero ? onlineWithin : DefaultOnlineWithin;
        TimeSpan interval = requested is { } asked && asked > TimeSpan.Zero
            ? asked
            : TimeSpan.FromSeconds(window.TotalSeconds / 2);

        return interval >= window
            ? TimeSpan.FromSeconds(window.TotalSeconds / 2)
            : interval;
    }

    public static MemberPresenceState Resolve(
        DateTimeOffset? lastSeen,
        DateTimeOffset now,
        TimeSpan? onlineWithin = null)
    {
        // No timestamp is not evidence of absence. Painting these people red
        // would state something nobody knows - the failure this whole change
        // exists to remove.
        if (lastSeen is null)
            return MemberPresenceState.Unknown;

        TimeSpan window = onlineWithin ?? DefaultOnlineWithin;
        if (window <= TimeSpan.Zero)
            window = DefaultOnlineWithin;

        // A clock slightly ahead of ours reads as a negative age; that is a
        // clock, not a person from the future, and they are plainly present.
        TimeSpan age = now - lastSeen.Value;
        return age <= window ? MemberPresenceState.Online : MemberPresenceState.Offline;
    }
}
