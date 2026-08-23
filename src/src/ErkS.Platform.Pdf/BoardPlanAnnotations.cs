using ErkS.Platform.Core;
using PdfSharp.Drawing;

namespace ErkS.Platform.Pdf;

/// <summary>
/// What a plan needs beside it to be read rather than merely looked at: what
/// its colours mean, which way is north, and how far anything is.
///
/// All three are drawn from what the plan itself carried, so none of them can
/// drift from the drawing they describe. And two of them refuse to be drawn at
/// all when the source only assumed the answer - a missing north arrow is
/// visible and gets fixed, while one pointing the wrong way is invisible and is
/// found by the jury.
/// </summary>
public static class BoardPlanAnnotations
{
    private const double PointsPerMm = 72.0 / 25.4;

    private static readonly XSolidBrush TextBrush = new(XColor.FromArgb(38, 42, 50));
    private static readonly XSolidBrush MutedBrush = new(XColor.FromArgb(110, 118, 130));
    private static readonly XPen HairlinePen = new(XColor.FromArgb(70, 78, 92), 0.4 * PointsPerMm);
    private static readonly XSolidBrush InkBrush = new(XColor.FromArgb(38, 42, 50));
    private static readonly XSolidBrush PaperBrush = new(XColors.White);

    /// <summary>
    /// The surfaces this plan actually shows, each with the mark it is drawn
    /// with. Never the catalogue: a legend lists what is on the board.
    /// </summary>
    public static int DrawLegend(
        XGraphics gfx,
        IReadOnlyList<PlanStyle> legend,
        XRect area,
        string title)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(legend);
        if (legend.Count == 0)
            return 0;

        double swatch = 5 * PointsPerMm;
        double pitch = 7 * PointsPerMm;
        double y = area.Top;
        var titleFont = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
        var font = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);

        if (!string.IsNullOrWhiteSpace(title))
        {
            gfx.DrawString(title, titleFont, TextBrush, new XRect(area.Left, y, area.Width, pitch),
                XStringFormats.CenterLeft);
            y += pitch;
        }

        int drawn = 0;
        foreach (PlanStyle style in legend)
        {
            if (y + swatch > area.Bottom)
                break;

            var box = new XRect(area.Left, y, swatch, swatch);
            if (BoardPlanRenderer.TryBrush(style.FillColorHex) is { } fill)
                gfx.DrawRectangle(fill, box);
            if (BoardPlanRenderer.TryColor(style.OutlineColorHex) is { } edge)
                gfx.DrawRectangle(new XPen(edge, 0.4 * PointsPerMm), box);

            gfx.DrawString(
                style.Label,
                font,
                style.IsUnrecognised ? MutedBrush : TextBrush,
                new XRect(area.Left + swatch + 3 * PointsPerMm, y, area.Width - swatch, swatch),
                XStringFormats.CenterLeft);
            y += pitch;
            drawn++;
        }

        return drawn;
    }

    /// <summary>
    /// North, at the angle the drawing declared. Returns false when it was only
    /// assumed and nobody has confirmed it: a wrongly pointed arrow is worse
    /// than none, because nothing about it looks wrong.
    /// </summary>
    public static bool DrawNorthArrow(
        XGraphics gfx,
        double northAngleDegrees,
        bool angleIsAssumed,
        bool confirmedByUser,
        XRect area)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        if (angleIsAssumed && !confirmedByUser)
            return false;

        double size = Math.Min(area.Width, area.Height);
        if (size < 8 * PointsPerMm)
            return false;

        double centreX = area.Left + area.Width / 2;
        double centreY = area.Top + area.Height / 2;
        double radius = size / 2 * 0.78;

        XGraphicsState state = gfx.Save();
        gfx.TranslateTransform(centreX, centreY);
        // The board counts angles the other way round from the drawing, which
        // counts them anticlockwise from east.
        gfx.RotateTransform(-northAngleDegrees);

        var head = new XPoint[]
        {
            new(0, -radius),
            new(radius * 0.42, radius * 0.62),
            new(0, radius * 0.24),
            new(-radius * 0.42, radius * 0.62),
        };
        gfx.DrawPolygon(
            new XPen(XColor.FromArgb(38, 42, 50), 0.35 * PointsPerMm),
            InkBrush,
            head,
            XFillMode.Alternate);
        gfx.Restore(state);

        var font = new XFont("Segoe UI", 8, XFontStyleEx.Bold);
        gfx.DrawString(
            "N",
            font,
            TextBrush,
            new XRect(area.Left, area.Bottom - 4 * PointsPerMm, area.Width, 4 * PointsPerMm),
            XStringFormats.TopCenter);
        return true;
    }

    /// <summary>
    /// A length of ground at the size it comes out on the board. Returns false
    /// when the scale is not trustworthy or no readable bar fits.
    /// </summary>
    public static bool DrawScaleBar(
        XGraphics gfx,
        double scaleDenominator,
        bool unitsAreAssumed,
        bool confirmedByUser,
        XRect area)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        if (unitsAreAssumed && !confirmedByUser)
            return false;
        if (BoardScaleBars.Choose(scaleDenominator, area.Width / PointsPerMm) is not { } plan)
            return false;

        double barHeight = Math.Min(3 * PointsPerMm, area.Height / 3);
        double length = plan.LengthMm * PointsPerMm;
        double left = area.Left;
        double top = area.Top + barHeight;
        double segment = length / plan.Segments;

        for (int index = 0; index < plan.Segments; index++)
        {
            // Alternating blocks: the oldest way of making a bar readable at a
            // glance, and it survives being photocopied.
            gfx.DrawRectangle(
                index % 2 == 0 ? InkBrush : PaperBrush,
                new XRect(left + index * segment, top, segment, barHeight));
        }
        gfx.DrawRectangle(HairlinePen, new XRect(left, top, length, barHeight));

        var font = new XFont("Segoe UI", 7.5, XFontStyleEx.Regular);
        gfx.DrawString("0", font, TextBrush,
            new XRect(left - 6, top + barHeight, 12, 4 * PointsPerMm), XStringFormats.TopCenter);
        gfx.DrawString(
            FormatGround(plan.GroundMetres),
            font,
            TextBrush,
            new XRect(left + length - 20, top + barHeight, 40, 4 * PointsPerMm),
            XStringFormats.TopCenter);
        gfx.DrawString(
            $"1:{plan.ScaleDenominator:0}",
            font,
            MutedBrush,
            new XRect(left, area.Top, area.Width, barHeight),
            XStringFormats.CenterLeft);
        return true;
    }

    private static string FormatGround(double metres) => metres >= 1000
        ? $"{metres / 1000:0.#} км"
        : $"{metres:0.#} м";
}
