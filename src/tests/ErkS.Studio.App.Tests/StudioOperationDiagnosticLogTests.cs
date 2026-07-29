using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioOperationDiagnosticLogTests
{
    [Fact]
    public void TryWrite_WritesOneSanitizedJsonLineWithCorrelationFields()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var log = new StudioOperationDiagnosticLog(
                root,
                utcNow: () => new DateTimeOffset(2026, 7, 29, 1, 2, 3, TimeSpan.Zero));

            bool written = log.TryWrite(
                new StudioOperationDiagnosticEvent(
                    "operation-1",
                    "cloud_sync",
                    "error",
                    "album_conflict",
                    """
                    Upload failed. Authorization: Bearer secret-token.
                    Native: C:\Users\member\Documents\private-source.rvt
                    {"accessToken":"raw-response-secret"}
                    """,
                    "project-1",
                    "Member@Example.com",
                    "device-fingerprint",
                    "server-trace-1",
                    new Dictionary<string, string[]>
                    {
                        ["componentCode"] = ["source:actual"],
                        ["expectedCode"] = ["source:expected"],
                        ["nativePath"] = [@"C:\Users\member\Documents\private-source.rvt"],
                    }));

            Assert.True(written);
            Assert.Equal(
                Path.Combine(root, "logs", "studio-operations.jsonl"),
                log.LogPath);
            string line = Assert.Single(File.ReadAllLines(log.LogPath));
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement entry = document.RootElement;

            Assert.Equal("2026-07-29T01:02:03+00:00", entry.GetProperty("timestampUtc").GetString());
            Assert.Equal("operation-1", entry.GetProperty("operationId").GetString());
            Assert.Equal("cloud_sync", entry.GetProperty("operation").GetString());
            Assert.Equal("error", entry.GetProperty("outcome").GetString());
            Assert.Equal("album_conflict", entry.GetProperty("reasonCode").GetString());
            Assert.Equal("project-1", entry.GetProperty("projectId").GetString());
            Assert.Equal("member@example.com", entry.GetProperty("account").GetString());
            Assert.Equal("device-fingerprint", entry.GetProperty("device").GetString());
            Assert.Equal("server-trace-1", entry.GetProperty("serverTraceId").GetString());
            JsonElement details = entry.GetProperty("details");
            Assert.Equal("source:actual", details.GetProperty("componentCode")[0].GetString());
            Assert.False(details.TryGetProperty("nativePath", out _));

            string message = entry.GetProperty("message").GetString() ?? "";
            Assert.Contains("Upload failed", message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-token", line, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-response-secret", line, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\Users\member", line, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryWrite_RollsCurrentLogBeforeAppendingPastConfiguredLimit()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var log = new StudioOperationDiagnosticLog(
                root,
                maxFileBytes: 360,
                utcNow: () => new DateTimeOffset(2026, 7, 29, 1, 2, 3, TimeSpan.Zero));

            Assert.True(log.TryWrite(Event("first", new string('a', 180))));
            Assert.True(log.TryWrite(Event("second", new string('b', 180))));

            string[] files = Directory.GetFiles(log.LogDirectory, "studio-operations*.jsonl");
            Assert.Equal(2, files.Length);
            Assert.Contains("second", File.ReadAllText(log.LogPath), StringComparison.Ordinal);
            string rotated = Assert.Single(files, path => !path.Equals(log.LogPath, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("first", File.ReadAllText(rotated), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryWrite_WhenLogDirectoryCannotBeCreated_DoesNotThrowOrBlockCaller()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string fileInsteadOfDirectory = Path.Combine(root, "not-a-directory");
            File.WriteAllText(fileInsteadOfDirectory, "occupied");
            var log = new StudioOperationDiagnosticLog(fileInsteadOfDirectory);

            Exception? error = Record.Exception(() =>
                Assert.False(log.TryWrite(Event("blocked", "diagnostic only"))));

            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AccountException_PreservesServerTraceIdentifier()
    {
        var error = new StudioAccountException(
            "Conflict",
            System.Net.HttpStatusCode.Conflict,
            "album_revision_conflict",
            "server-operation-42",
            new Dictionary<string, string[]>
            {
                ["componentCode"] = ["source:actual"],
                ["ownerEmail"] = ["owner@example.com"],
            },
            currentSourceId: "source-current",
            currentRevisionId: "revision-current");

        Assert.Equal("server-operation-42", error.TraceId);
        Assert.Equal("source:actual", error.FieldErrors["componentCode"].Single());
        Assert.Equal("source-current", error.CurrentSourceId);
        Assert.Equal("revision-current", error.CurrentRevisionId);
        Assert.Contains(
            "componentCode=source:actual",
            StudioCloudErrorDetails.SafeSummary(error.FieldErrors),
            StringComparison.Ordinal);
        Assert.Contains(
            "currentSourceId=source-current",
            StudioCloudErrorDetails.SafeSummary(error.FieldErrors),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TraceIdentifier_FallsBackToServerOperationHeader()
    {
        using var response = new System.Net.Http.HttpResponseMessage(
            System.Net.HttpStatusCode.Conflict);
        response.Headers.TryAddWithoutValidation(
            StudioCloudTraceIdentifier.OperationIdHeader,
            "server-header-operation");

        Assert.Equal(
            "server-header-operation",
            StudioCloudTraceIdentifier.Resolve(response, error: null));
    }

    private static StudioOperationDiagnosticEvent Event(string id, string message) =>
        new(
            id,
            "source_refresh",
            "completed",
            "source_refresh_completed",
            message,
            "project-1",
            "member@example.com",
            "device-1",
            "",
            null);

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-studio-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
