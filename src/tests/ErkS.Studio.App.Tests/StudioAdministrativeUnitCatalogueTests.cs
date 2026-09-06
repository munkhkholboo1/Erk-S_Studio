using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The fetch, the cache, and the sentences.
///
/// Yesterday this class returned an empty list unconditionally while the editor
/// promised «Холбогдсоны дараа сонгох боломжтой болно». Somebody reading that
/// would have deployed a server, seen three empty boxes and looked for the fault
/// on the wrong side of the wire. So the tests here are less about JSON than
/// about whether the five situations a person can be in - never downloaded,
/// offline, bad certificate, server error, unreadable answer - each say
/// something they can act on, and whether a cached list ever passes itself off
/// as a live one.
/// </summary>
public sealed class StudioAdministrativeUnitCatalogueTests : IDisposable
{
    private readonly string cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "erk-s-admin-units-" + Guid.NewGuid().ToString("N"));

    private const string Server = "https://erk-s.mn";

    private string CachePath => Path.Combine(cacheDirectory, "administrative-divisions.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ASuccessfulAnswerFILLSThePickerAndSAYSWhereItCameFrom()
    {
        Uri? requested = null;
        var catalogue = Catalogue((uri, _) =>
        {
            requested = uri;
            return Task.FromResult(Json(HttpStatusCode.OK, Published()));
        });

        Assert.True(await catalogue.LoadAsync(Server));

        Assert.Equal(
            "https://erk-s.mn/api/cloud-era/v1/administrative-divisions",
            requested?.AbsoluteUri);
        Assert.Equal("Улаанбаатар", catalogue.ChildrenOf("").Single().NameMn);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero), catalogue.AsOfUtc);
        Assert.Equal("", catalogue.UnavailableReasonMn);
        Assert.Contains("серверээс", catalogue.SourceNoticeMn, StringComparison.Ordinal);
        Assert.Contains("2026-09-06", catalogue.SourceNoticeMn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A404SaysTheROUTEMayNotBePublishedYet()
    {
        // The state the route was actually in the day this was written. «Сервер
        // алдаа буцаалаа» would send the reader looking for a fault in a server
        // that is working perfectly and simply has not been deployed.
        var catalogue = Catalogue((_, _) => Task.FromResult(Json(HttpStatusCode.NotFound, "")));

        Assert.False(await catalogue.LoadAsync(Server));

        Assert.False(catalogue.HasUnits);
        Assert.Contains("404", catalogue.UnavailableReasonMn, StringComparison.Ordinal);
        Assert.Contains("нийтлэгдээгүй", catalogue.UnavailableReasonMn, StringComparison.Ordinal);
        Assert.Equal("", catalogue.SourceNoticeMn);
    }

    [Fact]
    public async Task ACERTIFICATEFailureIsNotWordedAsBeingOffline()
    {
        // Two failures that arrive as the same exception type and mean opposite
        // things: check your network, or do not trust this server. Told apart,
        // because being told the wrong one costs an afternoon - and because
        // certificate checking is never switched off to make this go away.
        var offline = Catalogue((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("no such host")));
        var badCertificate = Catalogue((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("ssl", new AuthenticationException("cert"))));

        await offline.LoadAsync(Server);
        await badCertificate.LoadAsync(Server);

        Assert.Contains("сүлжээ", offline.UnavailableReasonMn, StringComparison.Ordinal);
        Assert.Contains("гэрчилгээ", badCertificate.UnavailableReasonMn, StringComparison.Ordinal);
        Assert.NotEqual(offline.UnavailableReasonMn, badCertificate.UnavailableReasonMn);
    }

    [Fact]
    public async Task FIVESituationsProduceFIVEDifferentSentences()
    {
        // THE DISCRIMINATION CHECK, and the reason this class exists in the shape
        // it does. Each of these is a different thing for the reader to go and do.
        // A tidy-up that merged any two of them into one polite apology would put
        // us back where yesterday started.
        string[] reasons =
        [
            AwaitReason(Catalogue((_, _) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")))),
            AwaitReason(Catalogue((_, _) => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("tls", new AuthenticationException("cert"))))),
            AwaitReason(Catalogue((_, _) =>
                Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")))),
            AwaitReason(Catalogue((_, _) => Task.FromResult(Json(HttpStatusCode.NotFound, "")))),
            AwaitReason(Catalogue((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"units\":[]}")))),
        ];

        Assert.All(reasons, reason => Assert.NotEqual("", reason));
        Assert.Equal(reasons.Length, reasons.Distinct(StringComparer.Ordinal).Count());

        // And none of them is the sentence for a build that does not fetch, which
        // is what this class used to say no matter what happened.
        Assert.DoesNotContain(reasons, reason => reason.Contains("хараахан татагдаагүй", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACACHEDListNeverPassesItselfOffAsALIVEOne()
    {
        // The worst of the states: somebody picks a khoroo out of a copy from
        // three months ago and nothing on screen mentions it. The picker hides
        // UnavailableReasonMn as soon as there is something to choose from -
        // correctly - so this notice has to be a separate channel or it would be
        // hidden by exactly the case it exists for.
        var first = Catalogue((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Published())));
        Assert.True(await first.LoadAsync(Server));
        Assert.True(File.Exists(CachePath));

        var second = Catalogue((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        Assert.False(await second.LoadAsync(Server));

        Assert.True(second.HasUnits);
        Assert.Equal("Улаанбаатар", second.ChildrenOf("").Single().NameMn);
        Assert.Equal("", second.UnavailableReasonMn);
        Assert.Contains("хуулбараас", second.SourceNoticeMn, StringComparison.Ordinal);
        Assert.Contains("шинэчлэгдээгүй", second.SourceNoticeMn, StringComparison.Ordinal);
        Assert.Contains("2026-09-06", second.SourceNoticeMn, StringComparison.Ordinal);

        // The version on screen is still the SERVER's, taken from the cached
        // answer - not a stamp this client invented when it read the file.
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero), second.AsOfUtc);
    }

    [Fact]
    public async Task AnUNUSABLEAnswerDoesNotPOISONTheCache()
    {
        // A good copy on disk is what keeps an offline machine working. Writing
        // over it with whatever the server said this time would turn one bad
        // deploy into a permanently broken client.
        var first = Catalogue((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Published())));
        await first.LoadAsync(Server);
        string kept = File.ReadAllText(CachePath, Encoding.UTF8);

        var second = Catalogue((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"units\":[]}")));
        await second.LoadAsync(Server);

        Assert.Equal(kept, File.ReadAllText(CachePath, Encoding.UTF8));
        Assert.True(second.HasUnits);
    }

    [Fact]
    public async Task AFAILEDRefreshDoesNotTHROWAWAYAGoodList()
    {
        // Nothing on the current screen reaches this - the load stops once a list
        // is in hand - so this is a trap set for the next caller rather than a
        // bug being fixed. Emptying a working catalogue because a refresh failed
        // is the kind of loss that gets noticed a long way from its cause.
        bool offline = false;
        var catalogue = Catalogue((_, _) => offline
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"))
            : Task.FromResult(Json(HttpStatusCode.OK, Published())));

        Assert.True(await catalogue.LoadAsync(Server));

        // The cache is removed so the surviving list can only be the one already
        // in memory - otherwise this passes on the cached copy and says nothing
        // about the case it is named for.
        File.Delete(CachePath);
        offline = true;
        Assert.False(await catalogue.LoadAsync(Server));

        Assert.True(catalogue.HasUnits);
        Assert.Equal("", catalogue.UnavailableReasonMn);

        // And the notice names where that list really came from - the server,
        // earlier - rather than calling it a cached copy, which is what a fixed
        // sentence would have had to say.
        Assert.Contains("серверээс", catalogue.SourceNoticeMn, StringComparison.Ordinal);
        Assert.Contains("шинэчлэгдээгүй", catalogue.SourceNoticeMn, StringComparison.Ordinal);
        Assert.DoesNotContain("хуулбараас", catalogue.SourceNoticeMn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatIsCACHEDReadsBackAsTheSameCatalogue()
    {
        // A copy that comes back subtly different is worse than no copy: it
        // invents defects that were never on the wire. The round trip is checked
        // rather than assumed because the alternative - trusting that writing and
        // reading agree - is exactly the assumption that has failed on this
        // platform before.
        var written = Catalogue((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Published())));
        await written.LoadAsync(Server);

        var readBack = Catalogue((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        await readBack.LoadAsync(Server);

        Assert.Equal(written.AsOfUtc, readBack.AsOfUtc);
        Assert.Equal(
            written.ChildrenOf("51101").Select(unit => unit.UnitCode + "|" + unit.NameMn),
            readBack.ChildrenOf("51101").Select(unit => unit.UnitCode + "|" + unit.NameMn));
        Assert.Equal("1-р хороо", readBack.ChildrenOf("51101").Single().NameMn);
    }

    [Fact]
    public async Task AMalformedAnswerREPORTSWhatWasWrongWithIt()
    {
        // Not "something went wrong". The parser already knows which row and
        // which field, and that sentence is the one worth carrying all the way
        // to the screen.
        var catalogue = Catalogue((_, _) => Task.FromResult(Json(
            HttpStatusCode.OK,
            "{\"asOfUtc\":\"2026-09-06T08:00:00Z\",\"units\":[" +
            "{\"unitCode\":\"511\",\"level\":\"Capital\",\"parentUnitCode\":null}]}")));

        Assert.False(await catalogue.LoadAsync(Server));

        Assert.Contains("nameMn", catalogue.UnavailableReasonMn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoServerUrlIsItsOwnAnswer()
    {
        bool asked = false;
        var catalogue = Catalogue((_, _) =>
        {
            asked = true;
            return Task.FromResult(Json(HttpStatusCode.OK, Published()));
        });

        Assert.False(await catalogue.LoadAsync("   "));

        Assert.False(asked);
        Assert.Contains("хаяг", catalogue.UnavailableReasonMn, StringComparison.Ordinal);
    }

    [Fact]
    public void TheROUTEIsTheOneSRVPublished()
    {
        Assert.Equal(
            "/api/cloud-era/v1/administrative-divisions",
            StudioAdministrativeUnitCatalogue.RoutePath);
    }

    private static string AwaitReason(StudioAdministrativeUnitCatalogue catalogue)
    {
        catalogue.LoadAsync(Server).GetAwaiter().GetResult();
        return catalogue.UnavailableReasonMn;
    }

    private StudioAdministrativeUnitCatalogue Catalogue(
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(CachePath, send);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// A published answer in the envelope SRV actually serves - header fields,
    /// declared count, `hasChildren` and an offset written «+00:00» rather than
    /// «Z».
    ///
    /// It was a guess until SRV serialised their real DTO into
    /// `_shared/mongolia-admin-divisions-envelope-sample.json`; the shape is
    /// checked field for field against that file over in
    /// AdministrativeUnitDocumentTests, and copied here so these tests exercise
    /// the fetch against what the wire will really carry.
    /// </summary>
    private static string Published() =>
        "{\"asOfUtc\":\"2026-09-06T08:00:00+00:00\",\"origin\":\"Bundled\"," +
        "\"unitCount\":3,\"units\":[" +
        "{\"unitCode\":\"511\",\"parentUnitCode\":null,\"level\":\"Capital\"," +
        "\"nameMn\":\"Улаанбаатар\",\"childPickerLabelMn\":\"Дүүрэг\",\"hasChildren\":true}," +
        "{\"unitCode\":\"51101\",\"parentUnitCode\":\"511\",\"level\":\"District\"," +
        "\"nameMn\":\"Багануур\",\"childPickerLabelMn\":\"Хороо\",\"hasChildren\":true}," +
        "{\"unitCode\":\"5110151\",\"parentUnitCode\":\"51101\",\"level\":\"Khoroo\"," +
        "\"nameMn\":\"1-р хороо\",\"childPickerLabelMn\":\"\",\"hasChildren\":false}]}";
}
