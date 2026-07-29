using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioOrganizationMutationCheckpointTests
{
    [Fact]
    public void CreatedOrganization_BecomesPendingUpdateWithCanonicalIdentity()
    {
        var entry = new CompanyCatalogEntry
        {
            Profile = new CompanyProfile
            {
                OrganizationId = "local-draft",
            },
            SyncStatus = CompanySyncStatuses.PendingCreate,
        };
        var pending = new CompanyProfile
        {
            OrganizationId = "local-draft",
            Name = "Example LLC",
        };

        StudioOrganizationMutationCheckpoint.Apply(
            entry,
            pending,
            new StudioCloudOrganization
            {
                OrganizationId = "org-1",
                ConcurrencyToken = "token-after-create",
                CanManage = true,
                CurrentUserRole = "Organization Owner",
            });

        Assert.Equal("org-1", entry.Profile.OrganizationId);
        Assert.Equal("token-after-create", entry.ConcurrencyToken);
        Assert.Equal(CompanySyncStatuses.PendingUpdate, entry.SyncStatus);
    }

    [Fact]
    public void LaterMutation_ReplacesCheckpointToken()
    {
        var entry = new CompanyCatalogEntry
        {
            Profile = new CompanyProfile { OrganizationId = "org-1" },
            ConcurrencyToken = "token-before",
        };

        StudioOrganizationMutationCheckpoint.Apply(
            entry,
            entry.Profile,
            new StudioCloudOrganization
            {
                OrganizationId = "org-1",
                ConcurrencyToken = "token-after-logo",
            });

        Assert.Equal("token-after-logo", entry.ConcurrencyToken);
    }
}
