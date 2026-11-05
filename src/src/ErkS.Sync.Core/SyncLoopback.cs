using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ErkS.Sync.Core;

/// <summary>
/// The owner's desktop runs SRV AND an Erk-S Sync: 19 GB of Altai went out through the Cloudflare tunnel at
/// 4 MB/s and came straight back into the same machine (Erk-S Sync, 2026-10-28). When this machine IS the
/// server, the pieces go over its loopback port instead - but only once that port is PROVED to be the very
/// server process (SRV 95f332a): one nonce through erk-s.mn (authenticated), the same nonce to /local-proof on
/// the loopback (no token), and the two HMACs must be equal. Anything else - another process on the port, a
/// refusal, a timeout, an address that is not loopback - and the normal road is used, silently.
/// </summary>
public static class SyncLoopback
{
    /// <summary>How long the loopback may take to answer the proof; a server on this machine answers at once.</summary>
    public static readonly TimeSpan ProofTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The proven loopback client, or null when this machine is not the server (or it could not be shown).</summary>
    public static async Task<SyncServerClient?> ProveAsync(SyncServerClient server, string deviceToken, Func<Uri, SyncServerClient> local, CancellationToken cancellationToken)
    {
        if (server.BaseAddress is { IsLoopback: true })
            return null; // already talking to it directly
        string nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        try
        {
            (string proof, string url) = await server.InstanceProofAsync(deviceToken, nonce, cancellationToken);
            // The token only ever goes to this machine's own address.
            if (proof.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out Uri? address) || !address.IsLoopback ||
                address.Scheme is not ("http" or "https"))
            {
                return null;
            }
            SyncServerClient client = local(address);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProofTimeout);
            string theirs = await client.LocalProofAsync(nonce, timeout.Token);
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proof), Encoding.ASCII.GetBytes(theirs)) ? client : null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is SyncServerException or HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A client of the loopback address: no proxy (a system proxy must not see the token), short connects.</summary>
    public static SyncServerClient Client(Uri address, string clientVersion) =>
        new(new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) }) { BaseAddress = address, Timeout = TimeSpan.FromMinutes(2) })
        {
            ClientVersion = clientVersion,
        };
}
