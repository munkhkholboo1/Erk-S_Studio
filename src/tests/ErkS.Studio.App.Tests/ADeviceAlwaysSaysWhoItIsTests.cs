using System.Text;
using System.Text.RegularExpressions;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Every bot-state call names this machine, and the name can never come out empty.
///
/// 🔴 THIS PINS THE PREMISE UNDER WHICH A NEW SERVER REFUSAL IS UNREACHABLE HERE.
/// On 2026-09-12 the server split «bot_state_device_required» (400) out of
/// «bot_state_seat_unavailable»: it means «retry - send your fingerprint; nothing
/// happened to the seat». This client cannot provoke it, because every bot-state
/// route sends a fingerprint that is a hash by construction.
///
/// That «cannot» is a fact about TODAY'S CODE, not a law - so it is asserted here
/// rather than written in a note. The day somebody adds a route that takes its
/// fingerprint from somewhere new, this goes red and the question gets asked
/// again: can it be empty, and if so, what does the client do when the server
/// says «send it properly»? A premise nobody re-checks is how a refusal arrives
/// at a client that treats it as an unknown failure.
/// </summary>
public sealed class ADeviceAlwaysSaysWhoItIsTests
{
    /// <summary>
    /// The expressions a bot-state request may take its fingerprint from, each
    /// proved non-empty by a test below. NAMES, not a count: the set is allowed
    /// to grow, but only through here.
    /// </summary>
    private static readonly string[] ProvenNonEmpty =
    [
        "fingerprints.Canonical",
        "keyFingerprint",
    ];

    [Fact]
    public void EVERYBotStateRouteSendsAFingerprintFromAProvenSource()
    {
        // 🔴 DERIVED FROM SOURCE, NEVER HAND-LISTED. A list of routes typed out
        // here would be corrected by hand the same day a route was added, which
        // is exactly when the check needed to fail.
        string service = ReadAppSource("StudioAccountService.cs");

        IReadOnlyList<(string Route, string Expression)> sites = BotStateFingerprintSites(service);

        Assert.True(
            sites.Count >= 5,
            $"only {sites.Count} bot-state route(s) were found; the scan has lost its grip on the source");

        foreach ((string route, string expression) in sites)
        {
            Assert.True(
                ProvenNonEmpty.Contains(expression, StringComparer.Ordinal),
                $"«{route}» sends its fingerprint from «{expression}», which nothing here proves " +
                "cannot be empty. Prove it and add it above, or the server's " +
                "«bot_state_device_required» becomes reachable with no handler.");
        }
    }

    [Fact]
    public void THEKnownRoutesAreAllStillThere()
    {
        // The scan above answers «is every site proven». This answers «did a site
        // quietly stop being scanned» - a renamed path would empty the first test
        // of meaning while leaving it green.
        string service = ReadAppSource("StudioAccountService.cs");
        IReadOnlyList<(string Route, string Expression)> sites = BotStateFingerprintSites(service);

        foreach (string route in new[] { "challenge", "session", "resume", "pin/lockout", "/state" })
        {
            Assert.True(
                sites.Any(site => site.Route.Contains(route, StringComparison.Ordinal)),
                $"the bot-state route «{route}» is no longer found by the scan");
        }
    }

    [Fact]
    public void THETraitFingerprintIsAHashEvenWhenTheMachineTellsUsNOTHING()
    {
        // The worst case this machine can be in: no name, no user, no registry
        // GUID, no SID - every trait unavailable. A hash of the salt alone is
        // still a hash, so the request is still answerable.
        StudioDeviceFingerprints starved =
            StudioDeviceIdentity.BuildFingerprints("", "", "", "");

        Assert.False(string.IsNullOrWhiteSpace(starved.Canonical));
        Assert.False(string.IsNullOrWhiteSpace(starved.Legacy));

        // And the two forms stay distinct, or «legacy» would stop carrying the
        // machine's older records anywhere.
        Assert.NotEqual(starved.Canonical, starved.Legacy);
    }

    [Fact]
    public void AKEYFingerprintIsAHashOfWhateverItIsGiven()
    {
        // Even an empty key blob hashes to something. The fingerprint cannot be
        // the empty string by any input this method accepts.
        Assert.False(string.IsNullOrWhiteSpace(StudioDeviceKeyStore.FingerprintOf([])));
        Assert.False(string.IsNullOrWhiteSpace(StudioDeviceKeyStore.FingerprintOf([0])));
    }

    [Fact]
    public void AMISSINGKeyIsREFUSEDRatherThanSentAsNothing()
    {
        // 🔴 THE ONE NULLABLE SOURCE, AND THE REASON IT IS SAFE. TryFingerprint
        // answers null when there is no key; the caller throws rather than
        // posting. Were that guard to become a fallback - «?? ""» - the client
        // would start asking the server to identify a machine that declined to
        // say which one it is, and «bot_state_device_required» would arrive at a
        // client with nowhere to put it.
        string body = MethodBody(
            ReadAppSource("StudioAccountService.cs"),
            "public async Task<StudioCloudBotStateToken> IssueBotSessionAsync(");

        Assert.Contains("StudioDeviceKeyStore.TryFingerprint()", body, StringComparison.Ordinal);
        Assert.Contains("if (keyFingerprint is null)", body, StringComparison.Ordinal);
        Assert.Contains("throw new StudioAccountException(NoDeviceKeyMessageMn)", body, StringComparison.Ordinal);

        // No fallback may stand in for the missing key.
        Assert.DoesNotContain("keyFingerprint ??", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NOBotStateRouteSendsALiteralEmptyFingerprint()
    {
        // The blunt version of the same claim, kept because it is the one that
        // survives a rewrite of the scan above.
        string service = ReadAppSource("StudioAccountService.cs");

        Assert.DoesNotContain("DeviceFingerprint = \"\"", service, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every bot-state request in the file, paired with the expression its
    /// DeviceFingerprint is taken from - read out of the source, so a new route
    /// appears here without anyone remembering to add it.
    /// </summary>
    private static IReadOnlyList<(string Route, string Expression)> BotStateFingerprintSites(string service)
    {
        var sites = new List<(string, string)>();

        // Two shapes reach the same family of endpoints: a literal path, and the
        // seat path with "/state" appended. Both are scanned, because a scan that
        // knew only the literal form would have missed the one that enters the
        // state in the first place.
        var routes = new Regex(
            "\"(/api/cloud-era/v1/bot-state/[^\"]+)\"|(SeatPath\\(botId\\) \\+ \"/state\")",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        var fingerprint = new Regex(
            "DeviceFingerprint\\s*=\\s*([^,\\r\\n]+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        foreach (Match route in routes.Matches(service))
        {
            // The request literal follows the path in the same call; a generous
            // window, bounded so a missing assignment cannot borrow the next
            // call's.
            int start = route.Index + route.Length;
            string window = service[start..Math.Min(service.Length, start + 420)];
            Match found = fingerprint.Match(window);
            if (found.Success)
                sites.Add((route.Value, found.Groups[1].Value.Trim()));
        }

        return sites;
    }

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
        int start = normalised.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = normalised.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return normalised[start..end];
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
