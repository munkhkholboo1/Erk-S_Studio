using System.Net.Http.Headers;
using System.Text;

namespace ErkS.CloudEra.Client.Generated;

public partial class CloudEraGeneratedClient
{
    public string BaseUrl { get; init; } = "";

    public string AccessToken { get; init; } = "";

    public string RelationshipBoundaryPolicyVersion { get; init; } =
        "ERKS-RELATIONSHIP-BOUNDARY-2026-07-17";

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder)
    {
        string normalizedBaseUrl = BaseUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(normalizedBaseUrl))
            urlBuilder.Insert(0, normalizedBaseUrl + "/");

        if (!string.IsNullOrWhiteSpace(AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        if (!string.IsNullOrWhiteSpace(RelationshipBoundaryPolicyVersion))
        {
            request.Headers.TryAddWithoutValidation(
                "X-ErkS-Relationship-Boundary",
                RelationshipBoundaryPolicyVersion);
        }
    }
}
