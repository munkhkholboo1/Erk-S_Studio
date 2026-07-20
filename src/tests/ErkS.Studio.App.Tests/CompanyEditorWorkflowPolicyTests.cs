using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Studio.App.Tests;

public sealed class CompanyEditorWorkflowPolicyTests
{
    [Fact]
    public void EmptyLegacyPendingDraft_IsRemovedFromCompanyList()
    {
        var entry = new CompanyCatalogEntry
        {
            Profile = new CompanyProfile
            {
                OrganizationId = "local-empty",
                DesignRepresentativeTitle = "Захирал",
            },
            SyncStatus = CompanySyncStatuses.PendingCreate,
        };

        Assert.True(CompanyEditorWorkflowPolicy.IsEmptyLegacyDraft(entry));
    }

    [Fact]
    public void PendingDraftWithUserData_IsPreserved()
    {
        var entry = new CompanyCatalogEntry
        {
            Profile = new CompanyProfile
            {
                OrganizationId = "local-company",
                Name = "Erk-S LLC",
            },
            SyncStatus = CompanySyncStatuses.PendingCreate,
        };

        Assert.False(CompanyEditorWorkflowPolicy.IsEmptyLegacyDraft(entry));
    }
}
