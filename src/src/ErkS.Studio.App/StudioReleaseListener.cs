using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ErkS.Sync.Core;

namespace ErkS.Studio;

/// <summary>
/// Hears that a new Studio was published the moment it is (owner, 2026-10-26: «цаг тутам бишээ … шинэ
/// хувилбар нийтлэхэд л мэдэгддэг байх хэрэгтэй»; Erk-S Sync, 2026-10-27: an open Studio kept saying
/// «no update» until restarted). The server tells Erk-S Sync on its events socket; Sync tells every
/// program here on its pipe («release-subscribe»). Nothing is read on a clock: while Sync is not
/// there, the connection is tried again with a growing wait (5 s … 10 min).
/// </summary>
internal static class StudioReleaseListener
{
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(10);

    public static async Task RunAsync(Action<string> studioPublished, CancellationToken cancellationToken)
    {
        TimeSpan wait = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", SyncProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(3000, cancellationToken).ConfigureAwait(false);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync(SyncProtocol.Serialize(new SyncPipeRequest("release-subscribe", ""))).ConfigureAwait(false);
                if (SyncProtocol.Deserialize<SyncPipeResponse>(await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is { Ok: true })
                {
                    wait = TimeSpan.FromSeconds(5);
                    while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                    {
                        if (StudioVersionIn(line) is { } version)
                            studioPublished(version);
                    }
                }
                else
                {
                    wait = LongestWait; // a Sync without the verb: it comes with the next update
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or JsonException or UnauthorizedAccessException)
            {
            }
            try
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, LongestWait.Ticks));
        }
    }

    /// <summary>The version in a «release» line for Studio, or null for any other line.</summary>
    internal static string? StudioVersionIn(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("event", out JsonElement kind) && kind.GetString() == "release" &&
                root.TryGetProperty("productCode", out JsonElement product) &&
                string.Equals(product.GetString(), StudioReleaseInfo.ProductCode, StringComparison.OrdinalIgnoreCase) &&
                // Studio's own channel is the main one ("" or absent); another channel is another build.
                (!root.TryGetProperty("channel", out JsonElement channel) || string.IsNullOrEmpty(channel.GetString())) &&
                root.TryGetProperty("version", out JsonElement version) && version.GetString() is { Length: > 0 } text
                ? text
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
