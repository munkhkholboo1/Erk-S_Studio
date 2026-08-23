namespace ErkS.Platform.Core;

/// <summary>
/// A scale bar: a length of ground, drawn at the size it comes out on the
/// board, divided so it can be read against.
///
/// The ground length is chosen rather than given. A bar has to end on a number
/// a person can hold in their head - fifty metres, two hundred - because it is
/// read by eye and compared by eye. A bar labelled 137 m is arithmetic, not a
/// scale.
/// </summary>
public readonly record struct BoardScaleBarPlan(
    double GroundMetres,
    double LengthMm,
    int Segments,
    double ScaleDenominator)
{
    public double SegmentGroundMetres => GroundMetres / Segments;

    public double SegmentLengthMm => LengthMm / Segments;
}

public static class BoardScaleBars
{
    /// <summary>Below this a bar is too short to read or to divide.</summary>
    private const double SmallestBarMm = 15;

    /// <summary>The share of the space offered that a bar aims to fill.</summary>
    private const double TargetShare = 0.7;

    /// <summary>The only ground lengths a bar is allowed to end on.</summary>
    private static readonly double[] NiceSteps = [1, 2, 5];

    /// <summary>
    /// The bar for a plan drawn at this scale, in the width available, or null
    /// when no readable bar fits.
    ///
    /// Returning nothing is a real answer: a card too small for a scale bar
    /// should carry no bar, rather than an unreadable one that still claims to
    /// be measured.
    /// </summary>
    public static BoardScaleBarPlan? Choose(double scaleDenominator, double availableWidthMm)
    {
        if (!double.IsFinite(scaleDenominator) || scaleDenominator <= 0 ||
            !double.IsFinite(availableWidthMm) || availableWidthMm < SmallestBarMm)
        {
            return null;
        }

        // 1 mm of board is scaleDenominator mm of ground.
        double metresPerMm = scaleDenominator / 1000;
        double targetGround = availableWidthMm * TargetShare * metresPerMm;
        if (!double.IsFinite(targetGround) || targetGround <= 0)
            return null;

        double ground = LargestNiceLengthAtMost(targetGround);
        if (ground <= 0)
            return null;

        double lengthMm = ground / metresPerMm;
        if (lengthMm < SmallestBarMm || lengthMm > availableWidthMm)
            return null;

        return new BoardScaleBarPlan(ground, lengthMm, SegmentsFor(ground), scaleDenominator);
    }

    /// <summary>The largest of 1, 2, 5 times a power of ten that fits.</summary>
    private static double LargestNiceLengthAtMost(double metres)
    {
        double best = 0;
        int lowest = (int)Math.Floor(Math.Log10(metres)) - 1;
        for (int power = lowest; power <= lowest + 3; power++)
        {
            double scale = Math.Pow(10, power);
            foreach (double step in NiceSteps)
            {
                double candidate = step * scale;
                if (candidate <= metres && candidate > best)
                    best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// How many divisions the bar carries. Chosen so each one also lands on a
    /// number worth printing: a bar of 200 m in four parts is 50 m a division,
    /// while one of 500 m in four would be 125.
    /// </summary>
    private static int SegmentsFor(double groundMetres)
    {
        double mantissa = groundMetres / Math.Pow(10, Math.Floor(Math.Log10(groundMetres)));
        return Math.Abs(mantissa - 5) < 0.001 ? 5 : 4;
    }
}
