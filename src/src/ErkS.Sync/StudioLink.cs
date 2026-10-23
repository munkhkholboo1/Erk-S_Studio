using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ErkS.Sync;

/// <summary>
/// Hands a person's decision to Studio.
///
/// 🔴 REGISTERING AND JOINING ARE A PERSON'S ACT ON A PROJECT (SRV contract: they need the
/// person's Cloud ERA rights). SYN holds only the device's own token, so «фолдер болгох» and
/// «фолдерт холбох» go to Studio through its bridge - and Studio is started when it is closed.
/// </summary>
internal static class StudioLink
{
    private const string BridgePrefix = "ErkS.Studio.Bridge.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Asks Studio to run a bridge action; starts Studio and waits for it when none runs.</summary>
    public static async Task<(bool Ok, string Message)> RunAsync(string action, IReadOnlyDictionary<string, string> args, CancellationToken cancellationToken)
    {
        int? pid = RunningStudio();
        if (pid is null)
        {
            if (!TryStartStudio())
                return (false, "Erk-S Studio олдсонгүй. Studio-г суулгаад дахин оролдоно уу.");
            for (int attempt = 0; attempt < 90 && pid is null; attempt++)
            {
                await Task.Delay(1000, cancellationToken);
                pid = RunningStudio();
            }
            if (pid is null)
                return (false, "Studio нээгдсэнгүй.");
        }

        // The bridge pipe appears once Studio has finished starting; wait for it.
        for (int attempt = 0; attempt < 120; attempt++)
        {
            (bool Ok, string Message)? answer = await TrySendAsync(pid.Value, action, args, cancellationToken);
            if (answer is not null)
                return answer.Value;
            await Task.Delay(1000, cancellationToken);
        }
        return (false, "Studio хариу өгсөнгүй.");
    }

    private static int? RunningStudio() =>
        Process.GetProcessesByName("ErkS.Studio")
            .Where(process => process.SessionId == Process.GetCurrentProcess().SessionId)
            .Select(process => (int?)process.Id)
            .FirstOrDefault();

    private static bool TryStartStudio()
    {
        string studio = Path.Combine(AppContext.BaseDirectory, "ErkS.Studio.exe");
        if (!File.Exists(studio))
            return false;
        Process.Start(new ProcessStartInfo(studio) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
        return true;
    }

    private static async Task<(bool Ok, string Message)?> TrySendAsync(int pid, string action, IReadOnlyDictionary<string, string> args, CancellationToken cancellationToken)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", BridgePrefix + pid, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(1500, cancellationToken);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), action, args }, Json));
            string? line = await reader.ReadLineAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(line ?? "{}");
            bool ok = document.RootElement.TryGetProperty("ok", out JsonElement okElement) && okElement.GetBoolean();
            string message = document.RootElement.TryGetProperty("message", out JsonElement messageElement) ? messageElement.GetString() ?? "" : "";
            return (ok, message);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
