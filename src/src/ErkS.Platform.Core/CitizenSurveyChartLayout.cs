namespace ErkS.Platform.Core;

/// <summary>One bar of a results chart, measured and ready to draw.</summary>
/// <param name="LengthPx">How long the bar is drawn. Never longer than the track.</param>
/// <param name="LabelOutside">
/// Whether the value has to sit past the bar's end because it will not fit inside it.
/// </param>
public readonly record struct CitizenSurveyBarMark(
    string OptionId,
    string Label,
    int Count,
    double Share,
    double LengthPx,
    bool LabelOutside);

/// <summary>
/// The geometry of the results charts.
///
/// 🔴 IT LIVES HERE AND NOT IN THE WINDOW BECAUSE IT IS A RULE. A bar length that is
/// computed while assembling a WPF panel cannot be tested, and the two mistakes it can
/// make - overflowing the track, and scaling two questions differently so they cannot be
/// compared - are both invisible in a screenshot of plausible data.
///
/// 🔴 THE TRACK IS «EVERYONE WHO ANSWERED THIS QUESTION», NOT THE LARGEST BAR. Scaling to
/// the biggest answer makes the winner full-width in every question, so a question where
/// one option took 90% and one where it took 30% draw identically - and the reader
/// compares them side by side. Scaling to those who answered keeps the bars comparable
/// down the page, and cannot overflow: one person's answer is counted once per option, so
/// no option can exceed the number of people who answered.
///
/// ⚠ WHICH IS ALSO WHY A MULTIPLE-CHOICE QUESTION IS SAFE HERE even though its shares add
/// past 100%: each individual share is still at most 1. The sum is what exceeds a whole,
/// not any single bar - so the totals line must say so, and the bars need no special case.
/// </summary>
public static class CitizenSurveyChartLayout
{
    /// <summary>A bar is capped rather than filling its row; the leftover is air.</summary>
    public const double MaxBarThicknessPx = 24;

    /// <summary>Rounded at the data end, square at the baseline.</summary>
    public const double BarEndCornerRadiusPx = 4;

    /// <summary>The surface-coloured gap that separates touching marks.</summary>
    public const double SurfaceGapPx = 2;

    /// <summary>Padding a value needs on each side to be allowed to sit inside a bar.</summary>
    public const double InsideLabelPaddingPx = 8;

    /// <summary>
    /// The bars for one question, in the order the options are offered.
    ///
    /// Kept in the survey's own option order rather than sorted by size: the citizen read
    /// them in that order, and a chart that reorders them makes the paper form and the
    /// result impossible to read side by side.
    /// </summary>
    /// <param name="trackWidthPx">The full width a 100% bar would occupy.</param>
    /// <param name="labelWidthPx">
    /// How wide the rendered value text is. Measured by the caller, because only the
    /// window knows the font - a rule that guessed would clip text on somebody's machine.
    /// </param>
    public static IReadOnlyList<CitizenSurveyBarMark> Bars(
        CitizenSurveyQuestionTally? tally,
        double trackWidthPx,
        Func<CitizenSurveyOptionTally, double> labelWidthPx)
    {
        ArgumentNullException.ThrowIfNull(labelWidthPx);
        if (tally is null || tally.Options.Count == 0)
            return [];

        double track = double.IsFinite(trackWidthPx) && trackWidthPx > 0 ? trackWidthPx : 0;

        var bars = new List<CitizenSurveyBarMark>(tally.Options.Count);
        foreach (CitizenSurveyOptionTally option in tally.Options)
        {
            double share = Math.Clamp(option.Share, 0d, 1d);
            double length = track * share;
            double label = Math.Max(0d, labelWidthPx(option));

            // ⚠ MEASURED, NOT ASSUMED. A value that does not fit inside its bar is moved
            // past the end rather than clipped - cropping the first characters of «128»
            // is worse than no label at all, and on a short bar it is the common case.
            bool outside = length < label + (InsideLabelPaddingPx * 2);

            bars.Add(new CitizenSurveyBarMark(
                option.OptionId,
                option.Text,
                option.Count,
                share,
                length,
                outside));
        }

        return bars;
    }

    /// <summary>
    /// Bar thickness for a row of the given height - capped, with the leftover left as air.
    /// </summary>
    public static double BarThickness(double rowHeightPx)
    {
        if (!double.IsFinite(rowHeightPx) || rowHeightPx <= 0)
            return 0;
        return Math.Min(MaxBarThicknessPx, Math.Max(0d, rowHeightPx - SurfaceGapPx));
    }

    /// <summary>
    /// The buckets of a written-number question, for a small histogram.
    ///
    /// ⚠ WHOLE NUMBERS, BECAUSE THE QUESTION COUNTS PEOPLE. «Ам бүлийн тоо» is answered
    /// with 4, not 4.2, so a bucket per value reads exactly - and a household of 11 keeps
    /// its own column instead of being averaged into invisibility.
    /// </summary>
    public static IReadOnlyList<(int Value, int Count)> NumberBuckets(
        IEnumerable<double>? numbers)
    {
        var counts = new SortedDictionary<int, int>();
        foreach (double number in numbers ?? [])
        {
            if (!double.IsFinite(number))
                continue;
            int bucket = (int)Math.Round(number, MidpointRounding.AwayFromZero);
            counts[bucket] = counts.GetValueOrDefault(bucket) + 1;
        }

        return counts.Select(pair => (pair.Key, pair.Value)).ToList();
    }
}
