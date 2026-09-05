namespace ErkS.Platform.Core;

/// <summary>
/// One administrative unit of Mongolia, as the national statistics office
/// publishes it. Aimag/capital, sum/district, bag/khoroo - three levels, nested
/// by code prefix.
/// </summary>
/// <param name="UnitCode">3, 5 or 7 digits; the parent is its prefix.</param>
/// <param name="Level">Aimag, Capital, Sum, District, Bag or Khoroo.</param>
/// <param name="ParentUnitCode">Empty at the top level.</param>
/// <param name="NameMn">The unit's own name, as printed.</param>
/// <param name="ChildPickerLabelMn">
/// What to call the NEXT picker - «Сум», «Дүүрэг», «Баг», «Хороо». It comes
/// from the data, never from a branch in this code.
/// </param>
public sealed record AdministrativeUnit(
    string UnitCode,
    string Level,
    string ParentUnitCode,
    string NameMn,
    string ChildPickerLabelMn)
{
    public bool HasChildren => ChildPickerLabelMn.Length > 0;
}

/// <summary>
/// Reading the published catalogue, and the two traps in it.
///
/// FIRST: the label of the next level is NOT derivable from "is this a city".
/// Erdenet and Darkhan are cities and their children are called «баг», not
/// «хороо» - measured on the real rows. Any code that writes `if (capital)
/// "Хороо" else "Баг"` is wrong for two of the country's largest settlements,
/// and wrong in a way that looks right everywhere it was tested.
///
/// SECOND: there is a third word. One row in Khövsgöl reads «Хатгал тосгон» -
/// a tosgon, neither bag nor khoroo. So the PICKER'S HEADING and the UNIT'S OWN
/// NAME are separate facts: the heading says «Баг» because its parent says so,
/// and the name says what the place is actually called.
///
/// Both are why the label travels in the data.
/// </summary>
public static class AdministrativeUnits
{
    public const string Aimag = "Aimag";
    public const string Capital = "Capital";
    public const string Sum = "Sum";
    public const string District = "District";
    public const string Bag = "Bag";
    public const string Khoroo = "Khoroo";

    /// <summary>
    /// Rows with fewer digits than a real unit are TOTALS - "Улсын дүн",
    /// regional sums - and must never reach a picker. Filtering by code length
    /// is what keeps "the national total" out of the list of places a building
    /// can stand in.
    /// </summary>
    public static bool IsSelectableUnit(string? unitCode)
    {
        string code = (unitCode ?? "").Trim();
        return code.Length is 3 or 5 or 7 && code.All(char.IsDigit);
    }

    /// <summary>
    /// The parent's code, DERIVED from the prefix - a second opinion, not the
    /// first.
    ///
    /// The catalogue publishes `parentUnitCode` for exactly this reason: the
    /// prefix rule is derivable, and two readers who each derive it can derive
    /// it differently. So the chain is checked against the units' own published
    /// parents wherever the units are in hand (see <see cref="ChainIsConsistent"/>),
    /// and this prefix rule is used where only codes survive - in a stored
    /// project, which keeps codes and names but not the catalogue rows they
    /// came from.
    ///
    /// Having both is the point: they are independent, so a disagreement is a
    /// finding rather than a silent choice between them.
    /// </summary>
    public static string ParentCodeOf(string? unitCode)
    {
        string code = (unitCode ?? "").Trim();
        return code.Length switch
        {
            7 => code[..5],
            5 => code[..3],
            _ => "",
        };
    }

    /// <summary>
    /// Whether a chosen province → district → ward chain holds together,
    /// checked against the units' OWN published parents rather than against
    /// their codes.
    ///
    /// This is the check that runs while the catalogue rows are in hand. The
    /// stored project keeps only codes, so it falls back to the prefix rule -
    /// two independent statements of the same relationship, which is why a
    /// disagreement between them means something.
    /// </summary>
    public static bool ChainIsConsistent(
        AdministrativeUnit? province,
        AdministrativeUnit? district,
        AdministrativeUnit? ward)
    {
        if (province is null || district is null || ward is null)
            return false;
        return district.ParentUnitCode.Equals(province.UnitCode, StringComparison.Ordinal) &&
            ward.ParentUnitCode.Equals(district.UnitCode, StringComparison.Ordinal);
    }
}

/// <summary>
/// How the units are ORDERED in a picker, which is neither by code nor by plain
/// text.
///
/// By code fails because the codes are issue order, not reading order: Erdenet's
/// «26-р баг» is the second row of its sum. Plain text fails for the reason
/// worth writing down, because the first example anyone reaches for is the
/// wrong one:
///
///   "2-р баг" against "26-р баг"   plain text is RIGHT here - '-' (45) sorts
///                                  before '6' (54), so 2 comes first anyway
///   "10-р баг" against "2-р баг"   plain text is WRONG - '1' sorts before '2',
///                                  so the tenth bag heads the list
///
/// The second case breaks under every comparer, ordinal or culture-aware, which
/// is what makes it the example to test with. The first was offered as the
/// reason and does not hold; the conclusion survived the correction, the
/// justification did not.
/// </summary>
public sealed class AdministrativeUnitNameComparer : IComparer<string>
{
    public static AdministrativeUnitNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        string a = (left ?? "").Trim();
        string b = (right ?? "").Trim();
        int i = 0;
        int j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int startA = i;
                int startB = j;
                while (i < a.Length && char.IsDigit(a[i]))
                    i++;
                while (j < b.Length && char.IsDigit(b[j]))
                    j++;

                // Compared as NUMBERS, so ten follows two.
                long numberA = long.Parse(a[startA..i], System.Globalization.CultureInfo.InvariantCulture);
                long numberB = long.Parse(b[startB..j], System.Globalization.CultureInfo.InvariantCulture);
                if (numberA != numberB)
                    return numberA < numberB ? -1 : 1;
                continue;
            }

            int letters = string.Compare(
                a[i].ToString(),
                b[j].ToString(),
                StringComparison.CurrentCulture);
            if (letters != 0)
                return letters;
            i++;
            j++;
        }

        return (a.Length - i).CompareTo(b.Length - j);
    }
}
