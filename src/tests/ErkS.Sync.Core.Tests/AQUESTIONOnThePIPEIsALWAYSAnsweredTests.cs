using System.IO.Pipes;
using System.Text;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-27: on SYN 138 the collab verbs came back as a closed pipe (ReadLine → null) and
/// CGA took that for an old SYN. A line that is not JSON (a path's «\» not doubled) or args that are not
/// text («wantCheckpoint»: true) ended the connection unanswered. Every question is answered now - over a
/// real named pipe, the way CGA asks.
/// </summary>
public sealed class AQUESTIONOnThePIPEIsALWAYSAnsweredTests
{
    [Fact]
    public async Task WHOAMIAndSTATUSAreAnsweredOverTheRealPipe()
    {
        await using var pipe = new TestPipe();

        SyncPipeResponse whoami = await pipe.AskAsync(
            SyncProtocol.Serialize(new SyncPipeRequest("collab-whoami", @"D:\Cloud Work\ZuunModSync\DWG\TuvAimagMP-V3.dwg")));
        SyncPipeResponse status = await pipe.AskAsync(
            "{\"verb\":\"collab-status\",\"path\":\"D:\\\\Cloud Work\\\\ZuunModSync\\\\DWG\\\\TuvAimagMP-V3.dwg\",\"args\":{\"fileId\":\"ee46\",\"wantCheckpoint\":true,\"seq\":2,\"writer\":null}}");

        Assert.True(whoami.Ok);
        Assert.Equal(@"whoami D:\Cloud Work\ZuunModSync\DWG\TuvAimagMP-V3.dwg", whoami.Data);
        Assert.True(status.Ok);
        Assert.Equal("fileId=ee46;seq=2;wantCheckpoint=true", status.Data); // a boolean and a number are text; null is left out
    }

    [Fact]
    public async Task ALineThatISNOTJsonIsANSWEREDWithWhy()
    {
        await using var pipe = new TestPipe();

        SyncPipeResponse rawPath = await pipe.AskAsync(@"{""verb"":""collab-whoami"",""path"":""D:\Cloud Work\a.dwg""}");
        SyncPipeResponse noVerb = await pipe.AskAsync("{\"path\":\"\"}");

        Assert.False(rawPath.Ok);
        Assert.Contains("JSON биш", rawPath.Message);
        Assert.Contains(@"«\\»", rawPath.Message); // says how to write the path
        Assert.False(noVerb.Ok);
        Assert.Contains("verb", noVerb.Message);
    }

    [Fact]
    public async Task AVerbThatFAILSInsideIsAnsweredAndLOGGED()
    {
        await using var pipe = new TestPipe();

        SyncPipeResponse answer = await pipe.AskAsync(SyncProtocol.Serialize(new SyncPipeRequest("collab-report", "D:\\x.dwg")));

        Assert.False(answer.Ok);
        Assert.Contains("InvalidOperationException", answer.Message);
        Assert.Contains("collab-report", pipe.Faults);
        // The pipe still serves the next caller.
        Assert.True((await pipe.AskAsync(SyncProtocol.Serialize(new SyncPipeRequest("collab-whoami", "D:\\x.dwg")))).Ok);
    }

    [Fact]
    public async Task ASUBSCRIPTIONKeepsItsConnection()
    {
        await using var pipe = new TestPipe();
        using var client = await pipe.ConnectAsync();
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync("{\"verb\":\"collab-subscribe\",\"path\":\"\"}");

        Assert.Equal("held", await reader.ReadLineAsync());
        Assert.Equal("event", await reader.ReadLineAsync());
    }

    /// <summary>The SYN pipe's own serving code on a pipe of its own name, beside a running SYN.</summary>
    private sealed class TestPipe : IAsyncDisposable
    {
        private readonly string _name = "ErkS.Sync.Test." + Guid.NewGuid().ToString("N");
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;

        public TestPipe() => _serving = Task.Run(() => ServeAsync(_stop.Token));

        public List<string> Faults { get; } = [];

        public async Task<NamedPipeClientStream> ConnectAsync()
        {
            var client = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);
            return client;
        }

        public async Task<SyncPipeResponse> AskAsync(string line)
        {
            using NamedPipeClientStream client = await ConnectAsync();
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(line);
            string? answer = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(answer); // never a closed pipe
            return SyncProtocol.Deserialize<SyncPipeResponse>(answer)!;
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = SyncProtocol.CreateServer(_name);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    bool held = await SyncPipeServer.ServeAsync(pipe, HoldAsync, HandleAsync, (verb, _) => Faults.Add(verb), cancellationToken);
                    if (held)
                        continue;
                }
                catch (OperationCanceledException)
                {
                }
                await pipe.DisposeAsync();
            }
        }

        private static async Task<bool> HoldAsync(SyncPipeRequest request, StreamReader reader, StreamWriter writer)
        {
            if (request.Verb != "collab-subscribe")
                return false;
            await writer.WriteLineAsync("held");
            await writer.WriteLineAsync("event");
            return true;
        }

        private static Task<SyncPipeResponse> HandleAsync(SyncPipeRequest request, CancellationToken cancellationToken) => request.Verb switch
        {
            "collab-whoami" => Task.FromResult(new SyncPipeResponse(true, "", "whoami " + request.Path)),
            "collab-status" => Task.FromResult(new SyncPipeResponse(true, "", string.Join(";", request.Args!.OrderBy(arg => arg.Key, StringComparer.Ordinal).Select(arg => arg.Key + "=" + arg.Value)))),
            _ => throw new InvalidOperationException("inside"),
        };

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _serving.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
            }
            _stop.Dispose();
        }
    }
}
