using ErkS.Updates;

namespace ErkS.Studio;

/// <summary>
/// «Холболт шалгах» (sign-in dialog) and the bridge's «net-diag» - one function (Erk-S Sync / Master,
/// 2026-10-28): on a machine that cannot reach the server it says the clock, the proxy, DNS and the
/// certificate the TLS handshake sees, so the member (or an agent) sees the cause there and then.
/// </summary>
internal static class StudioNetDiag
{
    public static async Task<string> RunAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        string url = string.IsNullOrWhiteSpace(serverUrl) ? "https://erk-s.mn/" : serverUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? server))
            return $"Серверийн хаяг буруу: {url}";
        string report = await NetworkFailure.DiagnoseAsync(server, cancellationToken).ConfigureAwait(true);
        return StudioNetworkFailureLog.Last is { } last
            ? report + Environment.NewLine + $"Сүүлийн алдаа: [{last.Code}/{last.Detail}] {last.Message}"
            : report;
    }
}
