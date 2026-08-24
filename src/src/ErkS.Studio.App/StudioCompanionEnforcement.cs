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
    /// <summary>
    /// Arguments that mark an unattended acceptance run — the CI product smoke
    /// test and the release script's install/update checks. Those builds carry
    /// a release label, so without this they would be enforced against, and a
    /// licence prompt would appear where no one can answer it.
    /// </summary>
    private static readonly string[] AcceptanceRunArguments =
    [
        "--release-smoke-test",
        "--release-update-hold-test",
    ];

    public static bool IsEnabledFor(string? serverUrl) =>
        IsEnabledFor(
            serverUrl,
            StudioReleaseInfo.IsDevelopmentBuild,
            Environment.GetCommandLineArgs());

    /// <summary>
    /// Whether the licence this enforcement checks for can be obtained yet.
    ///
    /// It cannot. The two-licence model is not open, nobody has been told how
    /// to buy one, and no decision has been made about the people already
    /// working - a real project is being drawn by four of them right now.
    /// Enforcing against a licence that does not exist would lock them out of
    /// their own work in the name of a rule none of them could satisfy.
    ///
    /// This is a build constant, not a setting: there is still no way for an
    /// official build to be talked out of enforcement at run time. Flip it in
    /// the release that opens licensing, together with whatever is decided for
    /// existing users.
    /// </summary>
    internal const bool LicensingIsOpen = false;

    internal static bool IsEnabledFor(
        string? serverUrl,
        bool isDevelopmentBuild,
        IReadOnlyList<string>? commandLineArguments = null) =>
        LicensingIsOpen &&
        WouldEnforce(serverUrl, isDevelopmentBuild, commandLineArguments);

    /// <summary>
    /// What enforcement decides, setting aside whether licensing is open at
    /// all. Kept separate so the rules stay under test while the gate above
    /// holds them back - a rule nothing exercises is a rule that has quietly
    /// stopped being true by the time it is needed again.
    /// </summary>
    internal static bool WouldEnforce(
        string? serverUrl,
        bool isDevelopmentBuild,
        IReadOnlyList<string>? commandLineArguments = null)
    {
        if (isDevelopmentBuild)
            return false;
        if (IsAcceptanceRun(commandLineArguments))
            return false;

        // An unknown server cannot be assumed local: enforcement stays on, and
        // the decision then rests on the cached grant.
        return !Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback;
    }

    internal static bool IsAcceptanceRun(IReadOnlyList<string>? commandLineArguments)
    {
        if (commandLineArguments is null)
            return false;

        foreach (string argument in commandLineArguments)
        {
            foreach (string acceptance in AcceptanceRunArguments)
            {
                if (string.Equals(argument, acceptance, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
