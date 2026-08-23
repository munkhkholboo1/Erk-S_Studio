namespace ErkS.Studio;

/// <summary>
/// The one switch that turns Studio's companion-licence enforcement on.
///
/// Studio is free but is meant to open only for an account holding an active
/// Platform or CityGen licence. Until an official build ships, that rule must
/// not stand in the way of development and testing, so a development build
/// never enforces it — and neither does a loopback server, whose database
/// holds no real licences. There is deliberately no environment variable or
/// setting that turns enforcement off for an official build against the live
/// server: that would be the bypass the rule exists to prevent.
/// </summary>
internal static class StudioCompanionEnforcement
{
    public static bool IsEnabledFor(string? serverUrl) =>
        IsEnabledFor(serverUrl, StudioReleaseInfo.IsDevelopmentBuild);

    internal static bool IsEnabledFor(string? serverUrl, bool isDevelopmentBuild)
    {
        if (isDevelopmentBuild)
            return false;

        // An unknown server cannot be assumed local: enforcement stays on, and
        // the decision then rests on the cached grant.
        return !Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback;
    }
}
