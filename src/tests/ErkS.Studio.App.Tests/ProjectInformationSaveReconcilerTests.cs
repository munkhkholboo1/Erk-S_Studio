using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class ProjectInformationSaveReconcilerTests
{
    [Fact]
    public void Compare_WhenServerRetainsEarlierCanonicalValues_PreservesPendingRequest()
    {
        StudioCloudProjectInformationUpdateRequest request = CreateRequest();
        StudioCloudProjectDetail response = CreateResponse();
        response.Project.ClientName = "Previous client";
        response.Project.PlanningAuthorityName = "Previous authority";
        DateTimeOffset queuedAt = new(2026, 7, 18, 4, 30, 0, TimeSpan.Zero);

        ProjectInformationReconciliationResult result =
            ProjectInformationSaveReconciler.Compare(request, response, queuedAt);

        Assert.False(result.AcceptedByServer);
        Assert.Equal(["ClientName", "PlanningAuthorityName"], result.Differences);
        Assert.NotNull(result.PendingUpdate);
        Assert.Equal(request.ClientName, result.PendingUpdate.ClientName);
        Assert.Equal(request.PlanningAuthorityName, result.PendingUpdate.PlanningAuthorityName);
        Assert.Equal(queuedAt, result.PendingUpdate.QueuedAtUtc);
    }

    [Fact]
    public void Compare_WhenCanonicalResponseMatchesRequest_ClearsPendingState()
    {
        StudioCloudProjectInformationUpdateRequest request = CreateRequest();
        StudioCloudProjectDetail response = CreateResponse();
        response.Project.Name = $"  {response.Project.Name}  ";
        response.ProjectInformation.Location = $" {response.ProjectInformation.Location} ";

        ProjectInformationReconciliationResult result =
            ProjectInformationSaveReconciler.Compare(request, response, DateTimeOffset.UtcNow);

        Assert.True(result.AcceptedByServer);
        Assert.Empty(result.Differences);
        Assert.Null(result.PendingUpdate);
    }

    [Fact]
    public void Compare_DesignRole_IgnoresAuthorityAndClientFieldsTheServerProtects()
    {
        StudioCloudProjectInformationUpdateRequest request = CreateRequest();
        StudioCloudProjectDetail response = CreateResponse();
        response.Project.CurrentUserRoles = ["DesignCompanyAdmin", "Architect"];
        response.Project.ClientName = "Canonical client";
        response.Project.PlanningAuthorityName = "Canonical authority";
        response.Foundation!.PlanningTask.AtdNumber = "Canonical ATD";
        response.Foundation.PlanningTask.IssuingAuthorityName = "Canonical authority";
        response.Foundation.PlanningTask.Status = "Issued";
        response.Foundation.PlanningTask.Summary = "Canonical ATD summary";

        ProjectInformationReconciliationResult result =
            ProjectInformationSaveReconciler.Compare(request, response, DateTimeOffset.UtcNow);

        Assert.True(result.AcceptedByServer);
        Assert.Empty(result.Differences);
        Assert.Null(result.PendingUpdate);
    }

    [Fact]
    public void Compare_UsesCanonicalSiteAddressWhenInformationLocationIsEmpty()
    {
        StudioCloudProjectInformationUpdateRequest request = CreateRequest();
        StudioCloudProjectDetail response = CreateResponse();
        response.ProjectInformation.Location = "";
        response.SiteAndLand.Addresses = [request.Location];

        ProjectInformationReconciliationResult result =
            ProjectInformationSaveReconciler.Compare(request, response, DateTimeOffset.UtcNow);

        Assert.True(result.AcceptedByServer);
        Assert.DoesNotContain("Location", result.Differences);
    }

    [Fact]
    public void FoundationDraft_ClonesAtdDocumentsAndBuildsRequestFromCapturedValues()
    {
        var sourceDocument = new ProjectFileReference
        {
            Id = "atd-1",
            Title = "Approved ATD",
            RelativePath = "documents/atd.pdf",
        };
        ProjectFoundationEditDraft draft = new(
            name: "Edited project",
            basisSourceType: "ATDRequest",
            requestNumber: "REQ-42",
            clientType: ProjectClientTypes.Organization,
            clientName: "Edited client",
            clientEmail: "client@example.test",
            clientRepresentativePosition: "Захирал",
            clientRepresentativeName: "Д.Бат",
            clientLogoPath: "foundation/documents/client-logo/logo.png",
            clientLogoOriginalFileName: "client-logo.png",
            siteAddress: "Edited address",
            landReference: "parcel-1",
            sourceOrganizationName: "Planning authority",
            basisSummary: "Edited purpose",
            atdNumber: "ATD-42",
            atdAuthorityName: "Edited authority",
            atdStatus: "Approved",
            atdSummary: "ATD summary",
            atdDocuments: [sourceDocument],
            conceptDesignApproval: new ConceptDesignApprovalRoster
            {
                IsConfigured = true,
                ApprovedBy = [new ProjectApprovalEntry { PositionTitle = "Ерөнхий архитектор", PersonName = "А.Даш" }],
                EndorsedBy =
                [
                    new ProjectApprovalEntry { PositionTitle = "Хэлтсийн дарга", PersonName = "Х.Чимэг" },
                    new ProjectApprovalEntry { PositionTitle = "Мэргэжилтэн", PersonName = "Х.Туяа" },
                ],
            });

        sourceDocument.Title = "Changed after capture";
        StudioCloudProjectInformationUpdateRequest request =
            draft.CreateCloudRequest("Design organization", "m2");

        Assert.Equal("Approved ATD", Assert.Single(draft.AtdDocuments).Title);
        Assert.Equal("Захирал", draft.ClientRepresentativePosition);
        Assert.Equal("Д.Бат", draft.ClientRepresentativeName);
        Assert.Equal("client-logo.png", draft.ClientLogoOriginalFileName);
        Assert.Equal("А.Даш", Assert.Single(draft.ConceptDesignApproval.ApprovedBy).PersonName);
        Assert.Equal("Edited project", request.Name);
        Assert.Equal("Edited client", request.ClientName);
        Assert.Equal("Edited authority", request.PlanningAuthorityName);
        Assert.Equal("Edited address", request.Location);
        Assert.Equal("Edited purpose", request.BuildingPurpose);
        Assert.Equal(ProjectClientTypes.Organization, request.Foundation.ClientType);
        Assert.Equal(draft.ClientRepresentativePosition, request.Foundation.ClientRepresentativePosition);
        Assert.Equal(draft.ClientRepresentativeName, request.Foundation.ClientRepresentativeName);
        Assert.Equal("REQ-42", request.Foundation.RequestNumber);
        Assert.Equal("parcel-1", request.Foundation.LandReference);
        Assert.Equal("ATD-42", request.Foundation.AtdNumber);
        Assert.Equal("Approved", request.Foundation.AtdStatus);
    }

    [Fact]
    public void Compare_WhenOlderServerOmitsFoundation_KeepsFoundationUpdatePending()
    {
        StudioCloudProjectInformationUpdateRequest request = CreateRequest();
        StudioCloudProjectDetail response = CreateResponse();
        response.Foundation = null;

        ProjectInformationReconciliationResult result =
            ProjectInformationSaveReconciler.Compare(request, response, DateTimeOffset.UtcNow);

        Assert.False(result.AcceptedByServer);
        Assert.Contains("Foundation", result.Differences);
        Assert.True(result.PendingUpdate!.Foundation.IsAvailable);
        Assert.Equal(request.Foundation.AtdNumber, result.PendingUpdate.Foundation.AtdNumber);
    }

    private static StudioCloudProjectInformationUpdateRequest CreateRequest() => new()
    {
        Name = "Edited project",
        ClientName = "Edited client",
        PlanningAuthorityName = "Edited authority",
        DesignOrganizationName = "Design organization",
        Location = "Edited location",
        BuildingPurpose = "Edited purpose",
        CapacityUnit = "m2",
        Foundation = new StudioCloudProjectFoundationUpdate
        {
            SourceType = "ATDRequest",
            RequestNumber = "REQ-42",
            ClientType = ProjectClientTypes.Organization,
            ClientEmail = "client@example.test",
            ClientRepresentativePosition = "Director",
            ClientRepresentativeName = "Client Representative",
            SiteAddress = "Edited location",
            LandReference = "parcel-1",
            SourceOrganizationName = "Edited authority",
            BasisSummary = "Edited purpose",
            AtdNumber = "ATD-42",
            AtdAuthorityName = "Edited authority",
            AtdStatus = "Approved",
            AtdSummary = "ATD summary",
        },
    };

    private static StudioCloudProjectDetail CreateResponse() => new()
    {
        Project = new StudioCloudProjectSummary
        {
            Name = "Edited project",
            ClientName = "Edited client",
            PlanningAuthorityName = "Edited authority",
            DesignOrganizationName = "Design organization",
        },
        ProjectInformation = new StudioCloudProjectInformation
        {
            Name = "Edited project",
            Location = "Edited location",
            BuildingPurpose = "Edited purpose",
            CapacityUnit = "m2",
        },
        Foundation = new StudioCloudProjectFoundation
        {
            InitiationBasis = new StudioCloudProjectInitiationBasis
            {
                SourceType = "ATDRequest",
                RequestNumber = "REQ-42",
                ClientType = ProjectClientTypes.Organization,
                ClientEmail = "client@example.test",
                ClientRepresentativePosition = "Director",
                ClientRepresentativeName = "Client Representative",
                SiteAddress = "Edited location",
                LandReference = "parcel-1",
                SourceOrganizationName = "Edited authority",
                Summary = "Edited purpose",
            },
            PlanningTask = new StudioCloudPlanningTask
            {
                AtdNumber = "ATD-42",
                IssuingAuthorityName = "Edited authority",
                Status = "Approved",
                Summary = "ATD summary",
            },
        },
    };
}
