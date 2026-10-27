using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using System.Runtime.Versioning;

namespace ErkS.Sync.Core;

/// <summary>One line each way on the SYN pipe: a verb and a folder path.</summary>
/// <param name="Args">
/// Named arguments of the collab verbs (fileId, writer, manifest …); absent for the older verbs. Read as
/// text whatever JSON kind they come as (true, 2, "x"): a caller that sends a number or a boolean is
/// answered, not cut off (Erk-S Sync, 2026-10-27).
/// </param>
public sealed record SyncPipeRequest(
    string Verb,
    string Path,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(SyncPipeArgsConverter))] Dictionary<string, string>? Args = null);

/// <summary>Named arguments as text: a string as it is, any other value as its JSON text; null ones are left out.</summary>
public sealed class SyncPipeArgsConverter : System.Text.Json.Serialization.JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("«args» нь объект байх ёстой.");
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
                continue;
            args[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
        }
        return args;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string name, string text) in value)
            writer.WriteString(name, text);
        writer.WriteEndObject();
    }
}

/// <param name="Data">A JSON answer for the verbs that return one (collab-pull, collab-status …).</param>
/// <param name="NeedsCheckpoint">collab-pull only: the DWG needs the newest checkpoint as its base (К5).</param>
public sealed record SyncPipeResponse(
    bool Ok,
    string Message,
    string? Data = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? NeedsCheckpoint = null);

/// <summary>
/// The running SYN's pipe. Named for the Windows user, opened to that user only - the Explorer
/// verbs, Studio and the bridge talk to the one tray process through it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SyncProtocol
{
    /// <summary>Twin watches for «ErkS.Sync.*» pipes (Master, 2026-10-23).</summary>
    public static string PipeName =>
        "ErkS.Sync." + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

    public static string MutexName => @"Local\ErkS.Sync.SingleInstance";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T? Deserialize<T>(string? line) =>
        string.IsNullOrWhiteSpace(line) ? default : JsonSerializer.Deserialize<T>(line, Json);

    /// <summary>
    /// Reads one request line. A line that is not one (not JSON, no verb) gives null and says why - the
    /// caller is answered with that, never left with a closed pipe (Erk-S Sync, 2026-10-27: CGA takes
    /// silence for an old SYN).
    /// </summary>
    public static SyncPipeRequest? ReadRequest(string? line, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(line))
        {
            error = "Хүсэлт хоосон ирлээ.";
            return null;
        }
        SyncPipeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SyncPipeRequest>(line, Json);
        }
        catch (JsonException exception)
        {
            error = line.Contains('\\') && !line.Contains(@"\\")
                ? $@"Хүсэлт JSON биш ({exception.Message}) — замын «\» тэмдэгтийг JSON-д «\\» болгож бичнэ үү."
                : $"Хүсэлт JSON биш ({exception.Message}).";
            return null;
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Verb))
        {
            error = "Хүсэлтэд «verb» алга.";
            return null;
        }
        return request with { Path = request.Path ?? "" };
    }

    public static NamedPipeServerStream CreateServer() => CreateServer(PipeName);

    /// <summary>A pipe of another name: a test serves its own beside the running SYN.</summary>
    public static NamedPipeServerStream CreateServer(string name)
    {
        var security = new PipeSecurity();
        SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    /// <summary>Sends one request to the running SYN; null when none answers.</summary>
    public static SyncPipeResponse? TrySend(SyncPipeRequest request, int timeoutMilliseconds = 3000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(timeoutMilliseconds);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine(Serialize(request));
            return Deserialize<SyncPipeResponse>(reader.ReadLine());
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// One connection on the SYN pipe: a request line in, always one answer line out - an unreadable
/// request, or a verb that fails inside, is answered {"ok":false,"message":…} and kept in the fault
/// log; the connection is never just closed (Erk-S Sync, 2026-10-27).
/// </summary>
[SupportedOSPlatform("windows")]
public static class SyncPipeServer
{
    /// <param name="hold">A verb that keeps the connection (a subscription) answers itself and returns true; the reader and writer are then its own.</param>
    /// <returns>True when <paramref name="hold"/> kept the connection.</returns>
    public static async Task<bool> ServeAsync(
        Stream pipe,
        Func<SyncPipeRequest, StreamReader, StreamWriter, Task<bool>> hold,
        Func<SyncPipeRequest, CancellationToken, Task<SyncPipeResponse>> handle,
        Action<string, Exception> fault,
        CancellationToken cancellationToken)
    {
        var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        SyncPipeRequest? request = SyncProtocol.ReadRequest(await reader.ReadLineAsync(cancellationToken), out string error);
        if (request is not null && await hold(request, reader, writer))
            return true; // its reader and writer stay with whoever holds it
        SyncPipeResponse response;
        try
        {
            response = request is null ? new SyncPipeResponse(false, error) : await handle(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            fault(request?.Verb ?? "", exception);
            response = new SyncPipeResponse(false, $"Erk-S Sync дотоод алдаа ({exception.GetType().Name}): {exception.Message}");
        }
        await writer.WriteLineAsync(SyncProtocol.Serialize(response));
        writer.Dispose();
        reader.Dispose();
        return false;
    }
}
