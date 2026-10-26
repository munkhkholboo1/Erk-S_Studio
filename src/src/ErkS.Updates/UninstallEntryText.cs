using System.Text.RegularExpressions;

namespace ErkS.Updates;

/// <summary>
/// The version a product shows in Windows «Apps» (its Uninstall entry), kept true after a delta update
/// (Master, 2026-10-27: after 126 was in place Windows still said «Demo V0.001.106»). The installer
/// writes the entry; the applier only moves the version in it, in the form the entry already uses.
/// </summary>
public static class UninstallEntryText
{
    private static readonly Regex VersionToken = new(@"[vV]?\d+(?:\.\d+)+", RegexOptions.CultureInvariant);

    /// <summary>
    /// <paramref name="current"/> with its version replaced by <paramref name="version"/> - prefix kept
    /// («Demo V0.001.101» + «v0.001.127» → «Demo V0.001.127»; «0.040» + «v0.065» → «0.065»). A text with
    /// no version becomes the bare version for a DisplayVersion, and is left as it is otherwise.
    /// </summary>
    public static string WithVersion(string? current, string version, bool isDisplayVersion)
    {
        string bare = version.Trim().TrimStart('v', 'V');
        if (bare.Length == 0)
            return current ?? "";
        Match match = VersionToken.Match(current ?? "");
        if (!match.Success)
            return isDisplayVersion ? version.Trim() : current ?? "";
        string prefix = match.Value[0] is 'v' or 'V' ? match.Value[0].ToString() : "";
        return current!.Substring(0, match.Index) + prefix + bare + current.Substring(match.Index + match.Length);
    }
}
