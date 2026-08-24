namespace ErkS.Platform.Core;

/// <summary>
/// Fits something of a known shape inside bounds without cropping it.
/// </summary>
/// <remarks>
/// Written for the project cover preview, which used to fill its frame and cut
/// off whatever hung over the edge. The frame assumed A4 portrait while album
/// pages come in whatever format the template asks for, so a landscape sheet
/// lost its sides. Fitting rather than filling is the difference between "the
/// page, smaller" and "part of the page".
/// </remarks>
public static class ScaledFit
{
    /// <summary>
    /// The largest box with the source's proportions that fits inside the
    /// bounds, or null when the source has no measurable size yet.
    /// </summary>
    public static (double Width, double Height)? Within(
        double sourceWidth,
        double sourceHeight,
        double maxWidth,
        double maxHeight)
    {
        // An image that has not reported its size yet, or bounds of zero, would
        // otherwise divide by zero and collapse whatever is being sized. The
        // caller decides what to show when the shape is unknown; guessing one
        // here would be the same mistake in a different place.
        if (!IsUsable(sourceWidth) || !IsUsable(sourceHeight) ||
            !IsUsable(maxWidth) || !IsUsable(maxHeight))
        {
            return null;
        }

        double scale = Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight);
        return (sourceWidth * scale, sourceHeight * scale);
    }

    private static bool IsUsable(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
