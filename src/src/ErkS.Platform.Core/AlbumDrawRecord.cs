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

    /// <summary>
    /// Copies the last sweep of the visualisation store removed.
    ///
    /// 🔴 THE DELETION HAS TO LEAVE A MARK THAT OUTLIVES THE SESSION. The
    /// sweep removes the owner's own files; if one day it removes a wrong one,
    /// this is the only place that will say how many went and how much came
    /// back. A number held in memory until the next restart is not a trace.
    /// </summary>
    public int LastSweepRemovedCount { get; set; }

    /// <summary>How much disk the last sweep gave back.</summary>
    public long LastSweepRemovedBytes { get; set; }

    /// <summary>When that sweep ran. Null until one has removed something.</summary>
    public DateTimeOffset? LastSweptAtUtc { get; set; }

    /// <summary>
    /// Why the most recent sweep refused, empty when it ran.
    ///
    /// 🔴 KEPT SEPARATELY FROM THE NUMBERS BECAUSE IT EXPIRES DIFFERENTLY. A
    /// removal is history and stays; a refusal describes the state of the
    /// project NOW, so a later healthy sweep must clear it. A stale refusal on
    /// screen would be a confident false statement about today.
    /// </summary>
    public string LastSweepRefusalMn { get; set; } = "";

    /// <summary>
    /// Visualisation images brought down to the album's density by the last pass
    /// that actually did so. History, like the sweep's numbers: a later build that
    /// only reuses the prepared copies has nothing to say and must not erase this.
    /// </summary>
    public int LastPreparedImageCount { get; set; }

    /// <summary>When that preparation happened.</summary>
    public DateTimeOffset? LastPreparedAtUtc { get; set; }

    /// <summary>
    /// Images that went into the album AT SOURCE SIZE because preparing them failed.
    ///
    /// 🔴 A DESCRIPTION OF THE ALBUM AS IT STANDS, NOT HISTORY - so unlike the
    /// count above it is rewritten on every recorded pass, including down to zero.
    /// One unreadable render must not cost the owner the whole album, but they are
    /// owed the sentence saying their album is heavier than the rule, and by how many.
    /// </summary>
    public int LastUnpreparedImageCount { get; set; }

    public AlbumDrawRecord Clone() => new()
    {
        DecidedAtUtc = DecidedAtUtc,
        Drew = Drew,
        ReasonCode = ReasonCode,
        LastDrewAtUtc = LastDrewAtUtc,
        LastDrewSeconds = LastDrewSeconds,
        LastSweepRemovedCount = LastSweepRemovedCount,
        LastSweepRemovedBytes = LastSweepRemovedBytes,
        LastSweptAtUtc = LastSweptAtUtc,
        LastSweepRefusalMn = LastSweepRefusalMn,
        LastPreparedImageCount = LastPreparedImageCount,
        LastPreparedAtUtc = LastPreparedAtUtc,
        LastUnpreparedImageCount = LastUnpreparedImageCount,
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

    /// <summary>
    /// Notes what a sweep of the visualisation store did.
    ///
    /// 🔴 A SWEEP THAT REMOVED NOTHING MUST NOT ERASE THE ONE THAT DID -
    /// the same hazard <see cref="Record"/> already guards for the draw. Sweeps
    /// run on every reconciling build and almost all of them find nothing; if
    /// each one cleared the numbers, the record of the deletion would survive
    /// exactly until the next build, which is to say not at all.
    ///
    /// The refusal is the opposite and is always written: it is a statement
    /// about the project as it stands, and yesterday's refusal shown today
    /// would be a confident lie.
    /// </summary>
    public void RecordStoreSweep(
        int removedCount,
        long removedBytes,
        string? refusalMn,
        DateTimeOffset atUtc)
    {
        LastSweepRefusalMn = (refusalMn ?? "").Trim();
        if (removedCount <= 0)
            return;

        LastSweepRemovedCount = removedCount;
        LastSweepRemovedBytes = Math.Max(0, removedBytes);
        LastSweptAtUtc = atUtc;
    }

    /// <summary>
    /// Notes what bringing the visualisation images to the album's density did.
    ///
    /// The two numbers have opposite lifetimes, for the same reason as the sweep's:
    /// «I prepared 26 images» is something that happened and stays; «2 images are in
    /// there at source size» describes the album sitting on disk right now, and has
    /// to be able to go back to zero.
    /// </summary>
    public void RecordRasterPreparation(
        int preparedCount,
        int unpreparedCount,
        DateTimeOffset atUtc)
    {
        LastUnpreparedImageCount = Math.Max(0, unpreparedCount);
        if (preparedCount <= 0)
            return;

        LastPreparedImageCount = preparedCount;
        LastPreparedAtUtc = atUtc;
    }
}
