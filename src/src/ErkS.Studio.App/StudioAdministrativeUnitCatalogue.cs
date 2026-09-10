using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Studio's source of administrative units: the published route, a copy of the
/// last good answer on this machine, and - always - a sentence saying which of
/// those the list on screen came from.
///
/// 🔴 THE REPLACEMENT POINT IS THIS CLASS AND NOTHING ELSE, which is what the
/// interface was for. The labels, the ordering, the cascade, what a stored
/// location restores to: all of that was finished and tested against fixtures
/// before this file made its first request, and none of it changed when it did.
///
/// WHY EVERY FAILURE GETS ITS OWN SENTENCE. Yesterday the editor said
/// «Холбогдсоны дараа сонгох боломжтой болно» while no code fetched anything,
/// and the reader would have deployed a server, seen three empty boxes and hunted
/// on the wrong side of the wire. One general sentence cannot avoid that,
/// because it has to stand for five different situations - never downloaded,
/// this machine is offline, the certificate did not verify, the server answered
/// an error, the answer did not parse - and the reader acts on which one it is.
/// So each says what happened, and 404 in particular says the route may not be
/// published on that server yet, which is exactly the state it was in when this
/// was written.
///
/// SERVING FROM CACHE IS NEVER SILENT. A stale list that looks live is the worst
/// of the states: somebody picks a khoroo out of a three-month-old copy and
/// nothing anywhere says so. When the list comes from the cached copy, the
/// notice says so and names the version it is.
/// </summary>
internal sealed class StudioAdministrativeUnitCatalogue : IAdministrativeUnitCatalogue
{
    /// <summary>The published route. No sign-in: the catalogue is public data.</summary>
    public const string RoutePath = "/api/cloud-era/v1/administrative-divisions";

    private const string CacheFileName = "administrative-divisions.json";

    private static readonly string NotDownloadedMn =
        "Засаг захиргааны нэгжийн жагсаалт хараахан татагдаагүй байна. " +
        "Доорх хаягийн мөрөнд бичнэ үү.";

    private readonly string cacheFilePath;
    private readonly Func<Uri, CancellationToken, Task<HttpResponseMessage>> send;
    private AdministrativeUnitSnapshot current = AdministrativeUnitSnapshot.Empty(NotDownloadedMn);

    /// <summary>
    /// Where the list currently on screen came from, in words. Kept rather than
    /// worked out at the moment of failure: by then the only thing visible is
    /// that a list exists, and "the cached copy" and "what the server sent
    /// earlier this run" would be a guess between two true-sounding sentences.
    /// </summary>
    private string listSourceMn = "";

    /// <summary>
    /// The one the application uses. A single instance so the download happens
    /// once per run and every picker built afterwards sees the same list and the
    /// same version.
    /// </summary>
    public static StudioAdministrativeUnitCatalogue Live { get; } = new();

    private StudioAdministrativeUnitCatalogue()
        : this(
            Path.Combine(StudioAccountService.AccountDataRoot, CacheFileName),
            CreateHttpSender())
    {
    }

    internal StudioAdministrativeUnitCatalogue(
        string cacheFilePath,
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> send)
    {
        this.cacheFilePath = cacheFilePath;
        this.send = send;
    }

    /// <summary>
    /// Which version of the catalogue is on screen, issued by the SERVER.
    ///
    /// Null while nothing has been read. Never invented here even when a value
    /// would be convenient: a stamp this client made up would make a choice from
    /// a stale offline copy and one made against live data say the same thing,
    /// and both sides agreed that must not happen.
    /// </summary>
    public DateTimeOffset? AsOfUtc => current.AsOfUtc;

    public IReadOnlyList<AdministrativeUnit> ChildrenOf(string? parentUnitCode) =>
        current.ChildrenOf(parentUnitCode);

    public string UnavailableReasonMn => current.UnavailableReasonMn;

    /// <summary>
    /// WHERE the list on screen came from, shown whether or not there is a list.
    ///
    /// Separate from <see cref="UnavailableReasonMn"/> because the picker hides
    /// that one as soon as there is anything to choose from - correctly, since it
    /// exists to explain an empty box. Serving a cached copy is the case where
    /// there IS something to choose from and the reader still has to be told.
    /// </summary>
    public string SourceNoticeMn { get; private set; } = "";

    /// <summary>Whether a usable list has been read from anywhere.</summary>
    public bool HasUnits => current.Count > 0;

    /// <summary>
    /// Read the catalogue: the cached copy first so a picker has something at
    /// once, then the route.
    ///
    /// Returns false when the list on screen did not come from the server this
    /// call - including the case where a perfectly good cached copy is being
    /// served, because "we are showing something" and "we reached the server"
    /// are different answers and the caller may care about either.
    /// </summary>
    public async Task<bool> LoadAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        AdoptCachedCopy();

        if (!Uri.TryCreate(NormalizeServerUrl(serverUrl), UriKind.Absolute, out Uri? baseUri))
        {
            Degrade("Серверийн хаяг тохируулагдаагүй байна.");
            return false;
        }

        Uri route = new(baseUri, RoutePath.TrimStart('/'));
        string body;
        try
        {
            using HttpResponseMessage response = await send(route, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // This one already NAMES its reason on screen, which is more
                // than the others did. The record is what lets somebody read it
                // afterwards, on a machine they are not sitting at.
                StudioBoundaryRefusals.Note(
                    StudioBoundaryRoute.Symbol(route),
                    "",
                    (int)response.StatusCode,
                    "Засаг захиргааны нэгжийн жагсаалт татагдсангүй: " +
                    DescribeStatus(response.StatusCode));
                Degrade(DescribeStatus(response.StatusCode));
                return false;
            }

            StudioBoundaryRefusals.Cleared(StudioBoundaryRoute.Symbol(route));
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Degrade(DescribeTransportFailure(error));
            return false;
        }

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(body);
        if (!read.IsUsable)
        {
            Degrade(read.ProblemMn);
            return false;
        }

        current = new AdministrativeUnitSnapshot(read.Units, read.AsOfUtc);
        listSourceMn = "серверээс";
        SourceNoticeMn = "Жагсаалт серверээс. Каталогийн хувилбар: " + Stamp(read.AsOfUtc) + ".";
        SaveCachedCopy(body);
        return true;
    }

    /// <summary>
    /// Puts the cached copy on screen before the route is tried, so the pickers
    /// are usable while the request is in flight and stay usable if it fails.
    /// </summary>
    private void AdoptCachedCopy()
    {
        string cached;
        try
        {
            if (!File.Exists(cacheFilePath))
                return;
            cached = File.ReadAllText(cacheFilePath, Encoding.UTF8);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(cached);
        if (!read.IsUsable)
            return;

        current = new AdministrativeUnitSnapshot(read.Units, read.AsOfUtc);
        listSourceMn = "энэ компьютерт хадгалсан хуулбараас";
        SourceNoticeMn =
            "Жагсаалт энэ компьютерт хадгалсан хуулбараас (хувилбар " +
            Stamp(read.AsOfUtc) + "). Шинэчилж байна…";
    }

    /// <summary>
    /// The route did not answer usefully. Two different outcomes, and they must
    /// not be worded the same: with a list already in hand the person can still
    /// work and needs to know it may be old; with none there is nothing to choose
    /// from and they need to know why.
    ///
    /// It asks the CURRENT STATE rather than taking a flag from the caller. A
    /// flag says what the caller believed a moment ago, and the one thing that
    /// must not happen here is throwing away a good list because a refresh
    /// failed.
    /// </summary>
    private void Degrade(string reasonMn)
    {
        if (current.Count > 0)
        {
            SourceNoticeMn =
                "Жагсаалт " + listSourceMn + " (хувилбар " + Stamp(current.AsOfUtc) +
                "), шинэчлэгдээгүй. " + reasonMn;
            return;
        }

        current = AdministrativeUnitSnapshot.Empty(reasonMn);
        SourceNoticeMn = "";
    }

    private void SaveCachedCopy(string body)
    {
        try
        {
            string? directory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Written aside and moved into place: a half-written cache would be
            // read back as a malformed catalogue on the next run, and the reason
            // shown would blame the server for this machine crashing mid-write.
            string temporaryPath = cacheFilePath + ".tmp";
            File.WriteAllText(temporaryPath, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, cacheFilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The list is already on screen; failing to keep a copy costs the
            // next offline run, not this one.
        }
    }

    /// <summary>
    /// 404 gets its own sentence because it is the state the route was in the
    /// day this was written - written on the server, not published - and
    /// "сервер алдаа буцаалаа" would send the reader looking for a fault that is
    /// not there.
    /// </summary>
    private static string DescribeStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound =>
            "Сервер дээр энэ маршрут алга (404). Серверийн шинэ хувилбар " +
            "нийтлэгдээгүй байж болзошгүй.",
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout =>
            "Сервер түр ажиллахгүй байна (" + (int)status + ").",
        _ => "Сервер " + (int)status + " гэж хариулав.",
    };

    /// <summary>
    /// A certificate failure is NOT worded as "offline". Verification is never
    /// switched off here - an importer that stops checking its source is not an
    /// importer - and telling somebody to check their network when the real
    /// answer is a certificate is a whole afternoon.
    /// </summary>
    private static string DescribeTransportFailure(Exception error)
    {
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
        {
            if (cause is AuthenticationException)
            {
                return "Серверийн гэрчилгээ шалгагдсангүй. Холболт аюулгүй " +
                    "эсэх нь батлагдаагүй тул жагсаалтыг татсангүй.";
            }
        }

        if (error is TaskCanceledException or TimeoutException)
            return "Сервер хугацаанд хариулсангүй.";

        return error is HttpRequestException
            ? "Сервер рүү холбогдож чадсангүй. Энэ компьютер сүлжээнд " +
                "холбогдоогүй, эсвэл сервер унтарсан байж магадгүй."
            : "Жагсаалтыг татахад алдаа гарлаа: " + error.Message;
    }

    private static string Stamp(DateTimeOffset? asOfUtc) =>
        asOfUtc is null
            ? "тодорхойгүй"
            : asOfUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NormalizeServerUrl(string? serverUrl)
    {
        string trimmed = (serverUrl ?? "").Trim();
        if (trimmed.Length == 0)
            return "";
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }

    private static Func<Uri, CancellationToken, Task<HttpResponseMessage>> CreateHttpSender()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
        // Without this the route answers 403 - Cloudflare refuses a request with
        // no User-Agent, and .NET sends none. Measured against production.
        StudioHttpIdentity.Identify(client);
        return (uri, cancellationToken) => client.GetAsync(uri, cancellationToken);
    }
}
