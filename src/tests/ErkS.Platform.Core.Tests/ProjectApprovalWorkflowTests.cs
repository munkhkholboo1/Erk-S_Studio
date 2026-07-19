using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectApprovalWorkflowTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-approval-tests-" + Guid.NewGuid().ToString("N"));

    public ProjectApprovalWorkflowTests() => Directory.CreateDirectory(workDirectory);

    [Fact]
    public void ConfiguredRoster_PreservesOrderAndEnforcesCoverLimits()
    {
        var workflow = new ProjectApprovalWorkflow
        {
            ConceptDesign = new ConceptDesignApprovalRoster
            {
                IsConfigured = true,
                ApprovedBy =
                [
                    Entry("Чөлөөт бүс", "Ерөнхий архитектор", "Ч.Бат"),
                    Entry("Аймаг", "Ерөнхий архитектор", "А.Даш"),
                    Entry("Нийслэл", "Ерөнхий архитектор", "Н.Арх"),
                    Entry("Илүү мөр", "Ерөнхий архитектор", "И.Мөр"),
                ],
                EndorsedBy = [Entry("Хот байгуулалтын газар", "Хэлтсийн дарга", "Х.Чимэг")],
            },
        };

        ConceptCoverApprovalSnapshot snapshot = ConceptCoverApprovalResolver.Resolve(
            workflow,
            new PlanningTaskInformation());

        Assert.Equal(3, snapshot.ApprovedBy.Count);
        Assert.Equal("Ч.Бат", snapshot.ApprovedBy[0].PersonName);
        Assert.Equal("А.Даш", snapshot.ApprovedBy[1].PersonName);
        Assert.Equal("Н.Арх", snapshot.ApprovedBy[2].PersonName);
        Assert.Equal(2, snapshot.EndorsedBy.Count);
        Assert.Equal("Х.Чимэг", snapshot.EndorsedBy[0].PersonName);
        Assert.Equal("", snapshot.EndorsedBy[1].PersonName);
    }

    [Fact]
    public void WorkingDrawingConsultations_DoNotEnterConceptCover()
    {
        var workflow = new ProjectApprovalWorkflow
        {
            ConceptDesign = new ConceptDesignApprovalRoster
            {
                IsConfigured = true,
                ApprovedBy = [Entry("Аймаг", "Ерөнхий архитектор", "А.Даш")],
                EndorsedBy =
                [
                    Entry("Хот байгуулалтын газар", "Хэлтсийн дарга", "Х.Чимэг"),
                    Entry("Хот байгуулалтын газар", "Мэргэжилтэн", "Х.Туяа"),
                ],
            },
            WorkingDrawingConsultedBy =
            [
                Entry("Онцгой байдлын газар", "Мэргэжилтэн", "О.Бат"),
                Entry("Байгаль орчны газар", "Мэргэжилтэн", "Б.Нар"),
            ],
        };

        ConceptCoverApprovalSnapshot snapshot = ConceptCoverApprovalResolver.Resolve(
            workflow,
            new PlanningTaskInformation());

        Assert.Equal(["Х.Чимэг", "Х.Туяа"], snapshot.EndorsedBy.Select(entry => entry.PersonName));
        Assert.DoesNotContain(snapshot.EndorsedBy, entry => entry.OrganizationName.Contains("Онцгой"));
    }

    [Fact]
    public void ElevationHeader_IncludesOnlyExplicitlySelectedEndorsedOfficials()
    {
        ProjectApprovalEntry selected = Entry("Urban authority", "Specialist", "H.Tuya");
        selected.IncludeInElevationHeader = true;
        ProjectApprovalEntry notSelected = Entry("Emergency authority", "Specialist", "O.Bat");
        var workflow = new ProjectApprovalWorkflow
        {
            ConceptDesign = new ConceptDesignApprovalRoster
            {
                IsConfigured = true,
                ApprovedBy = [Entry("City", "Chief architect", "A.Dash")],
                EndorsedBy = [selected, notSelected],
            },
        };

        ConceptElevationHeaderSnapshot snapshot = ConceptElevationHeaderResolver.Resolve(
            workflow,
            new PlanningTaskInformation());

        Assert.Equal("A.Dash", Assert.Single(snapshot.ApprovedBy).PersonName);
        Assert.Equal("H.Tuya", Assert.Single(snapshot.ReviewedBy).PersonName);
        Assert.DoesNotContain(snapshot.ReviewedBy, entry => entry.PersonName == "O.Bat");
    }

    [Fact]
    public void LegacyAuthorityMembers_RemainCompatibleUntilRosterIsConfigured()
    {
        var planningTask = new PlanningTaskInformation
        {
            AuthorityMembers =
            [
                Member("chief", "А.Даш", "Chief Architect"),
                Member("head", "Х.Чимэг", "Department Head"),
                Member("specialist", "Х.Туяа", "Authority Specialist"),
            ],
        };

        ConceptCoverApprovalSnapshot snapshot = ConceptCoverApprovalResolver.Resolve(
            new ProjectApprovalWorkflow(),
            planningTask);

        Assert.Equal("А.Даш", Assert.Single(snapshot.ApprovedBy).PersonName);
        Assert.Equal("Ерөнхий архитектор", snapshot.ApprovedBy[0].PositionTitle);
        Assert.Equal(["Х.Чимэг", "Х.Туяа"], snapshot.EndorsedBy.Select(entry => entry.PersonName));
        Assert.Equal("Хэлтсийн дарга", snapshot.EndorsedBy[0].PositionTitle);
        Assert.Equal("Хот байгуулалтын мэргэжилтэн", snapshot.EndorsedBy[1].PositionTitle);
    }

    [Fact]
    public void WorkspaceStore_RoundTripsRosterAndClientRepresentative()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("APPROVAL-01", "Approval test");
        project.Foundation.InitiationBasis.ClientType = ProjectClientTypes.Organization;
        project.Foundation.InitiationBasis.ClientName = "Захиалагч ХХК";
        project.Foundation.InitiationBasis.ClientRepresentativePosition = "Гүйцэтгэх захирал";
        project.Foundation.InitiationBasis.ClientRepresentativeName = "Д.Бат";
        project.Foundation.ApprovalWorkflow = new ProjectApprovalWorkflow
        {
            ConceptDesign = new ConceptDesignApprovalRoster
            {
                IsConfigured = true,
                ApprovedBy =
                [
                    Entry("Чөлөөт бүс", "Ерөнхий архитектор", "Ч.Бат"),
                    Entry("Аймаг", "Ерөнхий архитектор", "А.Даш"),
                ],
                EndorsedBy =
                [
                    new ProjectApprovalEntry
                    {
                        OrganizationName = "Хот байгуулалтын газар",
                        PositionTitle = "Хэлтсийн дарга",
                        PersonName = "Х.Чимэг",
                        IncludeInElevationHeader = true,
                    },
                    Entry("Онцгой байдлын газар", "Мэргэжилтэн", "О.Бат"),
                ],
            },
            WorkingDrawingConsultedBy =
            [
                Entry("Эрүүл ахуйн газар", "Мэргэжилтэн", "Э.Нэр"),
            ],
        };
        string path = Path.Combine(workDirectory, "project.erksproject");

        ProjectWorkspaceStore.Save(project, path);
        ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);

        Assert.Equal("Гүйцэтгэх захирал", loaded.Foundation.InitiationBasis.ClientRepresentativePosition);
        Assert.Equal("Д.Бат", loaded.Foundation.InitiationBasis.ClientRepresentativeName);
        Assert.True(loaded.Foundation.ApprovalWorkflow.ConceptDesign.IsConfigured);
        Assert.Equal(2, loaded.Foundation.ApprovalWorkflow.ConceptDesign.ApprovedBy.Count);
        Assert.Equal("Ч.Бат", loaded.Foundation.ApprovalWorkflow.ConceptDesign.ApprovedBy[0].PersonName);
        Assert.Equal("А.Даш", loaded.Foundation.ApprovalWorkflow.ConceptDesign.ApprovedBy[1].PersonName);
        Assert.True(loaded.Foundation.ApprovalWorkflow.ConceptDesign.EndorsedBy[0].IncludeInElevationHeader);
        Assert.False(loaded.Foundation.ApprovalWorkflow.ConceptDesign.EndorsedBy[1].IncludeInElevationHeader);
        Assert.Equal("Э.Нэр", Assert.Single(
            loaded.Foundation.ApprovalWorkflow.WorkingDrawingConsultedBy).PersonName);
    }

    [Fact]
    public void AlbumProjectStore_LoadsLegacyFileWithoutApprovalWorkflow()
    {
        string path = Path.Combine(workDirectory, "legacy.erksalbum");
        File.WriteAllText(
            path,
            """
            {
              "formatVersion": 2,
              "name": "Legacy album",
              "planningTask": {
                "authorityMembers": [
                  { "id": "chief", "fullName": "А.Даш", "roles": ["Chief Architect"] }
                ]
              }
            }
            """);

        AlbumProject loaded = AlbumProjectStore.Load(path);
        ConceptCoverApprovalSnapshot snapshot = ConceptCoverApprovalResolver.Resolve(
            loaded.ApprovalWorkflow,
            loaded.PlanningTask);

        Assert.False(loaded.ApprovalWorkflow.ConceptDesign.IsConfigured);
        Assert.Equal("А.Даш", Assert.Single(snapshot.ApprovedBy).PersonName);
        Assert.Equal(ProjectApprovalRosterLimits.MinEndorsedBy, snapshot.EndorsedBy.Count);
    }

    [Fact]
    public void DisplayPosition_KeepsOrganizationAndPositionAsSeparateLines()
    {
        string display = ConceptCoverApprovalResolver.DisplayPosition(
            Entry("Хот байгуулалтын газар", "Хэлтсийн дарга", "Х.Чимэг"));

        Assert.Equal("Хот байгуулалтын газар" + Environment.NewLine + "Хэлтсийн дарга", display);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private static ProjectApprovalEntry Entry(string organization, string position, string name) => new()
    {
        OrganizationName = organization,
        PositionTitle = position,
        PersonName = name,
    };

    private static ProjectMember Member(string id, string name, string role) => new()
    {
        Id = id,
        FullName = name,
        Roles = [role],
    };
}
