using System.Net.Http;
using System.Net.Http.Headers;

namespace ErkS.Studio;

/// <summary>
/// What Studio calls itself on the wire.
///
/// 🔴 WITHOUT THIS, THE CATALOGUE ROUTE ANSWERED 403 - measured against
/// production on 2026-09-07. `erk-s.mn` sits behind Cloudflare; .NET's
/// HttpClient sends no User-Agent by default; adding one turned 403 into 200
/// with 2217 units. That A/B is solid and is why this exists.
///
/// ⚠️ BUT THE RULE IS NOT «no User-Agent means 403». `curl` with its
/// User-Agent deliberately removed still gets 200 from the same route - Master
/// measured that immediately afterwards. Cloudflare scores the WHOLE client:
/// TLS handshake, which headers are present, what order they come in. A missing
/// User-Agent pushes .NET over the line and does not push curl over it.
///
/// So this header is a mitigation, NOT a fix, and writing it up as a fix is how
/// the next person concludes the problem is closed. The client remains subject
/// to a WAF decision and can be refused again tomorrow on some other signal.
///
/// THE DURABLE PART IS ELSEWHERE: a failed fetch has to be VISIBLE, and a save
/// must never write an empty location over a stored one. Those hold whatever
/// Cloudflare decides next, and they are what turns "a night of hunting" into
/// "a sentence on screen". See StudioAdministrativeUnitCatalogue and
/// ShellView.SiteLocation.CaptureSiteLocationDraft.
/// </summary>
internal static class StudioHttpIdentity
{
    public static string UserAgent =>
        "ErkS-Studio/" + StudioReleaseInfo.DisplayVersion;

    /// <summary>
    /// Puts the product's name on a client. Called wherever Studio builds one -
    /// a client that skips this works until somebody tightens a WAF rule, and
    /// then fails everywhere at once.
    /// </summary>
    public static void Identify(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.DefaultRequestHeaders.UserAgent.Count > 0)
            return;

        // ParseAdd rather than a constructed ProductInfoHeaderValue: the version
        // string carries build metadata on development builds (`1.2.3+abc`), and
        // the strict constructor rejects it.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Sanitise(UserAgent));
    }

    /// <summary>
    /// A User-Agent has to be a legal header value. A development version with
    /// a space or a stray character in it would throw here rather than on the
    /// first request, which is the wrong place to find out.
    /// </summary>
    internal static string Sanitise(string value)
    {
        var cleaned = new System.Text.StringBuilder(value.Length);
        foreach (char character in value)
        {
            cleaned.Append(character is > (char)32 and < (char)127 && character != '"'
                ? character
                : '-');
        }

        string text = cleaned.ToString().Trim('-');
        return text.Length == 0 ? "ErkS-Studio" : text;
    }
}
