using ErkS.CloudEra.Client;
using ErkS.CloudEra.Client.Generated;
using System.Net;
using System.Text;

namespace ErkS.Platform.Core.Tests;

public sealed class CloudEraCapabilityPolicyTests
{
    [Fact]
    public void SameMajorVersion_AllowsAdvertisedFeature()
    {
        CloudEraCapabilitiesSnapshot capabilities = Snapshot("1.7", ("album-revisions", true));

        Assert.True(CloudEraCapabilityPolicy.Supports(capabilities, "album-revisions"));
    }

    [Fact]
    public void ChunkedAlbumUpload_IsAnOptionalNegotiatedCapability()
    {
        CloudEraCapabilitiesSnapshot capabilities = Snapshot(
            "1.0",
            (CloudEraFeatures.ChunkedAlbumUploadsV1, true));

        Assert.True(CloudEraCapabilityPolicy.Supports(
            capabilities,
            CloudEraFeatures.ChunkedAlbumUploadsV1));
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("invalid")]
    [InlineData("")]
    public void IncompatibleVersion_ProducesControlledContractError(string version)
    {
        CloudEraContractMismatchException error = Assert.Throws<CloudEraContractMismatchException>(
            () => CloudEraCapabilityPolicy.EnsureCompatible(Snapshot(version)));

        Assert.Contains("not compatible", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingRequiredFeature_ProducesControlledContractError()
    {
        CloudEraCapabilitiesSnapshot capabilities = Snapshot("1.0", ("album-revisions", false));

        CloudEraContractMismatchException error = Assert.Throws<CloudEraContractMismatchException>(
            () => CloudEraCapabilityPolicy.Require(capabilities, "album-revisions"));

        Assert.Contains("album-revisions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedClient_DeserializesCapabilitiesAndIgnoresFutureOptionalFields()
    {
        const string json = """
            {
              "apiVersion": "1.4",
              "apiBasePath": "/api/cloud-era/v1",
              "openApiDocumentPath": "/api/cloud-era/openapi/v1.json",
              "serverTimeUtc": "2026-07-18T00:00:00Z",
              "features": { "source-packages-v4": true },
              "futureOptionalField": "safe-to-ignore"
            }
            """;
        using CloudEraCapabilitiesClient client = new(
            new Uri("https://erk-s.test/"),
            new JsonHandler(json));

        CloudEraCapabilitiesSnapshot capabilities = await client.GetAsync(CancellationToken.None);

        Assert.Equal("1.4", capabilities.ApiVersion);
        Assert.True(capabilities.Features["source-packages-v4"]);
    }

    [Fact]
    public async Task GeneratedClient_PreservesMissingCapabilitiesStatusCode()
    {
        using CloudEraCapabilitiesClient client = new(
            new Uri("http://127.0.0.1:5055/"),
            new StatusHandler(HttpStatusCode.NotFound));

        CloudEraContractMismatchException error = await Assert.ThrowsAsync<CloudEraContractMismatchException>(
            () => client.GetAsync(CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task GeneratedClient_DeserializesTypedProjectListContract()
    {
        const string json = """
            {
              "apiVersion": "v1",
              "serverTimeUtc": "2026-07-18T00:00:00Z",
              "projects": [
                {
                  "projectId": "project-1",
                  "projectCode": "ERKS-001",
                  "name": "Typed project",
                  "concurrencyToken": "etag-1"
                }
              ]
            }
            """;
        using HttpClient http = new(new ProjectListJsonHandler(json))
        {
            BaseAddress = new Uri("https://erk-s.test/"),
        };
        CloudEraGeneratedClient client = new(http);

        CloudEraProjectListResponse response = await client.ListCloudEraProjectsAsync(CancellationToken.None);

        CloudEraProjectSummaryDto project = Assert.Single(response.Projects);
        Assert.Equal("project-1", project.ProjectId);
        Assert.Equal("etag-1", project.ConcurrencyToken);
    }

    private static CloudEraCapabilitiesSnapshot Snapshot(
        string version,
        params (string Feature, bool Enabled)[] features) => new(
            version,
            "/api/cloud-era/v1",
            "/api/cloud-era/openapi/v1.json",
            features.ToDictionary(item => item.Feature, item => item.Enabled, StringComparer.Ordinal));

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("https://erk-s.test/api/cloud-era/v1/capabilities", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ProjectListJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("https://erk-s.test/api/cloud-era/v1/projects", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
