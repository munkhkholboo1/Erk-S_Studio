using System.Net;
using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCloudApiErrorTests
{
    [Fact]
    public void OrganizationRecoveryToken_ReadsFromTheWireError()
    {
        const string body = """
            {
              "code": "organization_concurrency_conflict",
              "message": "Байгууллага өөрчлөгдсөн.",
              "traceId": "trace-1",
              "currentOrganizationConcurrencyToken": "\"token-42\""
            }
            """;

        StudioCloudApiError? error = JsonSerializer.Deserialize<StudioCloudApiError>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(error);
        Assert.Equal("\"token-42\"", error!.CurrentOrganizationConcurrencyToken);
    }

    [Fact]
    public void Exception_CarriesTheOrganizationRecoveryToken()
    {
        var exception = new StudioAccountException(
            "conflict",
            HttpStatusCode.PreconditionFailed,
            "organization_concurrency_conflict",
            currentOrganizationConcurrencyToken: " \"token-42\" ");

        Assert.Equal("\"token-42\"", exception.CurrentOrganizationConcurrencyToken);
    }
}
