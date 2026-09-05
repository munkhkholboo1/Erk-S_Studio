using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Studio's source of administrative units.
///
/// Today it holds nothing, and that is a REAL STATE rather than a placeholder
/// standing in for one: the catalogue is served by Erk-S Server and cached
/// locally, so a first run, an offline machine and a server that is down all
/// arrive here with an empty list. The picker already answers that case, and
/// the editor already says so in words.
///
/// 🔴 THE REPLACEMENT POINT IS THIS CLASS AND NOTHING ELSE. When the route
/// ships, the cache is loaded here and every rule above it - the labels, the
/// ordering, the cascade, what a stored location restores to - stays as it is
/// and stays tested. That is what the interface was for.
/// </summary>
internal sealed class StudioAdministrativeUnitCatalogue : IAdministrativeUnitCatalogue
{
    public static StudioAdministrativeUnitCatalogue Unavailable { get; } = new();

    /// <summary>
    /// Null while nothing has been downloaded. The value is the SERVER's, never
    /// this client's: a stamp invented here would make a choice from a stale
    /// cache indistinguishable from one made against live data.
    /// </summary>
    public DateTimeOffset? AsOfUtc => null;

    public IReadOnlyList<AdministrativeUnit> ChildrenOf(string? parentUnitCode) => [];
}
