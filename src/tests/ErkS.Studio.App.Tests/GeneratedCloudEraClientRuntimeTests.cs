using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ErkS.CloudEra.Client.Generated;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class GeneratedCloudEraClientRuntimeTests
{
    [Fact]
    public async Task GeneratedClient_AppliesServerAuthenticationAndRelationshipBoundary()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedClient(httpClient)
        {
            BaseUrl = "https://erk-s.mn/",
            AccessToken = "access-token",
        };

        CloudEraProjectListResponse response = await client.ListCloudEraProjectsAsync(CancellationToken.None);

        Assert.Empty(response.Projects);
        Assert.Equal("https://erk-s.mn/api/cloud-era/v1/projects", handler.RequestUri?.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), handler.Authorization);
        Assert.Equal(
            "ERKS-RELATIONSHIP-BOUNDARY-2026-07-17",
            handler.RelationshipBoundaryAcknowledgement);
    }

    [Fact]
    public async Task ContractWrapper_MapsGeneratedProjectResponseToStudioModel()
    {
        const string json = """
            {
              "apiVersion": "1.0",
              "serverTimeUtc": "2026-07-18T00:00:00Z",
              "projects": [
                {
                  "projectId": "project-1",
                  "projectCode": "STUDIO-001",
                  "name": "Vector project",
                  "status": "Active",
                  "currentStage": "ConceptDesign",
                  "templateId": "MN-BLD-ARCH-CONCEPT",
                  "templateVersion": "1",
                  "clientName": "Client",
                  "planningAuthorityName": "Authority",
                  "designOrganizationName": "Erk-S LLC",
                  "updatedAtUtc": "2026-07-18T00:00:00Z",
                  "currentUserRoles": ["ProjectAdmin"],
                  "currentUserScopes": ["project:write"],
                  "currentUserIsCreator": true,
                  "concurrencyToken": "etag-1"
                }
              ]
            }
            """;
        var handler = new RecordingHandler(json);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedContractClient(httpClient);

        StudioCloudProjectListResponse response = await client.ListProjectsAsync(
            new CloudEraClientContext("https://erk-s.mn", "access-token"),
            CancellationToken.None);

        StudioCloudProjectSummary project = Assert.Single(response.Projects);
        Assert.Equal("project-1", project.ProjectId);
        Assert.Equal("STUDIO-001", project.ProjectCode);
        Assert.Equal("etag-1", project.ConcurrencyToken);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string responseJson;

        public RecordingHandler(string? responseJson = null)
        {
            this.responseJson = responseJson ?? """
                {
                  "apiVersion": "1.0",
                  "serverTimeUtc": "2026-07-18T00:00:00Z",
                  "projects": []
                }
                """;
        }

        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? RelationshipBoundaryAcknowledgement { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            RelationshipBoundaryAcknowledgement = request.Headers.TryGetValues(
                "X-ErkS-Relationship-Boundary",
                out IEnumerable<string>? values)
                ? values.Single()
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }
}
