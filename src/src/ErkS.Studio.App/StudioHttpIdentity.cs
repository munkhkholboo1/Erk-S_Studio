using System.Net.Http;
using System.Net.Http.Headers;

namespace ErkS.Studio;

/// <summary>
/// What Studio calls itself on the wire.
///
/// 🔴 A MISSING USER-AGENT IS A 403, MEASURED IN PRODUCTION ON 2026-09-07.
/// `erk-s.mn` sits behind Cloudflare, and a request with no `User-Agent` is
/// refused with error 1010 before it ever reaches the server. .NET's HttpClient
/// sends none by default, so every call Studio made was one WAF rule away from
/// failing - and the administrative-divisions route was already failing that
/// way while the client reported «Сервер 403 гэж хариулав», which sent the
/// reader to look for a permissions problem on a public, unauthenticated route.
///
/// Isolated by probing the live route with one header at a time: no headers and
/// `Accept` alone both return 403; a `User-Agent` alone returns 200 with 2217 units.
/// The value itself does not matter to the rule - only that there is one - so it
/// is the honest thing: the product and its version.
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
