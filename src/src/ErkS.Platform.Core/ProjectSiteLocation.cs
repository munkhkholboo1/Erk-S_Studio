namespace ErkS.Platform.Core;

/// <summary>
/// Where the site is, CHOSEN rather than typed: aimag or capital, then sum or
/// district, then bag or khoroo.
///
/// It sits BESIDE the free-text address, which is kept exactly as it was. The
/// old text is never parsed into these fields, and that is a rule rather than a
/// convenience: names repeat across the country, so guessing a unit from a
/// typed line is a guaranteed way to record the wrong one now and then.
///
/// What makes a wrong guess expensive here is that this is not only an address.
/// The concurring-body suggestion reads it, so a misread aimag puts a DIFFERENT
/// ORGANISATION'S NAME on a signed document - the error does not stay in the
/// address field it started in.
///
/// CODE AND NAME ARE BOTH STORED, and for different reasons. The code is what
/// matches; matching on names is what put «Erk-S Стандарт» and «Erk-S Standard»
/// on opposite sides of a comparison. The name is what PRINTS, and it is a
/// snapshot: administrative units get renamed, and an album that was issued
/// under the old name must keep saying the old name.
/// </summary>
public sealed class ProjectSiteLocation
{
    /// <summary>Aimag or the capital. Three digits.</summary>
    public string ProvinceCode { get; set; } = "";

    public string ProvinceName { get; set; } = "";

    /// <summary>Sum or district. Five digits.</summary>
    public string DistrictCode { get; set; } = "";

    public string DistrictName { get; set; } = "";

    /// <summary>Bag, khoroo - or, in one place, a tosgon. Seven digits.</summary>
    public string WardCode { get; set; } = "";

    public string WardName { get; set; } = "";

    /// <summary>
    /// The heading the ward level was chosen under - «Баг» or «Хороо» - copied
    /// from the catalogue at the time of choosing.
    ///
    /// Stored rather than re-derived because it cannot be worked out from what
    /// is here: Erdenet is a city whose wards are called «баг», so no rule of
    /// the form "capital means khoroo" is correct. Re-deriving it later would
    /// have to guess, and guessing renames a place on a printed sheet.
    /// </summary>
    public string WardLabelMn { get; set; } = "";

    /// <summary>
    /// Which version of the catalogue this was chosen from. Issued by the
    /// server, repeated by Studio, never invented here.
    ///
    /// Without it, a choice made from a three-month-old offline cache is
    /// indistinguishable from one made a second ago against live data - both
    /// would simply say "chosen today", and a wrong unit could not be explained.
    /// </summary>
    public DateTimeOffset? CatalogueAsOfUtc { get; set; }

    /// <summary>
    /// Whether a complete location has been chosen. A partial choice is not
    /// half an answer - the suggestion rules and the cover line both need all
    /// three levels, so anything less behaves exactly as nothing.
    ///
    /// 🔴 IT ALSO REQUIRES THE CHAIN TO HOLD TOGETHER, and this is where the
    /// protection lives now.
    ///
    /// It used to live in <see cref="Normalize"/>, which cleared the offending
    /// codes - except that nothing in the program ever calls Normalize on a
    /// location. So the rule was written, tested, green, and unreachable: a
    /// stored chain that does not hold together was loaded exactly as it was and
    /// PRINTED. Moving it here is what makes it apply whether or not anybody
    /// remembers to call something.
    /// </summary>
    public bool IsChosen =>
        ProvinceCode.Length > 0 &&
        DistrictCode.Length > 0 &&
        WardCode.Length > 0 &&
        ChainHoldsTogether;

    /// <summary>
    /// Whether each level sits under the one above it, by code.
    ///
    /// The check is the PREFIX rule, which is this side's own - the catalogue's
    /// authority is `parentUnitCode`, and SRV states plainly that they do not
    /// enforce agreement between the two.
    ///
    /// CHECKED 2026-09-06: all 2217 published rows satisfy both, measured on
    /// SRV's side, and their own test goes red before a differently-nested
    /// source could reach here. The date is part of the claim rather than
    /// decoration - a fact about the other side of a boundary that does not say
    /// when it was last true is one somebody will build on permanently, which is
    /// how a stale statement of mine ended up quoted in SRV's assertion text.
    /// </summary>
    public bool ChainHoldsTogether =>
        (DistrictCode.Length == 0 ||
            AdministrativeUnits.ParentCodeOf(DistrictCode).Equals(ProvinceCode, StringComparison.Ordinal)) &&
        (WardCode.Length == 0 ||
            AdministrativeUnits.ParentCodeOf(WardCode).Equals(DistrictCode, StringComparison.Ordinal));

    /// <summary>
    /// What is wrong with the stored chain, in the reader's language. Empty when
    /// nothing is.
    ///
    /// It exists because the alternative was DELETION. A record whose levels
    /// disagree was written by two different things, and it must not be acted on
    /// - but throwing away what somebody stored is not the way to stop acting on
    /// it. The values stay, the chain refuses to be used, and this says why.
    /// </summary>
    public string ProblemMn
    {
        get
        {
            if (DistrictCode.Length > 0 &&
                !AdministrativeUnits.ParentCodeOf(DistrictCode).Equals(ProvinceCode, StringComparison.Ordinal))
            {
                return "Хадгалсан хаяг зөрчилтэй: «" + DistrictName + "» (" + DistrictCode +
                    ") нь «" + ProvinceName + "» (" + ProvinceCode + ")-д харьяалагдахгүй байна. " +
                    "Утга нь хэвээр хадгалагдсан; хаягийг дахин сонгоно уу.";
            }

            if (WardCode.Length > 0 &&
                !AdministrativeUnits.ParentCodeOf(WardCode).Equals(DistrictCode, StringComparison.Ordinal))
            {
                return "Хадгалсан хаяг зөрчилтэй: «" + WardName + "» (" + WardCode +
                    ") нь «" + DistrictName + "» (" + DistrictCode + ")-д харьяалагдахгүй байна. " +
                    "Утга нь хэвээр хадгалагдсан; хаягийг дахин сонгоно уу.";
            }

            return "";
        }
    }

    public ProjectSiteLocation Clone() => new()
    {
        ProvinceCode = ProvinceCode,
        ProvinceName = ProvinceName,
        DistrictCode = DistrictCode,
        DistrictName = DistrictName,
        WardCode = WardCode,
        WardName = WardName,
        WardLabelMn = WardLabelMn,
        CatalogueAsOfUtc = CatalogueAsOfUtc,
    };

    /// <summary>
    /// Trims what was stored. IT DELETES NOTHING.
    ///
    /// 🔴 IT USED TO. A ward whose code did not sit under the chosen district
    /// was cleared here, together with its name and its heading - the reasoning
    /// being that a record two different things wrote must not reach the
    /// suggestion rules. The conclusion was right and the remedy was not:
    /// silently discarding what somebody stored, at load, is not one of the
    /// three honest answers. Those are keep-and-mark, keep-and-warn, or refuse
    /// loudly. This keeps and marks - see <see cref="ProblemMn"/> - and
    /// <see cref="IsChosen"/> is what stops it being acted on.
    ///
    /// The old code was never reached in any case: nothing outside the tests
    /// called this. So the deletion never ran AND the protection never ran, and
    /// an inconsistent chain was loaded exactly as stored and printed on a
    /// cover. Both halves of that are fixed above, where no caller is needed.
    /// </summary>
    public void Normalize()
    {
        ProvinceCode = (ProvinceCode ?? "").Trim();
        ProvinceName = (ProvinceName ?? "").Trim();
        DistrictCode = (DistrictCode ?? "").Trim();
        DistrictName = (DistrictName ?? "").Trim();
        WardCode = (WardCode ?? "").Trim();
        WardName = (WardName ?? "").Trim();
        WardLabelMn = (WardLabelMn ?? "").Trim();
    }

    /// <summary>
    /// The location line a cover prints: «УЛААНБААТАР ХОТ, БАЯНГОЛ ДҮҮРГИЙН
    /// 29-Р ХОРОО» and the like.
    ///
    /// Built from the stored NAMES, never from the codes, and empty when the
    /// choice is incomplete - a half-built line on a cover reads as a fault in
    /// the program rather than as an unanswered question.
    /// </summary>
    /// <summary>
    /// The chosen units as one line: «Улаанбаатар хот, Сонгинохайрхан дүүрэг,
    /// 1-р хороо».
    ///
    /// 🔴 REWRITTEN AGAINST THE SHARED VECTORS. The previous version was
    /// `"{Province}, {District}-ийн {Ward}"`, which was wrong three ways at
    /// once, and the third way is the one that matters most:
    ///
    ///   * it never read <see cref="WardLabelMn"/> - a field this very type
    ///     documents as impossible to re-derive, stored for exactly this use
    ///     and then not used. A value written and never read;
    ///   * it glued the genitive «-ийн» onto a NAME, producing «Сонгинохайрхан
    ///     дүүрэг-ийн». Mongolian genitive follows vowel harmony and place
    ///     names are an unbounded set, so guessing the suffix from a name is
    ///     wrong somewhere among 2 217 units. Labels are a CLOSED set of seven,
    ///     so a suffix on the label is a lookup instead of a guess;
    ///   * it printed no label for the first two levels at all.
    ///
    /// 🔴 THE LABEL IS APPENDED ONLY WHEN THE NAME DOES NOT ALREADY CARRY IT,
    /// and the test is CONTAINS rather than ENDS-WITH. Measured on the real
    /// catalogue: 1 852 of 1 853 ward names carry their own label, and many
    /// carry it in the MIDDLE - «1-р баг, Жинст». An ends-with test passes that
    /// one and prints «1-р баг, Жинст баг». The server's first composer had
    /// exactly this bug on 1 544 units.
    ///
    /// The first two levels' labels are DERIVED, because measurement says they
    /// are safe to derive: 0 of 22 province names and 2 of 342 district names
    /// carry theirs. Only the third level had to travel in the data.
    /// </summary>
    public string CoverLine()
    {
        // 🔴 AN INCONSISTENT CHAIN PRINTS NOTHING. A district that is not in the
        // chosen province is not a place - «Улаанбаатар хот, Өлгий дүүрэг» names
        // somewhere that does not exist, and it would go onto a signed sheet
        // looking perfectly ordinary. A PARTIAL choice is different and is
        // printed: a province alone is a real place, just an incomplete answer.
        //
        // This guard was in the previous version by accident - it returned ""
        // for anything not fully chosen, which covered inconsistency along with
        // everything else. Keeping it deliberately, and only for the case that
        // needs it, is what lets the partial case through.
        if (!ChainHoldsTogether)
            return "";

        var parts = new List<string>(3);
        if (ProvinceName.Trim().Length > 0)
            parts.Add(WithLabel(ProvinceName, IsCapital ? "хот" : "аймаг"));
        if (DistrictName.Trim().Length > 0)
            parts.Add(WithLabel(DistrictName, IsCapital ? "дүүрэг" : "сум"));
        if (WardName.Trim().Length > 0)
            parts.Add(WithLabel(WardName, WardLabelMn));

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The same line without the province - what a list column shows, where the
    /// province is either obvious or in its own column. Falls back to the
    /// province when that is all there is, because an empty cell would say
    /// "unknown" about a project that does have a location.
    /// </summary>
    public string ShortLine()
    {
        if (!ChainHoldsTogether)
            return "";

        var parts = new List<string>(2);
        if (DistrictName.Trim().Length > 0)
            parts.Add(WithLabel(DistrictName, IsCapital ? "дүүрэг" : "сум"));
        if (WardName.Trim().Length > 0)
            parts.Add(WithLabel(WardName, WardLabelMn));

        return parts.Count > 0 ? string.Join(", ", parts) : CoverLine();
    }

    /// <summary>
    /// Whether the province is the capital, which is what decides «хот/дүүрэг»
    /// against «аймаг/сум».
    ///
    /// Read from the CODE because a stored project keeps codes and names and
    /// not the catalogue rows they came from - there is no level to consult.
    /// Mongolia has one capital and its code is published and fixed, so this is
    /// a constant rather than a guess; the shared vectors exercise both sides
    /// of it.
    /// </summary>
    private bool IsCapital => ProvinceCode.Trim() == "511";

    /// <summary>
    /// A unit's name followed by its label - unless the name already says it.
    /// </summary>
    private static string WithLabel(string? name, string? label)
    {
        string unit = (name ?? "").Trim();
        string word = (label ?? "").Trim();
        if (unit.Length == 0 || word.Length == 0)
            return unit;

        return unit.Contains(word, StringComparison.OrdinalIgnoreCase)
            ? unit
            : unit + " " + word;
    }
}
