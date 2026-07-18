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

        CompanyProfile snapshot = profile.Clone();
        snapshot.OrganizationId = profile.OrganizationId;
        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
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
            left.RegisteredCity.Equals(right.RegisteredCity, StringComparison.Ordinal) &&
            left.Address.Equals(right.Address, StringComparison.Ordinal) &&
            left.PhoneNumbers.SequenceEqual(right.PhoneNumbers, StringComparer.Ordinal) &&
            left.Email.Equals(right.Email, StringComparison.Ordinal) &&
            left.WebSite.Equals(right.WebSite, StringComparison.Ordinal) &&
            left.LicenseScope.Equals(right.LicenseScope, StringComparison.Ordinal) &&
            left.LicenseNumber.Equals(right.LicenseNumber, StringComparison.Ordinal) &&
            left.DirectorTitle.Equals(right.DirectorTitle, StringComparison.Ordinal) &&
            left.DirectorName.Equals(right.DirectorName, StringComparison.Ordinal) &&
            left.LogoPath.Equals(right.LogoPath, StringComparison.OrdinalIgnoreCase) &&
            left.LogoScale.Equals(right.LogoScale) &&
            left.LogoOffsetX.Equals(right.LogoOffsetX) &&
            left.LogoOffsetY.Equals(right.LogoOffsetY) &&
            Nullable.Equals(left.UpdatedAtUtc, right.UpdatedAtUtc);
    }

    private static string FirstValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
