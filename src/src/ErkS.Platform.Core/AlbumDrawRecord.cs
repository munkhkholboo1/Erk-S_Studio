namespace ErkS.Platform.Core;

/// <summary>
/// What happened the last time the album could have been drawn.
///
/// 🔴 THE POINT IS THE SKIP, NOT THE DRAW. A build leaves a file behind, with a
/// timestamp and a size; a build that was CORRECTLY SKIPPED leaves nothing at
/// all, and the only way anyone had to tell «it did not rebuild» from «it
/// rebuilt quickly» was how long it felt. That is a measurement that changes
/// with the machine and cannot be checked afterwards - and the rule it was
/// standing in for is the one that made the product usable.
///
/// 🔴 THE RECORD IS WRITTEN ON BOTH ANSWERS, and an observation that only ever
/// says «skipped» proves nothing. A change to the project must make this say
/// «drew», in so many words - that is the positive control, and it is asserted
/// rather than assumed.
/// </summary>
public sealed class AlbumDrawRecord
{
    /// <summary>
    /// When the decision was made. Null until one has been.
    ///
    /// A record with no time is «never asked», which is not the same as «asked
    /// and skipped» - the second means the rule ran.
    /// </summary>
    public DateTimeOffset? DecidedAtUtc { get; set; }

    /// <summary>Whether the album was drawn.</summary>
    public bool Drew { get; set; }

    /// <summary>
    /// The reason, as the policy's own name for it.
    ///
    /// Stored as TEXT rather than a number so an older file stays readable when
    /// the list grows, and so a reason removed from the code does not silently
    /// become a different one - a renumbering that turns «nothing changed» into
    /// «the file was missing» is exactly the kind of quiet rewrite of history
    /// this record exists to prevent.
    /// </summary>
    public string ReasonCode { get; set; } = "";

    /// <summary>
    /// When the album was last actually drawn - kept across decisions that
    /// skipped, so «it has not been redrawn since Tuesday» is answerable.
    /// </summary>
    public DateTimeOffset? LastDrewAtUtc { get; set; }

    /// <summary>How long the drawing took, when it happened.</summary>
    public double LastDrewSeconds { get; set; }

    public AlbumDrawRecord Clone() => new()
    {
        DecidedAtUtc = DecidedAtUtc,
        Drew = Drew,
        ReasonCode = ReasonCode,
        LastDrewAtUtc = LastDrewAtUtc,
        LastDrewSeconds = LastDrewSeconds,
    };

    /// <summary>
    /// Records a decision, keeping what the last DRAW left behind.
    ///
    /// 🔴 A SKIP MUST NOT ERASE THE DRAW IT SKIPPED. «Nothing changed» is the
    /// ordinary answer and it happens many times between builds; if each one
    /// cleared the last-drawn time, the record would answer «never drawn» on a
    /// project whose album is sitting on disk.
    /// </summary>
    public void Record(bool drew, string reasonCode, DateTimeOffset atUtc)
    {
        DecidedAtUtc = atUtc;
        Drew = drew;
        ReasonCode = (reasonCode ?? "").Trim();
        if (drew)
            LastDrewAtUtc = atUtc;
    }

    /// <summary>Notes how long a draw took, once it has finished.</summary>
    public void RecordDrawFinished(DateTimeOffset atUtc, double seconds)
    {
        LastDrewAtUtc = atUtc;
        LastDrewSeconds = Math.Max(0, seconds);
    }
}
