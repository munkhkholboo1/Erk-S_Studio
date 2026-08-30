using System.Net.Http.Headers;
using System.Text;

namespace ErkS.CloudEra.Client.Generated;

public partial class CloudEraGeneratedClient
{
    public string BaseUrl { get; init; } = "";

    public string AccessToken { get; init; } = "";

    /// <summary>
    /// The policy the server requires every relationship-changing call to name,
    /// and the header it names it in.
    /// </summary>
    /// <remarks>
    /// The server compares this exactly and refuses anything else, so the value
    /// is a wire constant rather than a setting. It was written out by hand in
    /// three places in Studio - here, in StudioRelationshipBoundary, and in the
    /// test that pins it - and the copy here is the dangerous one: it is the
    /// default, so if a caller ever stopped passing its own, this would quietly
    /// become the answer and no one would know which of the two was being sent.
    ///
    /// One definition now, referenced by the others. The test keeps its own
    /// literal on purpose: a pin that reads the value it is pinning would pin
    /// nothing.
    /// </remarks>
    public const string CurrentRelationshipBoundaryPolicyVersion =
        "ERKS-RELATIONSHIP-BOUNDARY-2026-07-17";

    public const string RelationshipBoundaryHeaderName = "X-ErkS-Relationship-Boundary";

    public string RelationshipBoundaryPolicyVersion { get; init; } =
        CurrentRelationshipBoundaryPolicyVersion;

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
                RelationshipBoundaryHeaderName,
                RelationshipBoundaryPolicyVersion);
        }
    }
}
