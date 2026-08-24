namespace ErkS.Platform.Core;

/// <summary>Maintains the stage company assignment independently from its mutable company profile snapshot.</summary>
public static class ProjectCompanyAssignmentService
{
    public static bool HasAssignedOrganization(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return !string.IsNullOrWhiteSpace(project.Foundation.DesignCompany.OrganizationId);
    }

    public static bool MatchesAssignedOrganization(ProjectWorkspace project, CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        bool hasAssignmentId = !string.IsNullOrWhiteSpace(assignment.OrganizationId);
        if (hasAssignmentId)
        {
            return assignment.OrganizationId.Equals(
                profile.OrganizationId,
                StringComparison.OrdinalIgnoreCase);
        }

        return (!string.IsNullOrWhiteSpace(assignment.OrganizationName) &&
                (assignment.OrganizationName.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) ||
                 assignment.OrganizationName.Equals(profile.DisplayName, StringComparison.OrdinalIgnoreCase))) ||
            (project.Creation.InitiatorType.Equals(ProjectInitiatorTypes.DesignOrganization, StringComparison.OrdinalIgnoreCase) &&
             (project.Creation.InitiatorOrganizationId.Equals(profile.OrganizationId, StringComparison.OrdinalIgnoreCase) ||
              project.Creation.InitiatorOrganizationName.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) ||
              project.Creation.InitiatorOrganizationName.Equals(profile.DisplayName, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool RefreshAssignedSnapshot(ProjectWorkspace project, CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        if (!MatchesAssignedOrganization(project, profile))
            return false;

        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        CompanyProfile snapshot = profile.Clone();
        snapshot.OrganizationId = profile.OrganizationId;

        // The company library is not an authoritative list of the
        // organisation's scans, so its emptiness is not a deletion.
        //
        // A certificate can reach a project two ways: somebody on this device
        // put it in the company library, or the server sent it down for this
        // project. The library never learns about the second - the
        // organisation list carries no documents - so replacing the project's
        // snapshot with a library profile wholesale drops every scan that came
        // from the cloud, and the album quietly loses pages that worked a
        // moment earlier.
        snapshot.RegistrationCertificateDocuments = MergeDocuments(
            snapshot.RegistrationCertificateDocuments,
            assignment.OrganizationSnapshot.RegistrationCertificateDocuments);
        snapshot.DesignLicenseDocuments = MergeDocuments(
            snapshot.DesignLicenseDocuments,
            assignment.OrganizationSnapshot.DesignLicenseDocuments);

        bool changed = !ProfilesEqual(assignment.OrganizationSnapshot, snapshot) ||
            !assignment.OrganizationId.Equals(profile.OrganizationId, StringComparison.OrdinalIgnoreCase) ||
            !assignment.OrganizationName.Equals(profile.Name, StringComparison.Ordinal);
        if (!changed)
            return false;

        assignment.OrganizationSnapshot = snapshot;
        assignment.OrganizationId = profile.OrganizationId;
        assignment.OrganizationName = profile.Name;
        if (project.Creation.InitiatorType.Equals(ProjectInitiatorTypes.DesignOrganization, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(project.Creation.InitiatorOrganizationId) ||
             project.Creation.InitiatorOrganizationId.Equals(profile.OrganizationId, StringComparison.OrdinalIgnoreCase)))
        {
            project.Creation.InitiatorOrganizationId = profile.OrganizationId;
            project.Creation.InitiatorOrganizationName = profile.Name;
        }
        return true;
    }

    public static void AssignToProject(ProjectWorkspace project, CompanyProfile profile, DateTimeOffset assignedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.OrganizationType.Equals("DesignCompany", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected organization is not a design company.");
        if (string.IsNullOrWhiteSpace(profile.OrganizationId))
            throw new InvalidOperationException("The selected company has no organization identity.");

        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        assignment.History ??= [];
        bool sameOrganization = assignment.OrganizationId.Equals(profile.OrganizationId, StringComparison.OrdinalIgnoreCase);
        if (!sameOrganization &&
            (!string.IsNullOrWhiteSpace(assignment.OrganizationId) || !string.IsNullOrWhiteSpace(assignment.OrganizationName)))
        {
            assignment.History.Add(new ProjectCompanyAssignmentHistoryEntry
            {
                OrganizationId = assignment.OrganizationId,
                OrganizationName = assignment.OrganizationName,
                AssignmentSource = assignment.AssignmentSource,
                AssignedAtUtc = assignment.AssignedAtUtc,
                ReplacedAtUtc = assignedAtUtc,
                OrganizationSnapshot = assignment.OrganizationSnapshot.Clone(),
            });
        }

        bool cloudLinked = project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);
        assignment.OrganizationId = profile.OrganizationId;
        assignment.OrganizationName = profile.Name;
        assignment.OrganizationSnapshot = profile.Clone();
        assignment.AssignmentSource = cloudLinked ? "StudioCloudPending" : "StudioSelected";
        assignment.AssignedAtUtc = assignedAtUtc;
        if (cloudLinked)
        {
            project.Cloud.SyncStatus = ProjectSyncStatuses.Pending;
            project.Cloud.LastSyncError = "";
            project.Cloud.LastSyncNote = "Design organization reassignment is waiting for Cloud ERA sync.";
        }
    }

    public static void ConfirmCloudAssignment(ProjectWorkspace project, CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        if (!MatchesAssignedOrganization(project, profile))
            throw new InvalidOperationException("Cloud ERA confirmed a different design organization.");

        RefreshAssignedSnapshot(project, profile);
        project.Foundation.DesignCompany.AssignmentSource = "StudioCloudSelected";
        project.Cloud.SyncStatus = ProjectSyncStatuses.Linked;
        project.Cloud.LastSyncError = "";
        project.Cloud.LastSyncNote = "Design organization reassignment was confirmed by Cloud ERA.";
    }

    /// <summary>
    /// Applies the canonical Cloud ERA assignment without treating an omitted
    /// assignment as a command to clear a previously confirmed local mirror.
    /// </summary>
    public static bool MergeCloudAssignment(
        ProjectWorkspace project,
        string? organizationId,
        string? organizationName,
        CompanyProfile? renderProfile)
    {
        ArgumentNullException.ThrowIfNull(project);
        string cloudOrganizationId = organizationId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(cloudOrganizationId))
            return false;

        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        assignment.History ??= [];
        bool sameOrganization = assignment.OrganizationId.Equals(
            cloudOrganizationId,
            StringComparison.OrdinalIgnoreCase);
        CompanyProfile previousSnapshot = assignment.OrganizationSnapshot ?? new CompanyProfile();
        string cloudOrganizationName = FirstValue(
            renderProfile?.Name,
            renderProfile?.DisplayName,
            organizationName,
            sameOrganization ? assignment.OrganizationName : "");

        CompanyProfile nextSnapshot;
        if (renderProfile is not null)
        {
            nextSnapshot = renderProfile.Clone();
            nextSnapshot.OrganizationId = cloudOrganizationId;
            if (string.IsNullOrWhiteSpace(nextSnapshot.Name))
                nextSnapshot.Name = cloudOrganizationName;
            if (string.IsNullOrWhiteSpace(nextSnapshot.DisplayName))
                nextSnapshot.DisplayName = cloudOrganizationName;
            if (sameOrganization && string.IsNullOrWhiteSpace(nextSnapshot.LogoPath))
                nextSnapshot.LogoPath = previousSnapshot.LogoPath;
            if (sameOrganization && nextSnapshot.RegistrationCertificateDocuments.Count == 0)
            {
                nextSnapshot.RegistrationCertificateDocuments = previousSnapshot.RegistrationCertificateDocuments
                    .Select(document => document.Clone())
                    .ToList();
            }
            if (sameOrganization && nextSnapshot.DesignLicenseDocuments.Count == 0)
            {
                nextSnapshot.DesignLicenseDocuments = previousSnapshot.DesignLicenseDocuments
                    .Select(document => document.Clone())
                    .ToList();
            }
        }
        else if (sameOrganization)
        {
            nextSnapshot = previousSnapshot.Clone();
            nextSnapshot.OrganizationId = cloudOrganizationId;
            if (string.IsNullOrWhiteSpace(nextSnapshot.Name))
                nextSnapshot.Name = cloudOrganizationName;
            if (string.IsNullOrWhiteSpace(nextSnapshot.DisplayName))
                nextSnapshot.DisplayName = cloudOrganizationName;
        }
        else
        {
            nextSnapshot = new CompanyProfile
            {
                OrganizationId = cloudOrganizationId,
                Name = cloudOrganizationName,
                DisplayName = cloudOrganizationName,
            };
        }

        string nextSource = assignment.AssignmentSource;
        if (sameOrganization && nextSource.Equals("StudioCloudPending", StringComparison.OrdinalIgnoreCase))
            nextSource = "StudioCloudSelected";
        else if (!sameOrganization || string.IsNullOrWhiteSpace(nextSource))
            nextSource = "CloudERA";

        bool changed =
            !sameOrganization ||
            !assignment.OrganizationName.Equals(cloudOrganizationName, StringComparison.Ordinal) ||
            !assignment.AssignmentSource.Equals(nextSource, StringComparison.Ordinal) ||
            !ProfilesEqual(previousSnapshot, nextSnapshot);
        if (!changed)
            return false;

        if (!sameOrganization &&
            (!string.IsNullOrWhiteSpace(assignment.OrganizationId) ||
             !string.IsNullOrWhiteSpace(assignment.OrganizationName)))
        {
            assignment.History.Add(new ProjectCompanyAssignmentHistoryEntry
            {
                OrganizationId = assignment.OrganizationId,
                OrganizationName = assignment.OrganizationName,
                AssignmentSource = assignment.AssignmentSource,
                AssignedAtUtc = assignment.AssignedAtUtc,
                ReplacedAtUtc = DateTimeOffset.UtcNow,
                OrganizationSnapshot = previousSnapshot.Clone(),
            });
        }

        assignment.OrganizationId = cloudOrganizationId;
        assignment.OrganizationName = cloudOrganizationName;
        assignment.OrganizationSnapshot = nextSnapshot;
        assignment.AssignmentSource = nextSource;
        assignment.AssignedAtUtc ??= DateTimeOffset.UtcNow;
        return true;
    }

    private static bool ProfilesEqual(CompanyProfile left, CompanyProfile right)
    {
        left.Normalize();
        right.Normalize();
        return left.OrganizationId.Equals(right.OrganizationId, StringComparison.OrdinalIgnoreCase) &&
            left.Name.Equals(right.Name, StringComparison.Ordinal) &&
            left.DisplayName.Equals(right.DisplayName, StringComparison.Ordinal) &&
            left.ShortName.Equals(right.ShortName, StringComparison.Ordinal) &&
            left.RegistrationNumber.Equals(right.RegistrationNumber, StringComparison.Ordinal) &&
            left.LegalEntityType.Equals(right.LegalEntityType, StringComparison.Ordinal) &&
            left.LegalForm.Equals(right.LegalForm, StringComparison.Ordinal) &&
            left.ActivityDirections.SequenceEqual(right.ActivityDirections, StringComparer.Ordinal) &&
            Nullable.Equals(left.RegisteredAtUtc, right.RegisteredAtUtc) &&
            left.OfficialRepresentativeName.Equals(right.OfficialRepresentativeName, StringComparison.Ordinal) &&
            left.RegistrySource.Equals(right.RegistrySource, StringComparison.Ordinal) &&
            left.RegistrySourceUrl.Equals(right.RegistrySourceUrl, StringComparison.Ordinal) &&
            Nullable.Equals(left.RegistryCheckedAtUtc, right.RegistryCheckedAtUtc) &&
            left.RegisteredCity.Equals(right.RegisteredCity, StringComparison.Ordinal) &&
            left.Address.Equals(right.Address, StringComparison.Ordinal) &&
            left.PhoneNumbers.SequenceEqual(right.PhoneNumbers, StringComparer.Ordinal) &&
            left.Email.Equals(right.Email, StringComparison.Ordinal) &&
            left.WebSite.Equals(right.WebSite, StringComparison.Ordinal) &&
            left.LicenseScope.Equals(right.LicenseScope, StringComparison.Ordinal) &&
            left.LicenseNumber.Equals(right.LicenseNumber, StringComparison.Ordinal) &&
            left.DirectorTitle.Equals(right.DirectorTitle, StringComparison.Ordinal) &&
            left.DirectorName.Equals(right.DirectorName, StringComparison.Ordinal) &&
            left.DesignRepresentativeTitle.Equals(right.DesignRepresentativeTitle, StringComparison.Ordinal) &&
            left.DesignRepresentativeName.Equals(right.DesignRepresentativeName, StringComparison.Ordinal) &&
            left.LogoPath.Equals(right.LogoPath, StringComparison.OrdinalIgnoreCase) &&
            left.LogoScale.Equals(right.LogoScale) &&
            left.LogoOffsetX.Equals(right.LogoOffsetX) &&
            left.LogoOffsetY.Equals(right.LogoOffsetY) &&
            DocumentListsEqual(left.RegistrationCertificateDocuments, right.RegistrationCertificateDocuments) &&
            DocumentListsEqual(left.DesignLicenseDocuments, right.DesignLicenseDocuments) &&
            Nullable.Equals(left.UpdatedAtUtc, right.UpdatedAtUtc);
    }

    /// <summary>
    /// The library's scans, plus anything the project already had that the
    /// library does not know about.
    /// </summary>
    /// <remarks>
    /// Identity is the sha256, falling back to the server's document id when a
    /// scan has no hash yet. Category is ignored on purpose, matching how
    /// uploads are deduplicated: the same scan filed under a different heading
    /// is still the same scan, and drawing it twice is what the reader
    /// notices.
    /// </remarks>
    private static List<ProjectFileReference> MergeDocuments(
        IEnumerable<ProjectFileReference>? fromLibrary,
        IEnumerable<ProjectFileReference>? alreadyOnTheProject)
    {
        List<ProjectFileReference> merged = [.. (fromLibrary ?? [])];
        HashSet<string> seen = [.. merged
            .Select(Identity)
            .Where(identity => identity.Length > 0)];

        foreach (ProjectFileReference document in alreadyOnTheProject ?? [])
        {
            if (document is null)
                continue;

            string identity = Identity(document);
            // A document with neither a hash nor a server id cannot be told
            // apart from anything else, so keeping it risks a duplicate page.
            // Dropping it risks losing a scan. The album can survive a
            // duplicate; it cannot recover a scan nobody kept.
            if (identity.Length > 0 && !seen.Add(identity))
                continue;

            merged.Add(document.Clone());
        }

        return merged;

        static string Identity(ProjectFileReference document)
        {
            if (!string.IsNullOrWhiteSpace(document.Sha256))
                return "sha:" + document.Sha256.Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(document.ServerDocumentId)
                ? ""
                : "id:" + document.ServerDocumentId.Trim().ToLowerInvariant();
        }
    }

    private static bool DocumentListsEqual(
        IEnumerable<ProjectFileReference> left,
        IEnumerable<ProjectFileReference> right) => left
        .Select(DocumentIdentity)
        .Order(StringComparer.Ordinal)
        .SequenceEqual(
            right.Select(DocumentIdentity)
                .Order(StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static string DocumentIdentity(ProjectFileReference document) =>
        $"{document.Category}|{document.Sha256}|{document.PageCount}|{document.RelativePath}|" +
        $"{document.LinkedSourcePath}|{document.IsAvailable}|{document.Version}";

    private static string FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
