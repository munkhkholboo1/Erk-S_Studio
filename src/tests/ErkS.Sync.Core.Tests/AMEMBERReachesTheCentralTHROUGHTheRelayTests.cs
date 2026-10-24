using System.Text;
using System.Threading.Channels;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// The road for a member on another network (Будбаясгалан): calls through the server relay to
/// the host, answers back. A lost answer is resent and answered from the host's memory - never
/// run twice; a member who comes back resumes its session; no host means an at-once
/// «host_unreachable», never a hang (owner, 2026-10-24: «тогтворгүй сүлжээтэй гишүүн болон бүрэн
/// офлайн гишүүний зохицуулалтыг сайтар бодож хийгээрэй»).
/// </summary>
public sealed class AMEMBERReachesTheCentralTHROUGHTheRelayTests
{
    [Fact]
    public async Task CallsGoThroughAndALostAnswerIsResentNotRerun()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, "NP.rvt"), "model");
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var relay = new FakeRelay(new SyncCentralRelayHost(host));
        await using var channel = new SyncCentralRelayChannel(token => relay.ConnectMemberAsync("device-b"));
        var member = new SyncCentralClient(channel);

        SyncCentralResponse open = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.Read, Share = FileShare.ReadWrite });
        Assert.True(open.Ok);
        (SyncCentralResponse read, byte[] bytes) = await member.ReadAsync(open.Handle, 0, 5);
        Assert.Equal("model", Encoding.UTF8.GetString(bytes));

        // The next answer is lost on the road and the link breaks: the call is resent on a new link.
        relay.DropNextAnswer = true;
        SyncCentralResponse lockFile = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt.lock", Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None });
        Assert.True(lockFile.Ok, lockFile.Error); // not «exists»: answered from memory, not run twice
        Assert.True(relay.MemberConnections >= 2);

        // Same device, new connection: the old handles still answer.
        (SyncCentralResponse again, byte[] still) = await member.ReadAsync(open.Handle, 0, 5);
        Assert.True(again.Ok, again.Error);
        Assert.Equal("model", Encoding.UTF8.GetString(still));
    }

    [Fact]
    public async Task NoHostAnswersAtOnce()
    {
        await using var channel = new SyncCentralRelayChannel(_ => throw new IOException("host offline")) { ConnectTimeout = TimeSpan.FromSeconds(1) };
        var started = DateTime.UtcNow;

        SyncCentralResponse response = await new SyncCentralClient(channel).CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = "NP.rvt" });

        Assert.Equal(SyncCentralErrors.HostUnreachable, response.Error);
        // Well before the 20 s a call may wait on a live road - the point is no hang (a loaded machine is slower than 3 s).
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AHostOfflineRefusalIsAnsweredAtOnceNotAfterTheTimeout()
    {
        int connects = 0;
        await using var channel = new SyncCentralRelayChannel(_ =>
        {
            Interlocked.Increment(ref connects);
            return Task.FromResult<ISyncCentralRelayLink>(new RefusedLink(4404, "sync_central_host_offline"));
        });
        DateTime started = DateTime.UtcNow;

        SyncCentralResponse response = await new SyncCentralClient(channel).CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = "NP.rvt" });
        SyncCentralResponse again = await new SyncCentralClient(channel).CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Stat, Path = "NP.rvt" });

        Assert.Equal(SyncCentralErrors.HostUnreachable, response.Error);
        Assert.Equal(SyncCentralErrors.HostUnreachable, again.Error);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Equal(1, connects); // the second call waits out the pause instead of hammering the server
        Assert.Equal("sync_central_host_offline", channel.LastRefusal);
    }

    /// <summary>SRV be7ad1a: the server accepts the WebSocket and closes it at once with 4000 + status.</summary>
    private sealed class RefusedLink(int code, string reason) : ISyncCentralRelayLink
    {
        public (int Code, string Reason)? Closed => (code, reason);

        public Task SendAsync(SyncCentralRelayMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<SyncCentralRelayMessage?> ReceiveAsync(CancellationToken cancellationToken) => Task.FromResult<SyncCentralRelayMessage?>(null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AMemberGoneLongerThanTheLeaseHasItsBurstUndone()
    {
        using var folder = new TemporaryFolder();
        string central = Path.Combine(folder.Path, "NP.rvt");
        File.WriteAllText(central, "model v1");
        using var host = new SyncCentralHost(folder.Path, "NP.rvt");
        var relayHost = new SyncCentralRelayHost(host);
        var relay = new FakeRelay(relayHost);
        var channel = new SyncCentralRelayChannel(token => relay.ConnectMemberAsync("device-b"));
        var member = new SyncCentralClient(channel);
        SyncCentralResponse open = await member.CallAsync(new SyncCentralRequest { Op = SyncCentralOp.Open, Path = "NP.rvt", Access = FileAccess.ReadWrite, Share = FileShare.Read });
        await member.WriteAsync(open.Handle, 0, "HALF"u8.ToArray());

        await channel.DisposeAsync(); // the member's road is gone
        await relay.WaitForCloseAsync();
        // The host learns of the close asynchronously; until then the member counts as connected.
        for (int wait = 0; wait < 100 && relayHost.Connected > 0; wait++)
            await Task.Delay(50);
        Assert.Equal(0, relayHost.Sweep(DateTimeOffset.UtcNow));
        Assert.Equal(1, relayHost.Sweep(DateTimeOffset.UtcNow.AddMinutes(3)));

        Assert.Equal("model v1", File.ReadAllText(central));
    }

    /// <summary>The server's relay in memory: pairs members to the host with 16-byte sessions, as SRV 719cdea.</summary>
    private sealed class FakeRelay
    {
        private readonly SyncCentralRelayHost _host;
        private readonly Pipe _hostSide = new();
        private readonly Dictionary<Guid, Pipe> _members = new();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _connections;

        public FakeRelay(SyncCentralRelayHost host)
        {
            _host = host;
            _ = _host.ServeAsync(new Link(_hostSide.ToHost, _hostSide.FromHost, null), CancellationToken.None);
            _ = RouteAnswersAsync();
        }

        public bool DropNextAnswer { get; set; }

        public int MemberConnections => _connections;

        public Task WaitForCloseAsync() => _closed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public async Task<ISyncCentralRelayLink> ConnectMemberAsync(string device)
        {
            Interlocked.Increment(ref _connections);
            var session = Guid.NewGuid();
            var pipe = new Pipe();
            lock (_members)
                _members[session] = pipe;
            await _hostSide.ToHost.Writer.WriteAsync(new SyncCentralRelayMessage(true, Encoding.UTF8.GetBytes(
                $$"""{"type":"member-open","session":"{{session:N}}","deviceKeyId":"{{device}}"}""")));
            _ = ForwardCallsAsync(session, pipe);
            return new Link(pipe.FromHost, pipe.ToHost, async () =>
            {
                lock (_members)
                    _members.Remove(session);
                await _hostSide.ToHost.Writer.WriteAsync(new SyncCentralRelayMessage(true, Encoding.UTF8.GetBytes(
                    $$"""{"type":"member-closed","session":"{{session:N}}"}""")));
                _closed.TrySetResult();
            });
        }

        private async Task ForwardCallsAsync(Guid session, Pipe pipe)
        {
            await foreach (SyncCentralRelayMessage message in pipe.ToHost.Reader.ReadAllAsync())
            {
                byte[] framed = new byte[16 + message.Payload.Length];
                session.TryWriteBytes(framed);
                message.Payload.CopyTo(framed, 16);
                await _hostSide.ToHost.Writer.WriteAsync(new SyncCentralRelayMessage(false, framed));
            }
        }

        private async Task RouteAnswersAsync()
        {
            await foreach (SyncCentralRelayMessage message in _hostSide.FromHost.Reader.ReadAllAsync())
            {
                var session = new Guid(message.Payload.AsSpan(0, 16));
                Pipe? pipe;
                lock (_members)
                    _members.TryGetValue(session, out pipe);
                if (pipe is null)
                    continue;
                if (DropNextAnswer)
                {
                    DropNextAnswer = false;
                    pipe.FromHost.Writer.TryComplete(); // the road breaks with the answer on it
                    continue;
                }
                await pipe.FromHost.Writer.WriteAsync(new SyncCentralRelayMessage(false, message.Payload[16..]));
            }
        }
    }

    private sealed class Pipe
    {
        public Channel<SyncCentralRelayMessage> ToHost { get; } = Channel.CreateUnbounded<SyncCentralRelayMessage>();

        public Channel<SyncCentralRelayMessage> FromHost { get; } = Channel.CreateUnbounded<SyncCentralRelayMessage>();
    }

    private sealed class Link(Channel<SyncCentralRelayMessage> receive, Channel<SyncCentralRelayMessage> send, Func<Task>? onClose) : ISyncCentralRelayLink
    {
        public async Task SendAsync(SyncCentralRelayMessage message, CancellationToken cancellationToken)
        {
            if (!send.Writer.TryWrite(message))
                throw new IOException("link closed");
            await Task.CompletedTask;
        }

        public async Task<SyncCentralRelayMessage?> ReceiveAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await receive.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            send.Writer.TryComplete();
            if (onClose is not null)
                await onClose();
        }
    }
}
