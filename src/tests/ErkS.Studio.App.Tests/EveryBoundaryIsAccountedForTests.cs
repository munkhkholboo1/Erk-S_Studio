using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Every place that talks to the server is either covered by the funnel or
/// records for itself - and the ones that are neither are NAMED.
///
/// 🔴 THE SCOPE IS DERIVED FROM THE SOURCE, NOT LISTED. That is the whole point:
/// a hand-written list of «the places that make requests» stays true until
/// somebody adds a tenth one, and then the mechanism has a hole nobody can see.
/// The set is read out of the source every run, so a new HTTP owner is red the
/// day it appears - either it routes through the funnel, or it records, or it
/// has to be added to the list of known gaps ON PURPOSE.
///
/// 🔴 AND THE GAPS SHRINK. The uncovered set is a WORK LIST, not an exemption:
/// each name is a place where a server refusal reaches a person as an empty
/// screen, a null, or a bare HttpRequestException with the code and the sentence
/// both gone. Removing a name from it is the work; adding one needs a reason.
/// </summary>
public sealed class EveryBoundaryIsAccountedForTests
{
    /// <summary>
    /// The places that make HTTP calls and neither route through the funnel nor
    /// record a refusal themselves.
    ///
    /// 🔴 EMPTY, AND THAT IS THE POINT OF THE LIST RATHER THAN ITS ABSENCE. It
    /// held five names when this test was written, one for each shape of loss:
    ///
    ///   CloudEraAlbumComponentUploader    reinvented the funnel - read the
    ///                                     error body, then dropped all of it
    ///   StudioAdministrativeUnitCatalogue named its reason on screen, kept none
    ///   StudioProductCatalogService       silent null
    ///   StudioSiteImageCache              silent null
    ///   StudioUpdateService               EnsureSuccessStatusCode - the worst:
    ///                                     the code AND the sentence destroyed
    ///
    /// A name goes back in only as a deliberate act with a reason beside it. A
    /// NEW http owner that records nothing lands here by itself, and red.
    /// </summary>
    private static readonly string[] KnownGaps = [];

    [Fact]
    public void NOTHINGTalksToTheServerWithoutRecordingWhatItSaid()
    {
        IReadOnlyList<string> uncovered = HttpOwners()
            .Where(owner => !owner.Covered)
            .Select(owner => owner.File)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(KnownGaps.Order(StringComparer.Ordinal), uncovered);
    }

    [Fact]
    public void EVERYHttpOwnerIsNamedAndCovered()
    {
        // 🔴 THE POSITIVE CONTROL FOR THE ASSERTION ABOVE. «No uncovered files»
        // is also what a scan that finds no files at all reports, and it would
        // stay green through every future regression. So the covered set is
        // named too: seven owners, every one of them accounted for.
        IReadOnlyList<string> covered = HttpOwners()
            .Where(owner => owner.Covered)
            .Select(owner => owner.File)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[]
            {
                "CloudEraAlbumComponentUploader.cs",
                "CloudEraChunkedAlbumUploader.cs",
                "StudioAccountService.cs",
                "StudioAdministrativeUnitCatalogue.cs",
                "StudioProductCatalogService.cs",
                "StudioSiteImageCache.cs",
                "StudioUpdateService.cs",
            },
            covered);
    }

    [Fact]
    public void THEWorstShapeIsGoneFromTheUPDATEPath()
    {
        // 🔴 EnsureSuccessStatusCode() DESTROYS BOTH HALVES OF THE ANSWER. It
        // throws a bare HttpRequestException with the server's code and the
        // server's sentence gone, so nothing downstream can tell «your licence
        // expired» from «the update server is down» - on the update path, where
        // three separate diagnoses were made in one night.
        //
        // The behaviour is deliberately unchanged: the same exception reaches
        // the same callers. Only the refusal now survives the throw.
        string source = ReadAppSource("StudioUpdateService.cs");
        string helper = MethodBody(
            source, "private static void RecordThenThrow(HttpResponseMessage response, string what)");

        Assert.Contains("StudioBoundaryRefusals.Note(", helper, StringComparison.Ordinal);
        Assert.Contains("response.EnsureSuccessStatusCode();", helper, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(source, "RecordThenThrow(response,"));

        // The throw survives in exactly ONE place - inside the helper, after the
        // record.
        //
        // 🔴 COUNTED ON CODE ONLY. The first run of this went red on its own
        // PROSE: the helper's doc comment names EnsureSuccessStatusCode to
        // explain why it is quarantined there, and a reader counting raw text
        // found two. A test that cannot tell a mention from a call reports
        // whatever its subject happens to say about itself.
        Assert.Equal(
            Occurrences(CodeOnly(helper), "EnsureSuccessStatusCode()"),
            Occurrences(CodeOnly(source), "EnsureSuccessStatusCode()"));
    }

    [Fact]
    public void THESilentRulesChannelISCoveredNow()
    {
        // 🔴 THE ONE ROUTE INSIDE THE SERVICE THAT BYPASSED ITS OWN FUNNEL, and
        // the most consequential to lose. A refusal there returns «no rules»,
        // which is also what a server with no rules returns - so «I could not
        // tell you my rules» and «I have no rules» arrive as one value, and the
        // client silently runs on its own defaults. On a platform whose standing
        // principle is «keep the rules on the server», that is the single way to
        // switch the principle off without anybody noticing.
        string source = ReadAppSource("StudioAccountService.cs");
        string body = MethodBody(
            source,
            "public async Task<StudioServerRuleAnswer> GetServerRulesAsync(");

        // Both ways of losing the rules are recorded: a refusal, and a request
        // that never arrived.
        Assert.Equal(2, Occurrences(body, "StudioBoundaryRefusals.Note("));
        Assert.Contains("StudioBoundaryRefusals.Cleared(", body, StringComparison.Ordinal);

        // The FALLBACK is deliberately unchanged - «no rules» still means every
        // default stands. What was missing was the record, not the behaviour.
        Assert.Equal(3, Occurrences(body, "StudioServerRuleAnswer.Unanswered("));
    }

    private sealed record HttpOwner(string File, bool Covered);

    /// <summary>
    /// Every source file in the app that handles an HTTP response, and whether
    /// its refusals reach the boundary record.
    ///
    /// 🔴 FOUND BY SHAPE, NOT BY VARIABLE NAME. The first version of this scan
    /// looked for «httpClient.» and missed two owners that call their field
    /// something else - the same mistake as searching for destructive VERBS
    /// instead of the destructive function's callers. A file that handles an
    /// HttpResponseMessage is at the boundary whatever it named its client.
    /// </summary>
    private static IReadOnlyList<HttpOwner> HttpOwners()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? app = null;
        while (directory is not null && app is null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
                app = new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.NotNull(app);
        var owners = new List<HttpOwner>();
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(file.FullName, Encoding.UTF8);
            if (!source.Contains("HttpResponseMessage", StringComparison.Ordinal))
                continue;

            // A file that only READS a response somebody else fetched is not at
            // the boundary; it never learns of a refusal first-hand.
            bool sends =
                source.Contains("SendAsync(", StringComparison.Ordinal) ||
                source.Contains("PostAsJsonAsync(", StringComparison.Ordinal) ||
                source.Contains("PostAsync(", StringComparison.Ordinal) ||
                source.Contains("PutAsync(", StringComparison.Ordinal) ||
                source.Contains("GetAsync(", StringComparison.Ordinal);
            if (!sends)
                continue;

            bool covered =
                source.Contains("ReadResponseAsync<", StringComparison.Ordinal) ||
                source.Contains("ReadNoContentResponseAsync(", StringComparison.Ordinal) ||
                source.Contains("ThrowIfFailedAsync(", StringComparison.Ordinal) ||
                source.Contains("StudioBoundaryRefusals.Note(", StringComparison.Ordinal);

            owners.Add(new HttpOwner(file.Name, covered));
        }

        Assert.True(owners.Count >= 7, "the scan found only " + owners.Count + " HTTP owners");
        return owners;
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    /// <summary>
    /// The source with comment lines removed, so a rule counts calls rather than
    /// what the file says about itself.
    /// </summary>
    private static string CodeOnly(string source) =>
        string.Join(
            "\n",
            source.Split('\n').Where(line =>
            {
                string text = line.TrimStart();
                return !text.StartsWith("//", StringComparison.Ordinal) &&
                    !text.StartsWith("///", StringComparison.Ordinal) &&
                    !text.StartsWith("*", StringComparison.Ordinal);
            }));

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
