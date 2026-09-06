namespace ErkS.Platform.Core;

/// <summary>
/// One loaded copy of the catalogue, held in memory and answered from there.
///
/// The picker asks for children thousands of times while a person clicks
/// through three levels, so the parent index is built once, here. Nothing in
/// this class fetches anything: it is handed rows and a version, and where those
/// came from - the route, a cached file, a bundled copy, a test fixture - is
/// somebody else's fact. That separation is the whole reason the fetch could be
/// written after the rules were finished and tested.
///
/// 🔴 HasChildren IS DERIVED HERE, from the rows themselves.
///
/// The published rows do not carry it, and the label cannot supply it: three
/// sums - Хатгал, Бэрх and Гурванбаян - name their child level «Баг» and have no
/// bags published. Both sides once held that false equivalence and SRV measured
/// it away. Deriving it from "does any row name this one as its parent" is exact
/// PROVIDED the whole catalogue arrives in one answer, which is what the route
/// serves today - 22 + 342 + 1853. If it ever starts paging, this derivation
/// silently becomes a lie about the last page, so it is stated here rather than
/// assumed quietly.
/// </summary>
public sealed class AdministrativeUnitSnapshot : IAdministrativeUnitCatalogue
{
    private static readonly IReadOnlyList<AdministrativeUnit> None = [];

    private readonly Dictionary<string, List<AdministrativeUnit>> childrenByParent;

    /// <summary>
    /// A catalogue with nothing in it, carrying the reason. Used for every
    /// not-loaded state - never downloaded, offline, the server answered an
    /// error - because the reason is what tells those apart, and the empty list
    /// is what they have in common.
    /// </summary>
    public static AdministrativeUnitSnapshot Empty(string unavailableReasonMn) =>
        new([], null, unavailableReasonMn);

    public AdministrativeUnitSnapshot(
        IEnumerable<AdministrativeUnit> units,
        DateTimeOffset? asOfUtc,
        string unavailableReasonMn = "")
    {
        ArgumentNullException.ThrowIfNull(units);

        List<AdministrativeUnit> all = units.ToList();
        HashSet<string> parents = new(
            all.Select(unit => unit.ParentUnitCode),
            StringComparer.Ordinal);

        childrenByParent = all
            .Select(unit => unit with { HasChildren = parents.Contains(unit.UnitCode) })
            .GroupBy(unit => unit.ParentUnitCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        Count = all.Count;
        AsOfUtc = asOfUtc;
        UnavailableReasonMn = unavailableReasonMn;
    }

    /// <summary>How many units this copy holds. Zero is a state, not a failure.</summary>
    public int Count { get; }

    public DateTimeOffset? AsOfUtc { get; }

    public string UnavailableReasonMn { get; }

    public IReadOnlyList<AdministrativeUnit> ChildrenOf(string? parentUnitCode) =>
        childrenByParent.TryGetValue((parentUnitCode ?? "").Trim(), out List<AdministrativeUnit>? children)
            ? children
            : None;
}
