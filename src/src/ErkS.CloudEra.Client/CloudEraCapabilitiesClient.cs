using ErkS.CloudEra.Client.Generated;

namespace ErkS.CloudEra.Client;

public sealed class CloudEraCapabilitiesClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly CloudEraGeneratedClient generatedClient;

    public CloudEraCapabilitiesClient(Uri serverBaseUri, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        if (!serverBaseUri.IsAbsoluteUri)
            throw new ArgumentException("Cloud ERA server URL must be absolute.", nameof(serverBaseUri));

        Uri normalizedBaseUri = new(serverBaseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        httpClient.BaseAddress = normalizedBaseUri;
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        generatedClient = new CloudEraGeneratedClient(httpClient);
    }

    public async Task<CloudEraCapabilitiesSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        CloudEraApiCapabilitiesResponse response;
        try
        {
            response = await generatedClient
                .GetCloudEraCapabilitiesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ApiException error)
        {
            throw new CloudEraContractMismatchException(
                $"Cloud ERA capabilities request failed with HTTP {error.StatusCode}.",
                error.StatusCode);
        }

        CloudEraCapabilitiesSnapshot snapshot = new(
            response.ApiVersion ?? "",
            response.ApiBasePath ?? "",
            response.OpenApiDocumentPath ?? "",
            new Dictionary<string, bool>(
                response.Features ?? new Dictionary<string, bool>(),
                StringComparer.Ordinal));
        CloudEraCapabilityPolicy.EnsureCompatible(snapshot);
        return snapshot;
    }

    public void Dispose() => httpClient.Dispose();
}
