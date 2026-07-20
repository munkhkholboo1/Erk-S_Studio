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
        ProjectServerFoundationUpdate? pendingFoundation = pending?.Foundation is { IsAvailable: true } value
            ? value
            : null;
        bool applyFoundationDetails = serverFoundation.IsAvailable || pendingFoundation is not null;
        string projectCode = FirstValue(snapshot.ProjectCode, information.ProjectCode);
        string serverProjectName = FirstValue(snapshot.Name, information.Name);
        string projectName = pending is null ? serverProjectName : Clean(pending.Name);
        string serverSiteAddress = serverFoundation.IsAvailable
            ? FirstValue(serverBasis.SiteAddress, information.Location)
            : FirstValue(information.Location, siteAndLand.Addresses.FirstOrDefault());
        string siteAddress = pending is null ? serverSiteAddress : Clean(pending.Location);
        string serverLandReference = serverFoundation.IsAvailable
            ? Clean(serverBasis.LandReference)
            : string.Join(", ", CleanValues(siteAndLand.ParcelNumbers));
        string landReference = pendingFoundation is null
            ? serverLandReference
            : Clean(pendingFoundation.LandReference);
        string serverBuildingPurpose = serverFoundation.IsAvailable
            ? FirstValue(serverBasis.Summary, information.BuildingPurpose)
            : Clean(information.BuildingPurpose);
        string buildingPurpose = pending is null ? serverBuildingPurpose : Clean(pending.BuildingPurpose);
        string serverClientName = serverFoundation.IsAvailable
            ? FirstValue(serverBasis.ClientName, snapshot.ClientName)
            : Clean(snapshot.ClientName);
        string clientName = pending is null ? serverClientName : Clean(pending.ClientName);
        string serverPlanningAuthority = serverFoundation.IsAvailable
            ? FirstValue(serverPlanningTask.IssuingAuthorityName, snapshot.PlanningAuthorityName)
            : Clean(snapshot.PlanningAuthorityName);
        string planningAuthorityName = pending is null
            ? serverPlanningAuthority
            : Clean(pending.PlanningAuthorityName);
        string basisSourceType = pendingFoundation is null
            ? Clean(serverBasis.SourceType)
            : Clean(pendingFoundation.SourceType);
        string requestNumber = pendingFoundation is null
            ? Clean(serverBasis.RequestNumber)
            : Clean(pendingFoundation.RequestNumber);
        string clientType = pendingFoundation is null
            ? ProjectClientTypes.Normalize(serverBasis.ClientType)
            : ProjectClientTypes.Normalize(pendingFoundation.ClientType);
        string clientEmail = pendingFoundation is null
            ? Clean(serverBasis.ClientEmail)
            : Clean(pendingFoundation.ClientEmail);
        string clientRepresentativePosition = pendingFoundation is null
            ? Clean(serverBasis.ClientRepresentativePosition)
            : Clean(pendingFoundation.ClientRepresentativePosition);
        string clientRepresentativeName = pendingFoundation is null
            ? Clean(serverBasis.ClientRepresentativeName)
            : Clean(pendingFoundation.ClientRepresentativeName);
        string sourceOrganizationName = pendingFoundation is null
            ? Clean(serverBasis.SourceOrganizationName)
            : Clean(pendingFoundation.SourceOrganizationName);
        string atdNumber = pendingFoundation is null
            ? Clean(serverPlanningTask.AtdNumber)
            : Clean(pendingFoundation.AtdNumber);
        string atdStatus = pendingFoundation is null
            ? Clean(serverPlanningTask.Status)
            : Clean(pendingFoundation.AtdStatus);
        string atdSummary = pendingFoundation is null
            ? Clean(serverPlanningTask.Summary)
            : Clean(pendingFoundation.AtdSummary);
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

        if (serverFoundation.IsAvailable && pendingFoundation is null)
        {
            project.Foundation.Version = Math.Max(1, serverFoundation.Version);
        }
        else if (foundationChanged)
        {
            project.Foundation.Version = Math.Max(
                project.Foundation.Version,
                Math.Max(1, serverFoundation.Version)) + 1;
        }

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

    private static string Clean(string? value) => value?.Trim() ?? "";

    private static List<string> CleanValues(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
}
