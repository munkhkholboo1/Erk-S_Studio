using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace ErkS.Studio;

/// <summary>One request on the bridge: an action and its named arguments.</summary>
internal sealed record StudioBridgeRequest(
    string Id,
    string Action,
    IReadOnlyDictionary<string, string> Args)
{
    public string? Arg(string name) =>
        Args.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}

/// <summary>The answer: ok or a named refusal, a Mongolian message, a result.</summary>
internal sealed record StudioBridgeResponse(
    string Id,
    bool Ok,
    string ReasonCode,
    string Message,
    object? Result,
    IReadOnlyList<string> Notices);

internal sealed class StudioBridgeException(string reasonCode, string message) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
}

/// <summary>
/// The wire format: one JSON object per line each way, UTF-8. A client
/// connects, writes one request line, reads one response line.
/// </summary>
/// <remarks>
/// 🔴 THE OWNER'S RULE, 2026-10-20: Studio is driven from outside through
/// this bridge - PowerShell or cmd (tools/stu.ps1) - never by clicking the
/// owner's screen. One pipe per Studio process, named for it, opened by this
/// Windows user only (docs/STU-BRIDGE.md).
/// </remarks>
internal static class StudioBridgeProtocol
{
    public const string PipePrefix = "ErkS.Studio.Bridge.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static string PipeName(int processId) => PipePrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool TryParsePipeProcessId(string? pipeName, out int processId)
    {
        processId = 0;
        string name = pipeName ?? "";
        return name.StartsWith(PipePrefix, StringComparison.Ordinal) &&
               int.TryParse(name[PipePrefix.Length..], System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out processId);
    }

    public static StudioBridgeRequest ParseRequest(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line ?? "");
            JsonElement root = document.RootElement;
            string id = root.TryGetProperty("id", out JsonElement idElement) ? idElement.ToString() : "";
            string action = root.TryGetProperty("action", out JsonElement actionElement)
                ? (actionElement.GetString() ?? "").Trim()
                : "";
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("args", out JsonElement argsElement) && argsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in argsElement.EnumerateObject())
                {
                    args[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? ""
                        : property.Value.GetRawText();
                }
            }
            if (action.Length == 0)
                throw new StudioBridgeException("bad_request", "Үйлдлийн нэр (action) алга.");
            return new StudioBridgeRequest(id, action, args);
        }
        catch (JsonException exception)
        {
            throw new StudioBridgeException("bad_request", "JSON уншигдсангүй: " + exception.Message);
        }
    }

    public static StudioBridgeResponse Accept(string id, object? result, string message = "", IReadOnlyList<string>? notices = null) =>
        new(id, true, "", message, result, notices ?? []);

    public static StudioBridgeResponse Refuse(string id, string reasonCode, string message, IReadOnlyList<string>? notices = null) =>
        new(id, false, reasonCode, message, null, notices ?? []);

    public static string Serialize(StudioBridgeResponse response) =>
        JsonSerializer.Serialize(response, Options);
}

internal sealed record StudioBridgeAction(string Name, string DescriptionMn, string Arguments, bool Mutates);

/// <summary>
/// What the bridge can do. Each action calls the function the corresponding
/// Studio button calls - a missing one is added here, not worked around.
/// </summary>
internal static class StudioBridgeActionCatalog
{
    public static IReadOnlyList<StudioBridgeAction> All { get; } =
    [
        new("actions", "Гүүрийн бүх үйлдлийн жагсаалт.", "", false),
        new("status", "Studio-гийн төлөв: нээлттэй төсөл, эх үүсвэрүүд, ажиллаж буй үйлдэл, cloud төлөв.", "", false),
        new("notices", "Гүүрийн үед дэлгэцэнд гарах байсан мэдэгдлүүдийг авч цэвэрлэнэ.", "", false),
        new("projects", "Энэ төхөөрөмж дээрх төслүүдийн жагсаалт (код, нэр, зам).", "", false),
        new("open-project", "Төсөл нээнэ — төслийн картыг дарахтай адил.", "path=<хавтас эсвэл .erksproject> | code=<төслийн код>", true),
        new("check-sources", "«Эх үүсвэр шалгах»: энэ төхөөрөмжийн эх үүсвэрийн шинэ багцыг уншиж альбумыг шинэчилнэ.", "", true),
        new("rebuild-album", "Альбумын PDF-ийг дахин угсарна.", "", true),
        new("album-format", "«Нийтлэг хуудасны формат»: Studio-гийн үүсгэх хуудсуудын (нүүр, жагсаалт, тайлбар бичиг) A3 модулийн формат — сонгогчтой ижил; аргументгүй бол одоогийнхыг (сонгосон / альбумын хуудаснаас) хэлнэ.", "[columns=1-4 rows=1-4]", true),
        new("rearm-component", "Альбумын нэг хэсгийг дараагийн Sync-ээр дахин илгээхээр тэмдэглэнэ (локал угсралт зурдаг, Cloud-д алга болсон хэсэг) — «components»-ийн local кодоор.", "code=<бүрдлийн код>", true),
        new("sheet-numbers", "Хуудас бүрийн эх дугаар (Revit «18») ба альбумд хэвлэгдэх дугаар («БА-8») — эх үүсвэр эшлэлээ үүгээр бичнэ.", "", false),
        new("components", "Альбумын хэсгүүд: энэ төхөөрөмжийн угсралтын хэсэг (код, хуудас), Cloud-ийн хэсэг, хүлээгдэж буй код — синк юуг илгээхийг харах.", "", false),
        new("sync-preview", "Cloud товч: эх үүсвэр уншиж, альбумыг угсраад синкийн төлөвлөгөөг буцаана — илгээхгүй.", "", true),
        new("sync", "Cloud товч + «Sync хийх»: төлөвлөгөөг баталж Cloud руу синк хийнэ.", "confirm=true", true),
        new("relink-source", "«Эх файлыг солих»: эх үүсвэрийг өөр локал файлтай холбоно.", "source=<id эсвэл нэр> path=<файл>", true),
        new("import-front-matter", "«ЕХ мэдээлэл» › «Ноорог файлаас оруулах» + «Хадгалах».", "file=<.md>", true),
        new("visualizations", "(унших) «Харагдах байдал»: зураг бүрийн дараалал, id, нэр, эх файл, альбумд орсон эсэх.", "", false),
        new("visualization-add", "«Харагдах байдал» › «Зураг нэмэх»: зургийг (эсвэл хавтасны зургуудыг нэрийн дарааллаар) нэмнэ.", "path=<зураг> | folder=<хавтас> [pattern=*.png]", true),
        new("visualizations-replace", "Одоо альбумд байгаа бүх зургийг «Хуудаснаас хасах» (эх үүсвэрт идэвхгүй хадгалагдана, устгахгүй), дараа нь хавтасны зургуудыг нэрийн дарааллаар «Зураг нэмэх».", "folder=<хавтас> [pattern=*.png]", true),
        new("visualization-exclude", "«Хуудаснаас хасах»: зургийг эх үүсвэрт хадгалж альбумд идэвхгүй болгоно.", "id=<id[,id]> | id=all", true),
        new("visualization-include", "«Хуудсанд оруулах»: идэвхгүй зургийг альбумд буцаана.", "id=<id[,id]> | id=all", true),
        new("sync-status", "(унших) Erk-S Sync: tray ажиллаж буй эсэх, серверийн төлөв, хавтас бүрийн файлын тоо, холбогдсон машин, зөрүүтэй файлын ТОО — файлын нэргүй.", "", false),
        new("sync-folders", "(унших) Таны багийн төслүүдийн Erk-S Sync хавтас (сервер) ба энэ дискэд тэмдэглэгдсэн эсэх.", "", false),
        new("sync-make", "«Erk-S Sync фолдер болгох»: хавтсыг төслийн синк хавтас болгоно. project= байхгүй бол Studio-д хүн төслөө сонгоно.", "path=<хавтас> [project=<төслийн код>]", true),
        new("sync-join", "«Erk-S Sync фолдерт холбох»: энэ машины ижил замтай хавтсыг холбоно. folder= байхгүй бол Studio-д хүн сонгоно.", "path=<хавтас> [folder=<folderId>]", true),
        new("sync-unlink", "«Холболт салгах»: энэ машиныг синк хавтаснаас салгана; файлууд хэвээр.", "path=<хавтас>", true),
        new("sync-decide", "Зөрүүтэй файлын шийдвэр: theirs = нөгөө машины хувилбарыг авах (энэ машиных .erks-sync\\replaced-д хадгалагдана), mine = энэ машиных үлдэж нөгөө машин авна.", "folder=<id> choice=theirs|mine [peer=<түлхүүр>] [path=<файл>]", true),
        new("sync-unregister", "«Синк хавтсыг цуцлах»: төслийн синк хавтсын бүртгэлийг бүх багт цуцална; файлууд машин бүр дээр хэвээр.", "project=<төслийн код>", true),
        new("collab-status", "(унших) CGA хамтын файл (К1): хавтас бүрийн хамтын DWG, бичигч бүрийн логийн сүүлийн дугаар, хэрэглэсэн, хүлээгдэж буй, дутуу дугаар — файлын нэргүй.", "", false),
        new("sync-central", "Erk-S Central: энэ машин дээрх Revit central-ын хамтын ажиллагааг асаах/унтраах (Studio-гийн товчтой ижил). Эхний туршилтыг хуулбар дээр.", "path=<central .rvt бүтэн зам> on=true|false", true),
        new("dokany-install", "Erk-S Central-д хэрэгтэй Dokany драйверыг суулгана — «Dokany суулгах» товчтой ижил (албан ёсны msi, хэш + гарын үсэг шалгана; Windows-ийн UAC-ийг хүн зөвшөөрнө).", "", true),
        new("team-list", "(унших) Нээлттэй төслийн баг: гишүүд, хүлээгдэж буй урилга, урьж болох role-ууд.", "", false),
        new("team-invite", "«Хамтын ажил» › «Гишүүн урих»: бүртгэлтэй хэрэглэгчийг нээлттэй төслийн багт урина (и-мэйл зөвхөн аргумент — лог, Twin-д бичигдэхгүй).", "email=<и-мэйл> role=<код[,код]> confirm=true [project=<төслийн код>]", true),
        new("team-invitations", "(унших) Энэ бүртгэлд ирсэн, хүлээгдэж буй төслийн урилгууд.", "", false),
        new("team-accept", "«Мэдэгдэл» › «Зөвшөөрөх»: төслийн урилгыг хүлээн авч багт нэгдэнэ.", "invitation=<id> confirm=true", true),
        new("net-diag", "(унших) «Холболт шалгах» товчтой ижил: энэ машинаас сервер рүү холбогдох байдал — цаг, прокси, DNS, SSL гэрчилгээ (олгогч, хугацаа, chain status), сүүлийн холболтын алдааны код.", "[server=<url>]", false),
        new("close", "Studio-г хэвийнээр хаана.", "", true),
    ];

    public static StudioBridgeAction? Find(string? name) =>
        All.FirstOrDefault(action => action.Name.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// While the bridge works, a dialog that would wait for a person answers the
/// safe way at once - OK, or No/Cancel - and its text is kept for the caller.
/// </summary>
/// <remarks>
/// Some notices are posted after the action returns (the «Төсөл нээгдлээ»
/// summary runs at idle), so quiet lasts <see cref="GraceSeconds"/> past it.
/// </remarks>
internal sealed class StudioBridgeQuietMode
{
    public const int GraceSeconds = 15;
    private const int MaxNotices = 50;

    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private readonly List<string> notices = [];
    private int depth;
    private DateTimeOffset quietUntil = DateTimeOffset.MinValue;

    public StudioBridgeQuietMode(Func<DateTimeOffset>? clock = null) =>
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);

    public static StudioBridgeQuietMode Shared { get; } = new();

    public bool IsActive
    {
        get
        {
            lock (gate)
                return depth > 0 || clock() < quietUntil;
        }
    }

    public IDisposable Enter()
    {
        lock (gate)
            depth++;
        return new Scope(this);
    }

    public MessageBoxResult Answer(string title, string message, MessageBoxButton buttons)
    {
        MessageBoxResult answer = buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel,
        };
        lock (gate)
        {
            notices.Add($"[{title}] {message} → {answer}");
            if (notices.Count > MaxNotices)
                notices.RemoveAt(0);
        }
        return answer;
    }

    public IReadOnlyList<string> Drain()
    {
        lock (gate)
        {
            string[] drained = [.. notices];
            notices.Clear();
            return drained;
        }
    }

    private void Leave()
    {
        lock (gate)
        {
            depth = Math.Max(0, depth - 1);
            quietUntil = clock().AddSeconds(GraceSeconds);
        }
    }

    private sealed class Scope(StudioBridgeQuietMode owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Leave();
        }
    }
}

/// <summary>
/// The pipe server: one request per connection, answered by the handler,
/// for this Windows user only.
/// </summary>
internal sealed class StudioBridgeServer : IDisposable
{
    private readonly string pipeName;
    private readonly Func<StudioBridgeRequest, Task<StudioBridgeResponse>> handler;
    private readonly CancellationTokenSource stop = new();
    private Task? loop;

    public StudioBridgeServer(string pipeName, Func<StudioBridgeRequest, Task<StudioBridgeResponse>> handler)
    {
        this.pipeName = pipeName;
        this.handler = handler;
    }

    public string PipeName => pipeName;

    public void Start() => loop ??= Task.Run(() => ListenAsync(stop.Token));

    private async Task ListenAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                await ServeAsync(pipe, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // A client that hung up mid-line costs that one request only.
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
        {
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        }
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellation)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        string? line = await reader.ReadLineAsync(cancellation).ConfigureAwait(false);
        StudioBridgeResponse response;
        string id = "";
        try
        {
            StudioBridgeRequest request = StudioBridgeProtocol.ParseRequest(line ?? "");
            id = request.Id;
            response = await handler(request).ConfigureAwait(false);
        }
        catch (StudioBridgeException exception)
        {
            response = StudioBridgeProtocol.Refuse(id, exception.ReasonCode, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            response = StudioBridgeProtocol.Refuse(id, "action_failed", exception.GetType().Name + ": " + exception.Message);
        }
        await writer.WriteLineAsync(StudioBridgeProtocol.Serialize(response)).ConfigureAwait(false);
        try
        {
            pipe.WaitForPipeDrain();
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        stop.Dispose();
    }
}
