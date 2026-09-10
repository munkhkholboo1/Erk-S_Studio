using System.Text;

namespace ErkS.Studio;

/// <summary>
/// The name of a door, derived from the URL that knocked on it.
///
/// A refusal record has to say WHICH route refused, and the URL cannot be that
/// answer: it carries project ids, album ids, revision ids and account emails.
/// Written into a file on disk, those would turn a diagnostic note into a list
/// of what the person is working on. The symbol - «projects/{id}/albums/{id}»
/// - answers the question a diagnosis actually asks and carries nothing else.
///
/// 🔴 THE IDENTIFICATION IS BY SHAPE, NOT BY A LIST OF KNOWN ROUTES. A
/// hand-written table of paths stays true until somebody adds a route, and then
/// silently files every refusal from it under the wrong name - or under a raw
/// id. Segments are judged by what they look like, so a route nobody has
/// thought about yet is still symbolised correctly the first time it fails.
/// </summary>
internal static class StudioBoundaryRoute
{
    /// <summary>
    /// What a refusal is filed under when the response carries no request to
    /// name - a name of its own rather than a blank.
    ///
    /// 🔴 A BLANK WOULD MAKE «FORGET THIS ROUTE» MEAN «FORGET EVERY REFUSAL
    /// NOBODY COULD NAME», merging failures that have nothing to do with each
    /// other and letting one unrelated success erase the lot. It also reads, in
    /// the file, as a field somebody forgot to fill in. This showed up for real:
    /// a 502 arrived through a response with no RequestMessage and was recorded
    /// under an empty name.
    /// </summary>
    public const string Unnamed = "(route unknown)";

    /// <summary>
    /// The symbol for <paramref name="uri"/>, or <see cref="Unnamed"/> when
    /// there is no request to name.
    /// </summary>
    public static string Symbol(Uri? uri)
    {
        if (uri is null)
            return Unnamed;

        string path;
        try
        {
            path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString();
        }
        catch (InvalidOperationException)
        {
            path = uri.ToString();
        }

        // The query is dropped whole. Filters and tokens live there, and no
        // diagnosis needs them to know which door refused.
        int query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
            path = path[..query];

        var symbol = new StringBuilder();
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (symbol.Length > 0)
                symbol.Append('/');
            symbol.Append(LooksLikeAnIdentifier(segment) ? "{id}" : segment);
        }
        return symbol.Length == 0 ? Unnamed : symbol.ToString();
    }

    /// <summary>
    /// Whether this segment names a THING rather than a place.
    ///
    /// The shapes ids arrive in here: a GUID with or without dashes, a long run
    /// of hex (a fingerprint or a hash), anything with a digit in it, and
    /// anything carrying an «@» - an account email is a path segment on some
    /// routes and is the most sensitive of the lot.
    ///
    /// A route NAME is lower-case letters and dashes and nothing else, which is
    /// what survives this.
    /// </summary>
    private static bool LooksLikeAnIdentifier(string segment)
    {
        if (segment.Length == 0)
            return false;
        if (segment.Contains('@', StringComparison.Ordinal))
            return true;
        if (Guid.TryParse(segment, out _))
            return true;
        if (IsAVersionMarker(segment))
            return false;

        foreach (char character in segment)
        {
            if (char.IsDigit(character))
                return true;
            // A route name is ASCII lower-case and dashes. An upper-case letter
            // or an underscore is an id in one of the shapes the platform uses.
            if (character is not ((>= 'a' and <= 'z') or '-' or '.'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether this segment is an API version - «v1», «v2».
    ///
    /// 🔴 THE FIRST VERSION OF THIS FILE TURNED EVERY ROUTE INTO
    /// «api/cloud-era/{id}/projects/{id}» BECAUSE «v1» CONTAINS A DIGIT, and
    /// the symbol stopped naming which door refused - all of them looked alike.
    ///
    /// This is a SHAPE, not an entry in a list of known paths: «v» followed by
    /// digits and nothing else, which is what a version marker is everywhere and
    /// what no identifier this platform mints looks like. A route name added
    /// tomorrow is still symbolised correctly without anybody touching this.
    /// </summary>
    private static bool IsAVersionMarker(string segment)
    {
        if (segment.Length < 2 || segment[0] is not ('v' or 'V'))
            return false;
        for (int at = 1; at < segment.Length; at++)
        {
            if (!char.IsAsciiDigit(segment[at]))
                return false;
        }
        return true;
    }
}
