using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using System.Runtime.Versioning;

namespace ErkS.Sync.Core;

/// <summary>One line each way on the SYN pipe: a verb and a folder path.</summary>
/// <param name="Args">Named arguments of the collab verbs (fileId, writer, manifest …); absent for the older verbs.</param>
public sealed record SyncPipeRequest(string Verb, string Path, Dictionary<string, string>? Args = null);

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

    public static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            PipeName,
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
