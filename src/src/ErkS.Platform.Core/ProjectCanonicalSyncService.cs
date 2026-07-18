namespace ErkS.Platform.Core;

public static class ProjectCanonicalSyncService
{
    public static bool Apply(ProjectWorkspace project, ProjectServerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(snapshot);

        string serverProjectId = Clean(snapshot.ProjectId);
        if (string.IsNullOrWhiteSpace(serverProjectId))
            throw new InvalidDataException("Canonical server project ID is empty.");

        string linkedProjectId = Clean(project.Cloud.ServerProjectId);
        if (project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(linkedProjectId) &&
            !linkedProjectId.Equals(serverProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Local mirror is linked to project '{linkedProjectId}', not '{serverProjectId}'.");
        }

        ProjectServerInformation information = snapshot.Information ?? new();
        ProjectServerSiteAndLand siteAndLand = snapshot.SiteAndLand ?? new();
        PendingProjectInformationUpdate? pending = project.Cloud.PendingProjectInformation;
        string projectCode = FirstValue(snapshot.ProjectCode, information.ProjectCode);
        string serverProjectName = FirstValue(snapshot.Name, information.Name);
        string projectName = pending is null ? serverProjectName : Clean(pending.Name);
        string serverSiteAddress = FirstValue(information.Location, siteAndLand.Addresses.FirstOrDefault());
        string siteAddress = pending is null ? serverSiteAddress : Clean(pending.Location);
        string landReference = string.Join(", ", CleanValues(siteAndLand.ParcelNumbers));
        string buildingPurpose = pending is null ? Clean(information.BuildingPurpose) : Clean(pending.BuildingPurpose);
        string clientName = pending is null ? Clean(snapshot.ClientName) : Clean(pending.ClientName);
        string planningAuthorityName = pending is null
            ? Clean(snapshot.PlanningAuthorityName)
            : Clean(pending.PlanningAuthorityName);
        string currentStage = Clean(snapshot.CurrentStage);

        ProjectInitiationBasis basis = project.Foundation.InitiationBasis;
        PlanningTaskInformation planningTask = project.Foundation.PlanningTask;
        bool foundationChanged =
            !string.Equals(project.Identity.Name, projectName, StringComparison.Ordinal) ||
            !string.Equals(project.Identity.Code, projectCode, StringComparison.Ordinal) ||
            !string.Equals(project.Identity.Description, buildingPurpose, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(currentStage) &&
                !string.Equals(project.Identity.StageName, currentStage, StringComparison.Ordinal)) ||
            !string.Equals(basis.ClientName, clientName, StringComparison.Ordinal) ||
            !string.Equals(basis.SiteAddress, siteAddress, StringComparison.Ordinal) ||
            !string.Equals(basis.LandReference, landReference, StringComparison.Ordinal) ||
            !string.Equals(basis.Summary, buildingPurpose, StringComparison.Ordinal) ||
            !string.Equals(planningTask.IssuingAuthorityName, planningAuthorityName, StringComparison.Ordinal);

        project.ProjectId = serverProjectId;
        project.Identity.Code = projectCode;
        project.Identity.Name = projectName;
        project.Identity.Description = buildingPurpose;
        if (!string.IsNullOrWhiteSpace(currentStage))
            project.Identity.StageName = currentStage;

        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = serverProjectId;
        project.Cloud.CloudProjectCode = projectCode;
        project.Cloud.ServerSnapshot = Clone(snapshot, serverProjectId, projectCode, serverProjectName);

        basis.ClientName = clientName;
        basis.SiteAddress = siteAddress;
        basis.LandReference = landReference;
        basis.ServerRecordId = serverProjectId;
        basis.Summary = buildingPurpose;
        planningTask.IssuingAuthorityName = planningAuthorityName;

        if (foundationChanged)
            project.Foundation.Version++;

        return foundationChanged;
    }

    private static ProjectServerSnapshot Clone(
        ProjectServerSnapshot snapshot,
        string projectId,
        string projectCode,
        string projectName)
    {
        ProjectServerInformation information = snapshot.Information ?? new();
        ProjectServerSiteAndLand siteAndLand = snapshot.SiteAndLand ?? new();
        return new ProjectServerSnapshot
        {
            ProjectId = projectId,
            ProjectCode = projectCode,
            Name = projectName,
            Status = Clean(snapshot.Status),
            CurrentStage = Clean(snapshot.CurrentStage),
            ClientName = Clean(snapshot.ClientName),
            PlanningAuthorityName = Clean(snapshot.PlanningAuthorityName),
            DesignOrganizationName = Clean(snapshot.DesignOrganizationName),
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            ConcurrencyToken = Clean(snapshot.ConcurrencyToken),
            Information = new ProjectServerInformation
            {
                ProjectId = FirstValue(information.ProjectId, projectId),
                ProjectCode = FirstValue(information.ProjectCode, projectCode),
                Name = FirstValue(information.Name, projectName),
                Location = Clean(information.Location),
                BuildingPurpose = Clean(information.BuildingPurpose),
                Capacity = information.Capacity,
                CapacityUnit = Clean(information.CapacityUnit),
                FootprintSquareMeters = information.FootprintSquareMeters,
                GrossFloorAreaSquareMeters = information.GrossFloorAreaSquareMeters,
                HeightMeters = information.HeightMeters,
                FloorsAboveGround = information.FloorsAboveGround,
                FloorsBelowGround = information.FloorsBelowGround,
            },
            SiteAndLand = new ProjectServerSiteAndLand
            {
                ParcelNumbers = CleanValues(siteAndLand.ParcelNumbers),
                Addresses = CleanValues(siteAndLand.Addresses),
                RestrictionReferences = CleanValues(siteAndLand.RestrictionReferences),
            },
        };
    }

    private static string FirstValue(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : Clean(fallback);

    private static string Clean(string? value) => value?.Trim() ?? "";

    private static List<string> CleanValues(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
}
