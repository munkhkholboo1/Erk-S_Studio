using System.Net.Http;

namespace ErkS.Studio;

internal static class StudioCloudDiagnosticReasonCode
{
    public static string Resolve(
        Exception exception,
        string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is StudioBuildingCompositionConflictException compositionConflict)
            return compositionConflict.ReasonCode;
        return exception is StudioAccountException accountException &&
               !string.IsNullOrWhiteSpace(accountException.ErrorCode)
            ? accountException.ErrorCode.Trim()
            : fallback;
    }
}

internal static class StudioCloudTraceIdentifier
{
    internal const string OperationIdHeader = "X-ErkS-Operation-Id";

    public static string Resolve(
        HttpResponseMessage response,
        StudioCloudApiError? error)
    {
        if (!string.IsNullOrWhiteSpace(error?.TraceId))
            return error.TraceId.Trim();

        return response.Headers.TryGetValues(OperationIdHeader, out IEnumerable<string>? values)
            ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? ""
            : "";
    }

    public static string Resolve(
        IReadOnlyDictionary<string, IEnumerable<string>>? headers,
        string? responseTraceId)
    {
        if (!string.IsNullOrWhiteSpace(responseTraceId))
            return responseTraceId.Trim();
        if (headers is null)
            return "";

        KeyValuePair<string, IEnumerable<string>> header = headers.FirstOrDefault(
            candidate => candidate.Key.Equals(OperationIdHeader, StringComparison.OrdinalIgnoreCase));
        return header.Value?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }
}

internal static class StudioCloudErrorDetails
{
    private static readonly string[] SafeFieldOrder =
    [
        "componentCode",
        "expectedCode",
        "ownerEmail",
        "sourceKey",
        "sourceId",
        "revisionId",
        "currentSourceId",
        "currentRevisionId",
    ];

    public static Dictionary<string, string[]>? ForDiagnosticLog(
        IReadOnlyDictionary<string, string[]>? fieldErrors)
    {
        if (fieldErrors is null || fieldErrors.Count == 0)
            return null;

        var safe = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string field in SafeFieldOrder)
        {
            KeyValuePair<string, string[]> match = fieldErrors.FirstOrDefault(
                candidate => candidate.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match.Key) || match.Value is null)
                continue;
            string[] values = match.Value
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(StudioOperationDiagnosticLog.SanitizeMessage)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(8)
                .ToArray();
            if (values.Length > 0)
                safe[field] = values;
        }

        return safe.Count == 0 ? null : safe;
    }

    public static string SafeSummary(IReadOnlyDictionary<string, string[]>? fieldErrors)
    {
        Dictionary<string, string[]>? safe = ForDiagnosticLog(fieldErrors);
        return safe is null
            ? ""
            : string.Join(
                "; ",
                SafeFieldOrder
                    .Where(safe.ContainsKey)
                    .Select(field => $"{field}={string.Join(", ", safe[field])}"));
    }
}
