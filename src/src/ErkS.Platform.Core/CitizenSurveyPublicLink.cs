namespace ErkS.Platform.Core;

/// <summary>Whether a public link may be printed, and why not if it may not.</summary>
/// <param name="Refusal">Empty when usable. Never a code - it is shown to a person.</param>
public readonly record struct CitizenSurveyLinkCheck(bool IsUsable, string Refusal);

/// <summary>
/// The address a citizen scans, verified before it goes anywhere near paper.
///
/// 🔴 THE ONE VALUE IN THIS FEATURE THAT CANNOT BE CORRECTED LATER. Anything else the
/// server tells Studio can be fetched again; this is printed, posted on a notice board and
/// handed out. A wrong one surfaces as «the link does not work», reported by a member of
/// the public, weeks later - by which time the consultation may be closed and the people
/// who tried are not coming back.
///
/// ⚠ AND THE SERVER CAN PRODUCE A WRONG ONE HONESTLY. SRV measured their own publisher:
/// with no public base address configured it builds the URL from the REQUEST HEADERS, and
/// the live start-up script does not configure it. Nothing is attacking anybody on that
/// route - the publisher is authenticated and receives back its own address - but the
/// address it receives may not be the one the public can reach. So the pair is checked
/// where it is received rather than trusted, and the owner can supply the base by hand
/// when the server cannot know it.
///
/// 🔴 THE MATCH IS ON THE PATH'S LAST SEGMENT, NEVER «contains». A substring test
/// passes for an address that merely mentions the code on its way somewhere else, which is
/// precisely the shape a mistake takes here.
/// </summary>
public static class CitizenSurveyPublicLink
{
    /// <summary>The route a citizen meets, outside the API base, as the contract sets it.</summary>
    public const string PublicFormSegment = "s";

    public static CitizenSurveyLinkCheck Check(string? code, string? formUrl)
    {
        string wanted = (code ?? "").Trim();
        string address = (formUrl ?? "").Trim();

        if (wanted.Length == 0)
            return new CitizenSurveyLinkCheck(false, "Асуулга хараахан нийтлэгдээгүй — код алга.");
        if (address.Length == 0)
            return new CitizenSurveyLinkCheck(false, "Маягтын хаяг ирээгүй — QR үүсгэх боломжгүй.");

        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return new CitizenSurveyLinkCheck(
                false, $"Маягтын хаяг веб хаяг биш байна: {address}");
        }

        string[] segments = parsed.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        bool matches =
            segments.Length >= 2 &&
            segments[^1].Equals(wanted, StringComparison.Ordinal) &&
            segments[^2].Equals(PublicFormSegment, StringComparison.Ordinal);

        return matches
            ? new CitizenSurveyLinkCheck(true, "")
            : new CitizenSurveyLinkCheck(
                false,
                $"Маягтын хаяг кодтойгоо таарахгүй байна (код «{wanted}»): {address}");
    }

    /// <summary>
    /// The link for a code under a base the owner set, when the server cannot know its own.
    /// </summary>
    public static string Build(string? baseUrl, string? code)
    {
        string root = (baseUrl ?? "").Trim().TrimEnd('/');
        string wanted = (code ?? "").Trim();
        return root.Length == 0 || wanted.Length == 0
            ? ""
            : $"{root}/{PublicFormSegment}/{wanted}";
    }
}
