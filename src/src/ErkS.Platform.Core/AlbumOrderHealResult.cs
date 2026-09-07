namespace ErkS.Platform.Core;

/// <summary>
/// What happened when the stored page order was put back in step.
///
/// 🔴 "DID NOT RUN" AND "RAN AND NOTHING MOVED" ARE DIFFERENT ANSWERS, and the
/// bool this replaces could not tell them apart. The heal guards itself out on
/// an unfilled library - correctly, because ordering on no information is how a
/// person's data gets rewritten with nothing - and that produced the same
/// `false` as a project already in perfect order. The report then had to print
/// one of them as the other.
///
/// <see cref="PageCount"/> is carried even when nothing moved, because a number
/// that stays zero is a measurement and a silence is not.
/// </summary>
public readonly record struct AlbumOrderHealResult(bool Ran, int PageCount, int MovedCount)
{
    /// <summary>The order was actually corrected.</summary>
    public bool Changed => Ran && MovedCount > 0;

    /// <summary>Nothing was examined - no library, or no pages.</summary>
    public static AlbumOrderHealResult NotRun => new(false, 0, 0);
}
