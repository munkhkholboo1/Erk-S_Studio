using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErkS.Studio;

internal sealed record StudioOperationDiagnosticEvent(
    string OperationId,
    string Operation,
    string Outcome,
    string ReasonCode,
    string Message,
    string ProjectId,
    string Account,
    string Device,
    string ServerTraceId,
    IReadOnlyDictionary<string, string[]>? Details = null);

/// <summary>
/// Best-effort, privacy-bounded Studio operation diagnostics.
/// Logging must never become a dependency of Source Refresh or Cloud Sync.
/// </summary>
internal sealed class StudioOperationDiagnosticLog
{
    private const long DefaultMaxFileBytes = 10L * 1024L * 1024L;
    private static readonly Regex BearerToken = new(
        @"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveAssignment = new(
        @"(?i)\b(?:access[_-]?token|refresh[_-]?token|authorization|password|secret)\b\s*[:=]\s*[""']?[^,\s;""']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JsonObject = new(
        @"(?s)\{.*?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsAbsolutePath = new(
        @"(?i)\b[a-z]:\\[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UncAbsolutePath = new(
        @"\\\\[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object gate = new();
    private readonly long maxFileBytes;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public StudioOperationDiagnosticLog(
        string? dataRoot = null,
        long maxFileBytes = DefaultMaxFileBytes,
        Func<DateTimeOffset>? utcNow = null)
    {
        string resolvedRoot = ResolveDataRoot(dataRoot);
        LogDirectory = Path.Combine(resolvedRoot, "logs");
        LogPath = Path.Combine(LogDirectory, "studio-operations.jsonl");
        this.maxFileBytes = maxFileBytes > 0 ? maxFileBytes : DefaultMaxFileBytes;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string LogDirectory { get; }

    public string LogPath { get; }

    public static string CreateOperationId() => Guid.NewGuid().ToString("N");

    public bool TryWrite(StudioOperationDiagnosticEvent diagnosticEvent)
    {
        try
        {
            DateTimeOffset timestamp = utcNow();
            var entry = new StudioOperationDiagnosticEntry
            {
                TimestampUtc = timestamp,
                OperationId = CleanValue(diagnosticEvent.OperationId, 128),
                Operation = CleanValue(diagnosticEvent.Operation, 80),
                Outcome = CleanValue(diagnosticEvent.Outcome, 40),
                ReasonCode = CleanValue(diagnosticEvent.ReasonCode, 120),
                Message = SanitizeMessage(diagnosticEvent.Message),
                ProjectId = CleanValue(diagnosticEvent.ProjectId, 160),
                Account = CleanValue(diagnosticEvent.Account, 320).ToLowerInvariant(),
                Device = CleanValue(diagnosticEvent.Device, 320),
                ServerTraceId = CleanValue(diagnosticEvent.ServerTraceId, 320),
                Details = StudioCloudErrorDetails.ForDiagnosticLog(diagnosticEvent.Details),
            };
            string line = JsonSerializer.Serialize(entry, jsonOptions) + Environment.NewLine;
            int lineBytes = Encoding.UTF8.GetByteCount(line);

            lock (gate)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogPath) &&
                    new FileInfo(LogPath).Length > 0 &&
                    new FileInfo(LogPath).Length + lineBytes > maxFileBytes)
                {
                    RollCurrentLog(timestamp);
                }

                File.AppendAllText(LogPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string SanitizeMessage(string? message)
    {
        string value = message ?? "";
        value = JsonObject.Replace(value, "[server response omitted]");
        value = WindowsAbsolutePath.Replace(value, "[local path omitted]");
        value = UncAbsolutePath.Replace(value, "[local path omitted]");
        value = BearerToken.Replace(value, "Bearer [redacted]");
        value = SensitiveAssignment.Replace(value, "[sensitive value redacted]");
        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (value.Contains("  ", StringComparison.Ordinal))
            value = value.Replace("  ", " ", StringComparison.Ordinal);
        return value.Length <= 2000 ? value : value[..2000] + "…";
    }

    private static string ResolveDataRoot(string? dataRoot)
    {
        string configured = string.IsNullOrWhiteSpace(dataRoot)
            ? Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT")?.Trim() ?? ""
            : dataRoot.Trim();
        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Erk-S Studio");

        try
        {
            return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? fallback : configured);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return Path.GetFullPath(fallback);
        }
    }

    private static string CleanValue(string? value, int maxLength)
    {
        string cleaned = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private void RollCurrentLog(DateTimeOffset timestamp)
    {
        string stamp = timestamp.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ");
        string rolledPath = Path.Combine(
            LogDirectory,
            $"studio-operations.{stamp}.jsonl");
        if (File.Exists(rolledPath))
        {
            rolledPath = Path.Combine(
                LogDirectory,
                $"studio-operations.{stamp}.{Guid.NewGuid():N}.jsonl");
        }

        File.Move(LogPath, rolledPath);
    }

    private sealed class StudioOperationDiagnosticEntry
    {
        public DateTimeOffset TimestampUtc { get; set; }
        public string OperationId { get; set; } = "";
        public string Operation { get; set; } = "";
        public string Outcome { get; set; } = "";
        public string ReasonCode { get; set; } = "";
        public string Message { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Account { get; set; } = "";
        public string Device { get; set; } = "";
        public string ServerTraceId { get; set; } = "";
        public Dictionary<string, string[]>? Details { get; set; }
    }
}
