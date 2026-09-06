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
    /// enforce agreement between the two. Today all 2217 published rows satisfy
    /// both, measured; a future source that nests differently would make this
    /// say no, and SRV's own test goes red before that reaches here.
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
    public string CoverLine()
    {
        if (!IsChosen)
            return "";
        return $"{ProvinceName}, {DistrictName}-ийн {WardName}";
    }
}
