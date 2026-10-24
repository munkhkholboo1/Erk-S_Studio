using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>
/// One call or one answer on the road to a central's host: [4-byte header length][JSON header][data].
/// </summary>
public static class SyncCentralFrame
{
    public static byte[] Encode<T>(T header, ReadOnlySpan<byte> data)
    {
        byte[] json = SyncCentralWire.Encode(header);
        byte[] frame = new byte[4 + json.Length + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        data.CopyTo(frame.AsSpan(4 + json.Length));
        return frame;
    }

    public static (T Header, byte[] Data) Decode<T>(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
            throw new InvalidDataException("central frame cut short");
        int length = BinaryPrimitives.ReadInt32BigEndian(frame);
        if (length < 0 || length > frame.Length - 4)
            throw new InvalidDataException("central frame header too long");
        return (SyncCentralWire.Decode<T>(frame.Slice(4, length)), frame[(4 + length)..].ToArray());
    }
}

/// <summary>A message on the relay: binary (a frame, with the server's 16-byte session on the host side) or text (control).</summary>
public sealed record SyncCentralRelayMessage(bool IsText, byte[] Payload);

/// <summary>The relay connection as the clients see it - a WebSocket on the server, an in-memory pair in tests.</summary>
public interface ISyncCentralRelayLink : IAsyncDisposable
{
    Task SendAsync(SyncCentralRelayMessage message, CancellationToken cancellationToken);

    /// <summary>The next message, or null when the link closed.</summary>
    Task<SyncCentralRelayMessage?> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>SRV 719cdea: a WebSocket to /central/{centralId}/connect.</summary>
public sealed class SyncCentralWebSocketLink(WebSocket socket) : ISyncCentralRelayLink
{
    private readonly SemaphoreSlim _send = new(1, 1);

    public static async Task<SyncCentralWebSocketLink> ConnectAsync(Uri server, string deviceToken, string folderId, string centralId, string role, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + deviceToken);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        var builder = new UriBuilder(server)
        {
            Scheme = server.Scheme == "http" ? "ws" : "wss",
            Path = $"/api/cloud-era/v1/sync/device/folders/{Uri.EscapeDataString(folderId)}/central/{Uri.EscapeDataString(centralId)}/connect",
            Query = "role=" + role,
        };
        await socket.ConnectAsync(builder.Uri, cancellationToken);
        return new SyncCentralWebSocketLink(socket);
    }

    public async Task SendAsync(SyncCentralRelayMessage message, CancellationToken cancellationToken)
    {
        await _send.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(message.Payload, message.IsText ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, cancellationToken);
        }
        finally
        {
            _send.Release();
        }
    }

    public async Task<SyncCentralRelayMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] piece = new byte[64 * 1024];
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(piece, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            buffer.Write(piece, 0, result.Count);
            if (result.EndOfMessage)
                return new SyncCentralRelayMessage(result.MessageType == WebSocketMessageType.Text, buffer.ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
        }
        socket.Dispose();
        _send.Dispose();
    }
}

/// <summary>
/// The host's side of the relay: members' calls in, answers out. A member is its device: a
/// reconnecting device (a new server session, the same deviceKeyId) resumes its old host session
/// - its handles, its unfinished burst, its answered calls - within the lease; a member that does
/// not come back within the lease is ended and its unfinished burst rolled back.
/// </summary>
public sealed class SyncCentralRelayHost(SyncCentralHost host)
{
    private readonly ConcurrentDictionary<Guid, long> _relaySessions = new();
    private readonly ConcurrentDictionary<string, long> _devices = new(StringComparer.OrdinalIgnoreCase);
    private long _next;

    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Members on the road now.</summary>
    public int Connected => _relaySessions.Count;

    /// <summary>Serves one link until it closes; the caller reconnects and calls again.</summary>
    public async Task ServeAsync(ISyncCentralRelayLink link, CancellationToken cancellationToken)
    {
        while (await link.ReceiveAsync(cancellationToken) is { } message)
        {
            if (message.IsText)
            {
                Control(message.Payload);
                continue;
            }
            if (message.Payload.Length < 16)
                continue;
            var relay = new Guid(message.Payload.AsSpan(0, 16));
            if (!_relaySessions.TryGetValue(relay, out long session))
                continue;
            (SyncCentralRequest request, byte[] data) = SyncCentralFrame.Decode<SyncCentralRequest>(message.Payload.AsSpan(16));
            SyncCentralResponse response = await Task.Run(() => host.Handle(session, request, data, out byte[]? reply).WithReply(reply, out data), cancellationToken);
            byte[] frame = SyncCentralFrame.Encode(response with { Seq = request.Seq }, data);
            byte[] answer = new byte[16 + frame.Length];
            relay.TryWriteBytes(answer);
            frame.CopyTo(answer, 16);
            await link.SendAsync(new SyncCentralRelayMessage(false, answer), cancellationToken);
        }
    }

    /// <summary>Ends the sessions of devices gone longer than the lease; connected members are never swept.</summary>
    public int Sweep(DateTimeOffset now)
    {
        HashSet<long> live = _relaySessions.Values.ToHashSet();
        return host.Sweep(now, Lease, session => live.Contains(session));
    }

    private void Control(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        string type = root.TryGetProperty("type", out JsonElement value) ? value.GetString() ?? "" : "";
        if (!root.TryGetProperty("session", out JsonElement sessionText) || !Guid.TryParseExact(sessionText.GetString(), "N", out Guid relay))
            return;
        if (type == "member-open")
        {
            string device = root.TryGetProperty("deviceKeyId", out JsonElement key) ? key.GetString() ?? "" : "";
            long session = _devices.GetOrAdd(device.Length > 0 ? device : relay.ToString("N"), _ => Interlocked.Increment(ref _next));
            _relaySessions[relay] = session;
        }
        else if (type == "member-closed")
        {
            // Not ended here: the device may already be back on a newer connection, or come
            // back within the lease to finish what it started. The sweep decides.
            _relaySessions.TryRemove(relay, out _);
        }
    }
}

internal static class SyncCentralResponseExtensions
{
    public static SyncCentralResponse WithReply(this SyncCentralResponse response, byte[]? reply, out byte[] data)
    {
        data = reply ?? [];
        return response;
    }
}

/// <summary>
/// The member's road to the host through the relay. Calls are numbered; one whose answer did not
/// come is resent with the same number after a reconnect (the host answers it from memory). When
/// there is no road, or no answer within <see cref="CallTimeout"/>, the call fails at once with
/// «host_unreachable» - Revit then behaves as with an unreachable central, never hangs.
/// </summary>
public sealed class SyncCentralRelayChannel : ISyncCentralChannel, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<ISyncCentralRelayLink>> _connect;
    private readonly ConcurrentDictionary<long, Pending> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _linkGate = new(1, 1);
    private ISyncCentralRelayLink? _link;
    private TaskCompletionSource _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _seq;

    public SyncCentralRelayChannel(Func<CancellationToken, Task<ISyncCentralRelayLink>> connect) => _connect = connect;

    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public bool Connected => _link is not null;

    public async Task<(SyncCentralResponse Response, byte[]? Data)> CallAsync(SyncCentralRequest request, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        long seq = Interlocked.Increment(ref _seq);
        var pending = new Pending(SyncCentralFrame.Encode(request with { Seq = seq }, data.Span));
        _pending[seq] = pending;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CallTimeout);
            while (!pending.Answer.Task.IsCompleted)
            {
                ISyncCentralRelayLink? link = await LinkAsync(timeout.Token);
                if (link is null)
                    return Unreachable();
                Task lost = _lost.Task;
                try
                {
                    if (!pending.Sent)
                    {
                        await link.SendAsync(new SyncCentralRelayMessage(false, pending.Frame), timeout.Token);
                        pending.Sent = true;
                    }
                    // Woken by the answer or by the road breaking - then round again on a new link.
                    await Task.WhenAny(pending.Answer.Task, lost).WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Unreachable();
                }
                catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException)
                {
                    await DropAsync(link); // the road broke: every unanswered call is resent on the next link
                }
            }
            return await pending.Answer.Task;
        }
        finally
        {
            _pending.TryRemove(seq, out _);
        }
    }

    private static (SyncCentralResponse, byte[]?) Unreachable() =>
        (new SyncCentralResponse { Error = SyncCentralErrors.HostUnreachable }, null);

    private async Task<ISyncCentralRelayLink?> LinkAsync(CancellationToken cancellationToken)
    {
        if (_link is { } current)
            return current;
        await _linkGate.WaitAsync(cancellationToken);
        try
        {
            if (_link is not null)
                return _link;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            ISyncCentralRelayLink link;
            try
            {
                link = await _connect(timeout.Token);
            }
            catch (Exception exception) when (exception is WebSocketException or IOException or OperationCanceledException or HttpRequestException or SyncServerException)
            {
                return null;
            }
            _link = link;
            foreach (Pending pending in _pending.Values)
                pending.Sent = false; // resend what the old road swallowed - same numbers
            _ = ReadAsync(link);
            return link;
        }
        finally
        {
            _linkGate.Release();
        }
    }

    private async Task ReadAsync(ISyncCentralRelayLink link)
    {
        try
        {
            while (await link.ReceiveAsync(_stop.Token) is { } message)
            {
                if (message.IsText)
                    continue;
                (SyncCentralResponse response, byte[] data) = SyncCentralFrame.Decode<SyncCentralResponse>(message.Payload);
                if (_pending.TryGetValue(response.Seq, out Pending? pending))
                    pending.Answer.TrySetResult((response, data.Length == 0 ? null : data));
            }
        }
        catch (Exception exception) when (exception is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException or JsonException)
        {
        }
        await DropAsync(link);
    }

    private async Task DropAsync(ISyncCentralRelayLink link)
    {
        if (Interlocked.CompareExchange(ref _link, null, link) == link)
        {
            foreach (Pending pending in _pending.Values)
                pending.Sent = false;
            Interlocked.Exchange(ref _lost, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            await link.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_link is { } link)
            await DropAsync(link);
        _stop.Dispose();
        _linkGate.Dispose();
    }

    private sealed class Pending(byte[] frame)
    {
        public byte[] Frame { get; } = frame;

        public volatile bool Sent;

        public TaskCompletionSource<(SyncCentralResponse, byte[]?)> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
