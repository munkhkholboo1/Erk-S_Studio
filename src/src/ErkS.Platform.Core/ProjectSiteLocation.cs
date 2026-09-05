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
    /// </summary>
    public bool IsChosen =>
        ProvinceCode.Length > 0 && DistrictCode.Length > 0 && WardCode.Length > 0;

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

    public void Normalize()
    {
        ProvinceCode = (ProvinceCode ?? "").Trim();
        ProvinceName = (ProvinceName ?? "").Trim();
        DistrictCode = (DistrictCode ?? "").Trim();
        DistrictName = (DistrictName ?? "").Trim();
        WardCode = (WardCode ?? "").Trim();
        WardName = (WardName ?? "").Trim();
        WardLabelMn = (WardLabelMn ?? "").Trim();

        // A ward whose code does not sit under the chosen district is not a
        // near-miss to be repaired - it is a record that two different things
        // wrote. Keeping it would let the suggestion rules read a unit that
        // belongs somewhere else entirely.
        if (WardCode.Length > 0 &&
            !AdministrativeUnits.ParentCodeOf(WardCode).Equals(DistrictCode, StringComparison.Ordinal))
        {
            WardCode = "";
            WardName = "";
            WardLabelMn = "";
        }

        if (DistrictCode.Length > 0 &&
            !AdministrativeUnits.ParentCodeOf(DistrictCode).Equals(ProvinceCode, StringComparison.Ordinal))
        {
            DistrictCode = "";
            DistrictName = "";
            WardCode = "";
            WardName = "";
            WardLabelMn = "";
        }
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
