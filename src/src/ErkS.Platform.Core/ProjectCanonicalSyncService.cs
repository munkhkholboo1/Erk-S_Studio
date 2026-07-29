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
        ProjectServerFoundation serverFoundation = snapshot.Foundation ?? new();
        ProjectServerInitiationBasis serverBasis = serverFoundation.InitiationBasis ?? new();
        ProjectServerPlanningTask serverPlanningTask = serverFoundation.PlanningTask ?? new();
        PendingProjectInformationUpdate? pending = project.Cloud.PendingProjectInformation;
        bool discardedEmptyPending = pending is not null && IsLegacyEmptyPending(pending);
        if (discardedEmptyPending)
        {
            project.Cloud.PendingProjectInformation = null;
            pending = null;
        }
        bool applyFoundationDetails = serverFoundation.IsAvailable;
        string projectCode = FirstValue(snapshot.ProjectCode, information.ProjectCode);
        string serverProjectName = Clean(snapshot.Name);
        string projectName = serverProjectName;
        string siteAddress = Clean(information.Location);
        string landReference = serverFoundation.IsAvailable
            ? Clean(serverBasis.LandReference)
            : string.Join(", ", CleanValues(siteAndLand.ParcelNumbers));
        string buildingPurpose = Clean(information.BuildingPurpose);
        string clientName = Clean(snapshot.ClientName);
        string planningAuthorityName = Clean(snapshot.PlanningAuthorityName);
        string basisSourceType = Clean(serverBasis.SourceType);
        string requestNumber = Clean(serverBasis.RequestNumber);
        string clientType = ProjectClientTypes.Normalize(serverBasis.ClientType);
        string clientEmail = Clean(serverBasis.ClientEmail);
        string clientRepresentativePosition = Clean(serverBasis.ClientRepresentativePosition);
        string clientRepresentativeName = Clean(serverBasis.ClientRepresentativeName);
        string sourceOrganizationName = Clean(serverBasis.SourceOrganizationName);
        string atdNumber = Clean(serverPlanningTask.AtdNumber);
        string atdStatus = Clean(serverPlanningTask.Status);
        string atdSummary = Clean(serverPlanningTask.Summary);
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
            !string.Equals(planningTask.IssuingAuthorityName, planningAuthorityName, StringComparison.Ordinal) ||
            (applyFoundationDetails &&
                (!string.Equals(basis.SourceType, basisSourceType, StringComparison.Ordinal) ||
                 !string.Equals(basis.RequestNumber, requestNumber, StringComparison.Ordinal) ||
                 !string.Equals(ProjectClientTypes.Normalize(basis.ClientType), clientType, StringComparison.Ordinal) ||
                 !string.Equals(basis.ClientEmail, clientEmail, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(basis.ClientRepresentativePosition, clientRepresentativePosition, StringComparison.Ordinal) ||
                 !string.Equals(basis.ClientRepresentativeName, clientRepresentativeName, StringComparison.Ordinal) ||
                 !string.Equals(basis.SourceOrganizationName, sourceOrganizationName, StringComparison.Ordinal) ||
                 !string.Equals(planningTask.AtdNumber, atdNumber, StringComparison.Ordinal) ||
                 !string.Equals(planningTask.Status, atdStatus, StringComparison.Ordinal) ||
                 !string.Equals(planningTask.Summary, atdSummary, StringComparison.Ordinal)));

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
        if (applyFoundationDetails)
        {
            basis.SourceType = basisSourceType;
            basis.RequestNumber = requestNumber;
            basis.RequestedAtUtc = serverBasis.RequestedAtUtc;
            basis.ClientType = clientType;
            basis.ClientEmail = clientEmail;
            basis.ClientRepresentativePosition = clientRepresentativePosition;
            basis.ClientRepresentativeName = clientRepresentativeName;
            basis.SourceOrganizationName = sourceOrganizationName;
            planningTask.AtdNumber = atdNumber;
            planningTask.IssuedAtUtc = serverPlanningTask.IssuedAtUtc;
            planningTask.Status = atdStatus;
            planningTask.Summary = atdSummary;
            planningTask.Requirements = CleanValues(serverPlanningTask.Requirements);
        }
        basis.ClientOrganizationSnapshot.Name = clientName;
        basis.ClientOrganizationSnapshot.DisplayName = clientName;
        basis.ClientOrganizationSnapshot.OrganizationType = clientType switch
        {
            ProjectClientTypes.GovernmentAuthority => "GovernmentAuthority",
            ProjectClientTypes.Organization => "ClientOrganization",
            _ => "Citizen",
        };

        if (serverFoundation.IsAvailable)
        {
            project.Foundation.Version = Math.Max(1, serverFoundation.Version);
        }
        else if (foundationChanged)
        {
            project.Foundation.Version = Math.Max(
                project.Foundation.Version,
                Math.Max(1, serverFoundation.Version)) + 1;
        }

        return foundationChanged || discardedEmptyPending;
    }

    private static ProjectServerSnapshot Clone(
        ProjectServerSnapshot snapshot,
        string projectId,
        string projectCode,
        string projectName)
    {
        ProjectServerInformation information = snapshot.Information ?? new();
        ProjectServerSiteAndLand siteAndLand = snapshot.SiteAndLand ?? new();
        ProjectServerFoundation serverFoundation = snapshot.Foundation ?? new();
        ProjectServerInitiationBasis serverBasis = serverFoundation.InitiationBasis ?? new();
        ProjectServerPlanningTask serverPlanningTask = serverFoundation.PlanningTask ?? new();
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
            Surface = Clone(snapshot.Surface),
            Information = new ProjectServerInformation
            {
                ProjectId = FirstValue(information.ProjectId, projectId),
                ProjectCode = FirstValue(information.ProjectCode, projectCode),
                Name = Clean(information.Name),
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
            Foundation = new ProjectServerFoundation
            {
                IsAvailable = serverFoundation.IsAvailable,
                Version = Math.Max(1, serverFoundation.Version),
                InitiationBasis = new ProjectServerInitiationBasis
                {
                    SourceType = Clean(serverBasis.SourceType),
                    RequestNumber = Clean(serverBasis.RequestNumber),
                    RequestedAtUtc = serverBasis.RequestedAtUtc,
                    ClientType = ProjectClientTypes.Normalize(serverBasis.ClientType),
                    ClientName = Clean(serverBasis.ClientName),
                    ClientEmail = Clean(serverBasis.ClientEmail),
                    ClientRepresentativePosition = Clean(serverBasis.ClientRepresentativePosition),
                    ClientRepresentativeName = Clean(serverBasis.ClientRepresentativeName),
                    ClientLogoUrl = Clean(serverBasis.ClientLogoUrl),
                    SiteAddress = Clean(serverBasis.SiteAddress),
                    LandReference = Clean(serverBasis.LandReference),
                    SourceOrganizationName = Clean(serverBasis.SourceOrganizationName),
                    ServerRecordId = Clean(serverBasis.ServerRecordId),
                    Summary = Clean(serverBasis.Summary),
                },
                PlanningTask = new ProjectServerPlanningTask
                {
                    AtdNumber = Clean(serverPlanningTask.AtdNumber),
                    IssuedAtUtc = serverPlanningTask.IssuedAtUtc,
                    IssuingAuthorityName = Clean(serverPlanningTask.IssuingAuthorityName),
                    Status = Clean(serverPlanningTask.Status),
                    Summary = Clean(serverPlanningTask.Summary),
                    Requirements = CleanValues(serverPlanningTask.Requirements),
                },
            },
            SiteAndLand = new ProjectServerSiteAndLand
            {
                ParcelNumbers = CleanValues(siteAndLand.ParcelNumbers),
                Addresses = CleanValues(siteAndLand.Addresses),
                RestrictionReferences = CleanValues(siteAndLand.RestrictionReferences),
            },
        };
    }

    private static ProjectServerSurface Clone(ProjectServerSurface? surface)
    {
        surface ??= new ProjectServerSurface();
        return new ProjectServerSurface
        {
            SchemaVersion = Clean(surface.SchemaVersion),
            ProductName = Clean(surface.ProductName),
            Sections = (surface.Sections ?? [])
                .OrderBy(item => item.Order)
                .Select(Clone)
                .ToList(),
            FoundationSections = (surface.FoundationSections ?? [])
                .OrderBy(item => item.Order)
                .Select(Clone)
                .ToList(),
        };
    }

    private static ProjectServerSurfaceSection Clone(ProjectServerSurfaceSection item) => new()
    {
        Id = Clean(item.Id),
        Label = Clean(item.Label),
        Icon = Clean(item.Icon),
        Order = item.Order,
    };

    private static string FirstValue(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : Clean(fallback);

    private static bool IsLegacyEmptyPending(PendingProjectInformationUpdate pending)
    {
        ProjectServerFoundationUpdate foundation = pending.Foundation ?? new();
        bool onlyDefaultClientType =
            string.IsNullOrWhiteSpace(foundation.ClientType) ||
            ProjectClientTypes.Normalize(foundation.ClientType)
                .Equals(ProjectClientTypes.Citizen, StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(pending.BaseConcurrencyToken) &&
            string.IsNullOrWhiteSpace(pending.Name) &&
            string.IsNullOrWhiteSpace(pending.ClientName) &&
            string.IsNullOrWhiteSpace(pending.PlanningAuthorityName) &&
            string.IsNullOrWhiteSpace(pending.DesignOrganizationName) &&
            string.IsNullOrWhiteSpace(pending.Location) &&
            string.IsNullOrWhiteSpace(pending.BuildingPurpose) &&
            string.IsNullOrWhiteSpace(pending.CapacityUnit) &&
            onlyDefaultClientType &&
            string.IsNullOrWhiteSpace(foundation.SourceType) &&
            string.IsNullOrWhiteSpace(foundation.RequestNumber) &&
            string.IsNullOrWhiteSpace(foundation.ClientEmail) &&
            string.IsNullOrWhiteSpace(foundation.ClientRepresentativePosition) &&
            string.IsNullOrWhiteSpace(foundation.ClientRepresentativeName) &&
            string.IsNullOrWhiteSpace(foundation.SiteAddress) &&
            string.IsNullOrWhiteSpace(foundation.LandReference) &&
            string.IsNullOrWhiteSpace(foundation.SourceOrganizationName) &&
            string.IsNullOrWhiteSpace(foundation.BasisSummary) &&
            string.IsNullOrWhiteSpace(foundation.AtdNumber) &&
            string.IsNullOrWhiteSpace(foundation.AtdAuthorityName) &&
            string.IsNullOrWhiteSpace(foundation.AtdStatus) &&
            string.IsNullOrWhiteSpace(foundation.AtdSummary);
    }

    private static string Clean(string? value) => value?.Trim() ?? "";

    private static List<string> CleanValues(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
}
