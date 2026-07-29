using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class CompanyCatalogConcurrencyTokenTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-company-concurrency-tests",
        Guid.NewGuid().ToString("N"));

    public CompanyCatalogConcurrencyTokenTests() =>
        Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void CanonicalTokenSurvivesOfflinePendingDraftRoundTrip()
    {
        string catalogPath = Path.Combine(workDirectory, "companies.json");
        var store = new CompanyLibraryStore(
            catalogPath,
            Path.Combine(workDirectory, "logos"));
        store.Save(
        [
            new CompanyCatalogEntry
            {
                Profile = new CompanyProfile
                {
                    OrganizationId = "org-1",
                    Name = "Local pending draft",
                },
                ConcurrencyToken = "server-token-at-edit",
                SyncStatus = CompanySyncStatuses.PendingUpdate,
            },
        ]);

        CompanyCatalogEntry loaded = Assert.Single(store.Load());

        Assert.Equal(
            "server-token-at-edit",
            loaded.ConcurrencyToken);
        Assert.Equal(
            CompanySyncStatuses.PendingUpdate,
            loaded.SyncStatus);
    }
}
