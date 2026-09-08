namespace ErkS.Platform.Core;

/// <summary>
/// The address a sheet prints, composed from the chosen location and whatever
/// the person typed beside it.
///
/// 🔴 THE CHOICE IS THE SOURCE; THE TYPED LINE IS AN ADDITION. The user said it
/// plainly - «эндээс байршлаа сонгоход төслийн хаяг өөрөө үүсэх ёстой. гэтэл
/// давхар байх шаардлага юм байна вэ?» - and they are right: aimag, sum and bag
/// already say where the site is. A second field holding the same thing is not
/// redundancy, it is a SECOND SOURCE OF TRUTH, and the first time the two
/// disagree nobody can say which one the drawing should believe.
///
/// So there is one rule and it has no branches for taste: a chosen location
/// composes the address, and the typed field carries only what a location
/// CANNOT say - a street, a building number, a landmark.
///
/// WHAT IS DELIBERATELY NOT DONE. The typed text of an existing project is not
/// parsed into codes. «…34-р хороо, Баянгол» would parse three levels and leave
/// a fourth word nobody asked about, and names repeat across the country - 31%
/// of them - so a parse would assign the wrong unit sooner rather than later.
/// The old text stays exactly as written, in the additional field, and the
/// person is told it moved.
/// </summary>
public static class ProjectSiteAddress
{
    /// <summary>
    /// What to print. Empty only when there is genuinely nothing.
    /// </summary>
    /// <param name="location">The chosen aimag / sum / bag, if it is complete.</param>
    /// <param name="additional">
    /// The street and building line - or, on a project that predates the picker,
    /// the whole address as somebody typed it.
    /// </param>
    public static string Compose(ProjectSiteLocation? location, string? additional)
    {
        string extra = (additional ?? "").Trim();

        // 🔴 NOT GATED ON IsChosen. A half-made choice - a province and nothing
        // else - still names a real place, and refusing to print it would lose
        // information the person deliberately entered. IsChosen answers "is
        // this complete enough to reason about", which is a different question
        // from "is there anything to print".
        string chosen = location?.CoverLine() ?? "";

        if (chosen.Length == 0)
        {
            // No usable choice: the typed line IS the address. This is every
            // project that existed before the picker, and it must keep printing
            // exactly what it printed yesterday.
            return extra;
        }

        // 🔴 THE ADDITION NEVER REPLACES THE CHOICE. Master's condition, and the
        // reason is the disagreement case: if a typed line could override the
        // chosen units, then the sheet and the roster suggestions would read
        // different places from the same project.
        return extra.Length == 0 ? chosen : chosen + ", " + extra;
    }

    /// <summary>
    /// The short form, for a list column: district and ward, without the
    /// province and WITHOUT the typed line.
    ///
    /// The typed line is left out on purpose - it is a street and a building
    /// number, which is precisely what does not fit a column and does not help
    /// somebody scanning a list of projects.
    /// </summary>
    public static string ComposeShort(ProjectSiteLocation? location, string? additional)
    {
        string shortLine = location?.ShortLine() ?? "";
        return shortLine.Length > 0 ? shortLine : (additional ?? "").Trim();
    }

    /// <summary>
    /// Whether this project's typed address is about to become the ADDITIONAL
    /// line because a location has been chosen for the first time.
    ///
    /// The person typed that text and must be told it moved - a field quietly
    /// changing meaning under somebody is how they stop trusting the ones that
    /// did not.
    /// </summary>
    public static bool TypedAddressBecomesAdditional(
        ProjectSiteLocation? locationBefore,
        ProjectSiteLocation? locationAfter,
        string? typedAddress) =>
        (typedAddress ?? "").Trim().Length > 0 &&
        locationBefore is not { IsChosen: true } &&
        locationAfter is { IsChosen: true };

    /// <summary>What to tell them when it does.</summary>
    public const string TypedAddressMovedNoticeMn =
        "Таны бичсэн хаяг «Нэмэлт хаяг» болж шилжлээ — хаягийн эхний хэсэг " +
        "одоо сонгосон байршлаас үүснэ. Давхардсан хэсгийг нь гараар хасна уу.";
}
