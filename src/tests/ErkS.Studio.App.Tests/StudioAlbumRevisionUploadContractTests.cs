using System.Net.Http;
using System.Text.Json;
using ErkS.Studio;
using Xunit;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumRevisionUploadContractTests
{
    [Fact]
    public async Task AddAlbumRevisionUploadFields_IncludesAtomicManifestInheritanceOptions()
    {
        using MultipartFormDataContent content = new();

        StudioAccountService.AddAlbumRevisionUploadFields(
            content,
            pageCount: 4,
            pageSizeSummary: "A3",
            projectConcurrencyToken: " project-token-1 ",
            expectedBaseRevisionId: " revision0 ",
            inheritComponentManifest: true,
            componentManifest: null);

        Dictionary<string, string> fields = await ReadFieldsAsync(content);

        Assert.Equal("4", fields["pageCount"]);
        Assert.Equal("A3", fields["pageSizeSummary"]);
        Assert.Equal("project-token-1", fields["projectConcurrencyToken"]);
        Assert.Equal("revision0", fields["expectedBaseRevisionId"]);
        Assert.Equal("true", fields["inheritComponentManifest"]);
        Assert.DoesNotContain("componentManifest", fields);
    }

    [Fact]
    public async Task AddAlbumRevisionUploadFields_IncludesSuppliedComponentManifest()
    {
        using MultipartFormDataContent content = new();

        StudioAccountService.AddAlbumRevisionUploadFields(
            content,
            pageCount: 4,
            pageSizeSummary: "A3",
            projectConcurrencyToken: "project-token-1",
            expectedBaseRevisionId: "revision0",
            inheritComponentManifest: false,
            componentManifest:
            [
                new StudioCloudAlbumSection
                {
                    Code = "generated:cover",
                    Label = "Cover",
                    Order = 0,
                    PageNumbers = [1],
                    Status = "Available",
                },
            ]);

        Dictionary<string, string> fields = await ReadFieldsAsync(content);

        List<StudioCloudAlbumSection>? manifest =
            JsonSerializer.Deserialize<List<StudioCloudAlbumSection>>(
                fields["componentManifest"],
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        StudioCloudAlbumSection component = Assert.Single(manifest!);
        Assert.Equal("generated:cover", component.Code);
        Assert.Equal([1], component.PageNumbers);
    }

    [Fact]
    public async Task AddAlbumRevisionUploadFields_OmitsUnusedInheritanceOptions()
    {
        using MultipartFormDataContent content = new();

        StudioAccountService.AddAlbumRevisionUploadFields(
            content,
            pageCount: 4,
            pageSizeSummary: "A3",
            projectConcurrencyToken: "project-token-1",
            expectedBaseRevisionId: null,
            inheritComponentManifest: false,
            componentManifest: null);

        Dictionary<string, string> fields = await ReadFieldsAsync(content);

        Assert.DoesNotContain("expectedBaseRevisionId", fields);
        Assert.DoesNotContain("inheritComponentManifest", fields);
        Assert.DoesNotContain("componentManifest", fields);
    }

    [Fact]
    public void ComponentManifestUpdateCarriesExactProjectAndRevisionBase()
    {
        StudioCloudAlbumComponentManifestUpdateRequest request =
            StudioAccountService.CreateAlbumComponentManifestUpdateRequest(
                " project-token ",
                " revision-42 ",
                [
                    new StudioCloudAlbumSection
                    {
                        Code = "generated:cover",
                        PageNumbers = [1],
                    },
                ]);

        Assert.Equal("project-token", request.ProjectConcurrencyToken);
        Assert.Equal("revision-42", request.ExpectedBaseRevisionId);
        Assert.Single(request.Components);
    }

    [Theory]
    [InlineData("", "revision-42")]
    [InlineData("project-token", "")]
    public void ComponentManifestUpdateRejectsMissingConcurrencyBase(
        string projectToken,
        string revisionId)
    {
        Assert.Throws<StudioAccountException>(() =>
            StudioAccountService.CreateAlbumComponentManifestUpdateRequest(
                projectToken,
                revisionId,
                []));
    }

    private static async Task<Dictionary<string, string>> ReadFieldsAsync(
        MultipartFormDataContent content)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (HttpContent part in content)
        {
            string name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
            fields.Add(name, await part.ReadAsStringAsync());
        }

        return fields;
    }
}
