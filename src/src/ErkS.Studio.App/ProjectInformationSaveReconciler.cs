using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record ProjectInformationReconciliationResult(
    bool AcceptedByServer,
    PendingProjectInformationUpdate? PendingUpdate,
    IReadOnlyList<string> Differences);

internal static class ProjectInformationSaveReconciler
{
    public static ProjectInformationReconciliationResult Compare(
        StudioCloudProjectInformationUpdateRequest request,
        StudioCloudProjectDetail response,
        DateTimeOffset queuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        StudioCloudProjectSummary project = response.Project ?? new();
        StudioCloudProjectInformation information = response.ProjectInformation ?? new();
        StudioCloudSiteAndLand siteAndLand = response.SiteAndLand ?? new();
        StudioCloudProjectFoundation? foundation = response.Foundation;
        List<string> differences = [];
        string[] roles = project.CurrentUserRoles ?? [];
        bool roleAware = roles.Length > 0;
        bool projectAdmin = HasRole(roles, "ProjectAdmin");
        bool authority = HasRole(
            roles,
            "AuthoritySpecialist",
            "AuthorityDepartmentHead",
            "ChiefArchitect");
        bool client = HasRole(roles, "Client");
        bool design = HasRole(roles, "DesignCompanyAdmin", "MajorArchitect", "Architect");
        bool canEditCommon = !roleAware || projectAdmin || authority || client || design;
        bool canEditClient = !roleAware || projectAdmin || authority || client;
        bool canEditAuthority = !roleAware || projectAdmin || authority;
        bool canEditDesignOrganization = !roleAware || projectAdmin || design;
        bool canEditInitiation = !roleAware || projectAdmin || authority || client || design;
        bool canEditAtd = !roleAware || projectAdmin || authority;

        if (canEditCommon)
        {
            AddDifference(differences, "Name", request.Name, First(project.Name, information.Name));
            AddDifference(
                differences,
                "Location",
                request.Location,
                First(information.Location, siteAndLand.Addresses?.FirstOrDefault()));
            AddDifference(
                differences,
                "BuildingPurpose",
                request.BuildingPurpose,
                information.BuildingPurpose);
            AddDifference(differences, "CapacityUnit", request.CapacityUnit, information.CapacityUnit);
        }
        if (canEditClient)
            AddDifference(differences, "ClientName", request.ClientName, project.ClientName);
        if (canEditAuthority)
        {
            AddDifference(
                differences,
                "PlanningAuthorityName",
                request.PlanningAuthorityName,
                project.PlanningAuthorityName);
        }
        if (canEditDesignOrganization)
        {
            AddDifference(
                differences,
                "DesignOrganizationName",
                request.DesignOrganizationName,
                project.DesignOrganizationName);
        }

        if (foundation is null)
        {
            if ((canEditInitiation && HasInitiationFoundationValues(request.Foundation)) ||
                (canEditAtd && HasAtdFoundationValues(request.Foundation)))
                differences.Add("Foundation");
        }
        else
        {
            StudioCloudProjectInitiationBasis initiation = foundation.InitiationBasis ?? new();
            StudioCloudPlanningTask planningTask = foundation.PlanningTask ?? new();
            if (canEditInitiation)
            {
                AddDifference(differences, "Foundation.SourceType", request.Foundation.SourceType, initiation.SourceType);
                AddDifference(differences, "Foundation.RequestNumber", request.Foundation.RequestNumber, initiation.RequestNumber);
                AddDifference(differences, "Foundation.ClientEmail", request.Foundation.ClientEmail, initiation.ClientEmail);
                AddDifference(differences, "Foundation.SiteAddress", request.Foundation.SiteAddress, initiation.SiteAddress);
                AddDifference(differences, "Foundation.LandReference", request.Foundation.LandReference, initiation.LandReference);
                AddDifference(
                    differences,
                    "Foundation.SourceOrganizationName",
                    request.Foundation.SourceOrganizationName,
                    initiation.SourceOrganizationName);
                AddDifference(differences, "Foundation.BasisSummary", request.Foundation.BasisSummary, initiation.Summary);
            }
            if (canEditAtd)
            {
                AddDifference(differences, "Foundation.AtdNumber", request.Foundation.AtdNumber, planningTask.AtdNumber);
                AddDifference(
                    differences,
                    "Foundation.AtdAuthorityName",
                    request.Foundation.AtdAuthorityName,
                    planningTask.IssuingAuthorityName);
                AddDifference(differences, "Foundation.AtdStatus", request.Foundation.AtdStatus, planningTask.Status);
                AddDifference(differences, "Foundation.AtdSummary", request.Foundation.AtdSummary, planningTask.Summary);
            }
        }

        if (differences.Count == 0)
            return new ProjectInformationReconciliationResult(true, null, differences);

        return new ProjectInformationReconciliationResult(
            false,
            CreatePendingUpdate(request, queuedAtUtc),
            differences);
    }

    public static PendingProjectInformationUpdate CreatePendingUpdate(
        StudioCloudProjectInformationUpdateRequest request,
        DateTimeOffset queuedAtUtc) => new()
    {
        Name = Clean(request.Name),
        ClientName = Clean(request.ClientName),
        PlanningAuthorityName = Clean(request.PlanningAuthorityName),
        DesignOrganizationName = Clean(request.DesignOrganizationName),
        Location = Clean(request.Location),
        BuildingPurpose = Clean(request.BuildingPurpose),
        CapacityUnit = Clean(request.CapacityUnit),
        Foundation = new ProjectServerFoundationUpdate
        {
            IsAvailable = true,
            SourceType = Clean(request.Foundation.SourceType),
            RequestNumber = Clean(request.Foundation.RequestNumber),
            ClientEmail = Clean(request.Foundation.ClientEmail),
            SiteAddress = Clean(request.Foundation.SiteAddress),
            LandReference = Clean(request.Foundation.LandReference),
            SourceOrganizationName = Clean(request.Foundation.SourceOrganizationName),
            BasisSummary = Clean(request.Foundation.BasisSummary),
            AtdNumber = Clean(request.Foundation.AtdNumber),
            AtdAuthorityName = Clean(request.Foundation.AtdAuthorityName),
            AtdStatus = Clean(request.Foundation.AtdStatus),
            AtdSummary = Clean(request.Foundation.AtdSummary),
        },
        QueuedAtUtc = queuedAtUtc,
    };

    public static StudioCloudProjectInformationUpdateRequest CreateRequest(
        PendingProjectInformationUpdate pending) => new()
    {
        Name = pending.Name,
        ClientName = pending.ClientName,
        PlanningAuthorityName = pending.PlanningAuthorityName,
        DesignOrganizationName = pending.DesignOrganizationName,
        Location = pending.Location,
        BuildingPurpose = pending.BuildingPurpose,
        CapacityUnit = pending.CapacityUnit,
        Foundation = new StudioCloudProjectFoundationUpdate
        {
            SourceType = pending.Foundation.SourceType,
            RequestNumber = pending.Foundation.RequestNumber,
            ClientEmail = pending.Foundation.ClientEmail,
            SiteAddress = pending.Foundation.SiteAddress,
            LandReference = pending.Foundation.LandReference,
            SourceOrganizationName = pending.Foundation.SourceOrganizationName,
            BasisSummary = pending.Foundation.BasisSummary,
            AtdNumber = pending.Foundation.AtdNumber,
            AtdAuthorityName = pending.Foundation.AtdAuthorityName,
            AtdStatus = pending.Foundation.AtdStatus,
            AtdSummary = pending.Foundation.AtdSummary,
        },
    };

    private static void AddDifference(
        ICollection<string> differences,
        string field,
        string? requested,
        string? canonical)
    {
        if (!string.Equals(Clean(requested), Clean(canonical), StringComparison.Ordinal))
            differences.Add(field);
    }

    private static string First(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : Clean(fallback);

    private static bool HasInitiationFoundationValues(StudioCloudProjectFoundationUpdate foundation) =>
        !string.IsNullOrWhiteSpace(foundation.SourceType) ||
        !string.IsNullOrWhiteSpace(foundation.RequestNumber) ||
        !string.IsNullOrWhiteSpace(foundation.ClientEmail) ||
        !string.IsNullOrWhiteSpace(foundation.SiteAddress) ||
        !string.IsNullOrWhiteSpace(foundation.LandReference) ||
        !string.IsNullOrWhiteSpace(foundation.SourceOrganizationName) ||
        !string.IsNullOrWhiteSpace(foundation.BasisSummary);

    private static bool HasAtdFoundationValues(StudioCloudProjectFoundationUpdate foundation) =>
        !string.IsNullOrWhiteSpace(foundation.AtdNumber) ||
        !string.IsNullOrWhiteSpace(foundation.AtdAuthorityName) ||
        !string.IsNullOrWhiteSpace(foundation.AtdStatus) ||
        !string.IsNullOrWhiteSpace(foundation.AtdSummary);

    private static bool HasRole(IEnumerable<string> roles, params string[] accepted) =>
        roles.Any(role => accepted.Any(value => role.Equals(value, StringComparison.OrdinalIgnoreCase)));

    private static string Clean(string? value) => value?.Trim() ?? "";
}

internal sealed class ProjectFoundationEditDraft
{
    public ProjectFoundationEditDraft(
        string name,
        string basisSourceType,
        string requestNumber,
        string clientType,
        string clientName,
        string clientEmail,
        string clientRepresentativePosition,
        string clientRepresentativeName,
        string clientLogoPath,
        string clientLogoOriginalFileName,
        string siteAddress,
        string landReference,
        string sourceOrganizationName,
        string basisSummary,
        string atdNumber,
        string atdAuthorityName,
        string atdStatus,
        string atdSummary,
        IEnumerable<ProjectFileReference> atdDocuments,
        ConceptDesignApprovalRoster conceptDesignApproval)
    {
        Name = Clean(name);
        BasisSourceType = Clean(basisSourceType);
        RequestNumber = Clean(requestNumber);
        ClientType = ProjectClientTypes.Normalize(clientType);
        ClientName = Clean(clientName);
        ClientEmail = Clean(clientEmail);
        ClientRepresentativePosition = Clean(clientRepresentativePosition);
        ClientRepresentativeName = Clean(clientRepresentativeName);
        ClientLogoPath = Clean(clientLogoPath);
        ClientLogoOriginalFileName = Clean(clientLogoOriginalFileName);
        SiteAddress = Clean(siteAddress);
        LandReference = Clean(landReference);
        SourceOrganizationName = Clean(sourceOrganizationName);
        BasisSummary = basisSummary ?? "";
        AtdNumber = Clean(atdNumber);
        AtdAuthorityName = Clean(atdAuthorityName);
        AtdStatus = Clean(atdStatus);
        AtdSummary = atdSummary ?? "";
        AtdDocuments = atdDocuments.Select(document => document.Clone()).ToArray();
        ConceptDesignApproval = conceptDesignApproval.Clone();
    }

    public string Name { get; }
    public string BasisSourceType { get; }
    public string RequestNumber { get; }
    public string ClientType { get; }
    public string ClientName { get; }
    public string ClientEmail { get; }
    public string ClientRepresentativePosition { get; }
    public string ClientRepresentativeName { get; }
    public string ClientLogoPath { get; }
    public string ClientLogoOriginalFileName { get; }
    public string SiteAddress { get; }
    public string LandReference { get; }
    public string SourceOrganizationName { get; }
    public string BasisSummary { get; }
    public string AtdNumber { get; }
    public string AtdAuthorityName { get; }
    public string AtdStatus { get; }
    public string AtdSummary { get; }
    public IReadOnlyList<ProjectFileReference> AtdDocuments { get; }
    public ConceptDesignApprovalRoster ConceptDesignApproval { get; }

    public StudioCloudProjectInformationUpdateRequest CreateCloudRequest(
        string designOrganizationName,
        string capacityUnit) => new()
    {
        Name = Name,
        ClientName = ClientName,
        PlanningAuthorityName = AtdAuthorityName,
        DesignOrganizationName = Clean(designOrganizationName),
        Location = SiteAddress,
        BuildingPurpose = BasisSummary.Trim(),
        CapacityUnit = Clean(capacityUnit),
        Foundation = new StudioCloudProjectFoundationUpdate
        {
            SourceType = BasisSourceType,
            RequestNumber = RequestNumber,
            ClientEmail = ClientEmail,
            SiteAddress = SiteAddress,
            LandReference = LandReference,
            SourceOrganizationName = SourceOrganizationName,
            BasisSummary = BasisSummary.Trim(),
            AtdNumber = AtdNumber,
            AtdAuthorityName = AtdAuthorityName,
            AtdStatus = AtdStatus,
            AtdSummary = AtdSummary.Trim(),
        },
    };

    private static string Clean(string? value) => value?.Trim() ?? "";
}
