using System.Globalization;
using System.Text.RegularExpressions;

namespace ErkS.Studio;

/// <summary>
/// The «album_review» row written to studio-operations.jsonl after every album
/// build, so the Twin can turn the review (магадлал) findings into STU issues
/// (Мастер, 2026-10-20).
/// </summary>
/// <remarks>
/// reasonCode carries the review item numbers in order («magadlal:1,4,4,6»);
/// the findings themselves go to the safe detail field
/// <see cref="FindingsField"/>, sanitized like every other message in the log.
/// </remarks>
internal sealed partial record StudioAlbumReviewLogRow(
    string Outcome,
    string ReasonCode,
    string Message,
    IReadOnlyDictionary<string, string[]> Details)
{
    public const string Operation = "album_review";
    public const string FindingsField = "reviewFindings";

    public static StudioAlbumReviewLogRow From(IEnumerable<string> buildWarnings)
    {
        List<(string Item, string Text)> findings = (buildWarnings ?? [])
            .Select(warning => (Match: FindingPattern().Match(warning ?? ""), Text: warning ?? ""))
            .Where(item => item.Match.Success)
            .Select(item => (item.Match.Groups["item"].Value, item.Text))
            .ToList();
        string reason = "magadlal:" + string.Join(",", findings.Select(finding => finding.Item));
        return new StudioAlbumReviewLogRow(
            findings.Count == 0 ? "completed" : "warning",
            reason,
            findings.Count == 0
                ? "Магадлалын шалгалт: олдвол алга."
                : $"Магадлалын шалгалт: {findings.Count.ToString(CultureInfo.InvariantCulture)} олдвол.",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [FindingsField] = findings.Select(finding => finding.Text).ToArray(),
            });
    }

    [GeneratedRegex(@"^Магадлал (?<item>\d+):", RegexOptions.CultureInvariant)]
    private static partial Regex FindingPattern();
}
