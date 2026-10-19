using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// 🔴 THE OWNER'S RULE, 2026-10-20: «цаашид powershell эсвэл cmd ашиглаад
/// студиог гаднаас удирдах боломжтой болгож ком удирдалгүй гаднаас нь
/// гүүрээр ажилдаг болгоорой». Agents drove Studio by clicking pixels on the
/// owner's screen - a second process spawned, focus lost to the taskbar, file
/// pickers out of reach. The bridge is a pipe only this user can open; every
/// action calls the same function as the button.
/// </summary>
public sealed class THESTUDIOIsDrivenFromOutsideThroughTheBRIDGETests
{
    [Fact]
    public void ARequestReadsFromOneLineOfJson()
    {
        StudioBridgeRequest request = StudioBridgeProtocol.ParseRequest(
            """{"id":"7","action":"open-project","args":{"path":"C:\\p\\project.erksproject"}}""");

        Assert.Equal("7", request.Id);
        Assert.Equal("open-project", request.Action);
        Assert.Equal(@"C:\p\project.erksproject", request.Arg("path"));
        Assert.Null(request.Arg("missing"));
    }

    [Fact]
    public void ABrokenRequestIsAnAnswerNotACrash()
    {
        StudioBridgeResponse response = StudioBridgeProtocol.Refuse("", "bad_request", "JSON уншигдсангүй");
        string line = StudioBridgeProtocol.Serialize(response);

        Assert.DoesNotContain('\n', line);
        using JsonDocument document = JsonDocument.Parse(line);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("bad_request", document.RootElement.GetProperty("reasonCode").GetString());
        Assert.Throws<StudioBridgeException>(() => StudioBridgeProtocol.ParseRequest("{not json"));
    }

    [Fact]
    public void ThePipeIsNamedForItsProcess()
    {
        Assert.Equal("ErkS.Studio.Bridge.4242", StudioBridgeProtocol.PipeName(4242));
        Assert.True(StudioBridgeProtocol.TryParsePipeProcessId("ErkS.Studio.Bridge.4242", out int pid));
        Assert.Equal(4242, pid);
        Assert.False(StudioBridgeProtocol.TryParsePipeProcessId("ErkS.Other.4242", out _));
    }

    [Fact]
    public void TheCatalogNamesEveryActionOnceWithAMongolianDescription()
    {
        IReadOnlyList<StudioBridgeAction> actions = StudioBridgeActionCatalog.All;

        Assert.Equal(actions.Count, actions.Select(action => action.Name).Distinct().Count());
        Assert.All(actions, action => Assert.False(string.IsNullOrWhiteSpace(action.DescriptionMn)));
        foreach (string required in new[] { "actions", "status", "open-project", "check-sources", "rebuild-album", "sync-preview", "sync", "relink-source", "import-front-matter", "notices", "close" })
            Assert.Contains(actions, action => action.Name == required);
        Assert.True(StudioBridgeActionCatalog.Find("sync")!.Mutates);
        Assert.False(StudioBridgeActionCatalog.Find("status")!.Mutates);
    }

    [Fact]
    public void AQuietDialogAnswersTheSafeWayAndIsKept()
    {
        var quiet = new StudioBridgeQuietMode();
        using (quiet.Enter())
        {
            Assert.True(quiet.IsActive);
            Assert.Equal(System.Windows.MessageBoxResult.OK, quiet.Answer("Гарчиг", "Мэдэгдэл", System.Windows.MessageBoxButton.OK));
            Assert.Equal(System.Windows.MessageBoxResult.No, quiet.Answer("Асуулт", "Устгах уу?", System.Windows.MessageBoxButton.YesNo));
            Assert.Equal(System.Windows.MessageBoxResult.Cancel, quiet.Answer("Асуулт", "Үргэлжлүүлэх үү?", System.Windows.MessageBoxButton.OKCancel));
            Assert.Equal(System.Windows.MessageBoxResult.Cancel, quiet.Answer("Асуулт", "?", System.Windows.MessageBoxButton.YesNoCancel));
        }

        IReadOnlyList<string> notices = quiet.Drain();
        Assert.Equal(4, notices.Count);
        Assert.Contains("Мэдэгдэл", notices[0], StringComparison.Ordinal);
        Assert.Empty(quiet.Drain());
    }

    [Fact]
    public void QuietLastsAMomentAfterTheActionSoADeferredNoticeIsCaughtToo()
    {
        var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var quiet = new StudioBridgeQuietMode(() => now);
        using (quiet.Enter())
        {
        }

        Assert.True(quiet.IsActive);
        now = now.AddSeconds(StudioBridgeQuietMode.GraceSeconds + 1);
        Assert.False(quiet.IsActive);
    }

    [Fact]
    public async Task ARequestOverThePipeGetsItsAnswer()
    {
        string name = "ErkS.Studio.Bridge.Test." + Guid.NewGuid().ToString("N");
        using var server = new StudioBridgeServer(
            name,
            request => Task.FromResult(StudioBridgeProtocol.Accept(request.Id, new { echoed = request.Action })));
        server.Start();

        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync("""{"id":"1","action":"status"}""");
        string? line = await reader.ReadLineAsync();

        using JsonDocument document = JsonDocument.Parse(line!);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("1", document.RootElement.GetProperty("id").GetString());
        Assert.Equal("status", document.RootElement.GetProperty("result").GetProperty("echoed").GetString());
    }

    [Fact]
    public async Task AFailingHandlerBecomesARefusalAndTheServerKeepsListening()
    {
        string name = "ErkS.Studio.Bridge.Test." + Guid.NewGuid().ToString("N");
        using var server = new StudioBridgeServer(
            name,
            request => request.Action == "boom"
                ? throw new InvalidOperationException("эвдэрсэн")
                : Task.FromResult(StudioBridgeProtocol.Accept(request.Id, new { })));
        server.Start();

        Assert.Contains("\"ok\":false", await RoundTrip(name, """{"id":"1","action":"boom"}"""), StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", await RoundTrip(name, """{"id":"2","action":"status"}"""), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandLineToolSpeaksToTheSamePipeAndEveryActionIsDocumented()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "stu.ps1")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string tool = File.ReadAllText(Path.Combine(directory!.FullName, "tools", "stu.ps1"));
        string doc = File.ReadAllText(Path.Combine(directory.FullName, "docs", "STU-BRIDGE.md"));

        Assert.Contains("'" + StudioBridgeProtocol.PipePrefix + "'", tool, StringComparison.Ordinal);
        foreach (StudioBridgeAction action in StudioBridgeActionCatalog.All)
            Assert.Contains("`" + action.Name + "`", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCatalogActionIsHandled()
    {
        // An action listed but not handled answers «not_implemented» at run
        // time; the switch in the view is read here so that cannot ship.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string view = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.Bridge.cs"));

        foreach (StudioBridgeAction action in StudioBridgeActionCatalog.All)
            Assert.Contains("\"" + action.Name + "\"", view, StringComparison.Ordinal);
    }

    private static async Task<string> RoundTrip(string name, string request)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(request);
        return await reader.ReadLineAsync() ?? "";
    }
}
