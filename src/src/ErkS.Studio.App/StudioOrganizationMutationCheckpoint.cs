using ErkS.Platform.Core;
using System.IO;

namespace ErkS.Studio;

/// <summary>
/// Persists the server identity/token immediately after each successful
/// organization mutation so a later logo/network failure cannot retry a
/// completed create or reuse a stale concurrency token.
/// </summary>
internal static class StudioOrganizationMutationCheckpoint
{
    public static void Apply(
        CompanyCatalogEntry entry,
        CompanyProfile pendingProfile,
        StudioCloudOrganization canonical)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(pendingProfile);
        ArgumentNullException.ThrowIfNull(canonical);
        if (string.IsNullOrWhiteSpace(canonical.OrganizationId) ||
            string.IsNullOrWhiteSpace(canonical.ConcurrencyToken))
        {
            throw new InvalidDataException(
                "Canonical organization acknowledgement is missing its identity or concurrency token.");
        }

        pendingProfile.OrganizationId = canonical.OrganizationId.Trim();
        entry.Profile = pendingProfile;
        entry.ConcurrencyToken = canonical.ConcurrencyToken.Trim();
        entry.CanManage = canonical.CanManage;
        entry.CurrentUserRole = canonical.CurrentUserRole?.Trim() ?? "";
        entry.SyncStatus = CompanySyncStatuses.PendingUpdate;
    }
}
