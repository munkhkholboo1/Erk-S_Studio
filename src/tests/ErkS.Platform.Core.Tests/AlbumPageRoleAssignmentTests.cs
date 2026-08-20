using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class AlbumPageRoleAssignmentTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-page-role-tests",
        Guid.NewGuid().ToString("N"));

    public AlbumPageRoleAssignmentTests() => Directory.CreateDirectory(workDirectory);

    [Fact]
    public void BulkApply_AssignsTeamMemberToSelectedSourceAndGeneratedPagesOnly()
    {
        var selectedSource = new AlbumPageDefinition();
        var untouchedSource = new AlbumPageDefinition();
        var selectedGenerated = new AlbumCompositionItem();
        var member = new ProjectMember
        {
            Id = "participant-engineer-01",
            FamilyName = "Дорж",
            GivenName = "Бат",
            FullName = "Дорж Бат",
            Email = "bat@example.test",
            Roles = ["Engineer"],
        };

        int changed = AlbumPageRoleAssignmentService.Apply(
            [selectedSource, selectedGenerated],
            AlbumPageRoleCodes.PreparedBy,
            member);

        Assert.Equal(2, changed);
        Assert.Empty(untouchedSource.RoleAssignments);
        Assert.All(
            new IAlbumPageRoleOwner[] { selectedSource, selectedGenerated },
            target =>
            {
                AlbumPageRoleAssignment assignment = Assert.Single(target.RoleAssignments);
                Assert.Equal(AlbumPageRoleCodes.PreparedBy, assignment.RoleCode);
                Assert.Equal(member.Id, assignment.ParticipantId);
                Assert.Equal(member.FamilyName, assignment.FamilyName);
                Assert.Equal(member.GivenName, assignment.GivenName);
                Assert.Equal(member.FullName, assignment.FullName);
                Assert.Equal(member.Email, assignment.Email);
            });
    }

    [Fact]
    public void BulkApply_ReplacesOneRoleWithoutDuplicatingAndCanRestoreInheritance()
    {
        var page = new AlbumPageDefinition();
        var first = new ProjectMember { Id = "member-01", FullName = "А.Бат" };
        var second = new ProjectMember { Id = "member-02", FullName = "Б.Дорж" };

        AlbumPageRoleAssignmentService.Apply([page], AlbumPageRoleCodes.CheckedBy, first);
        AlbumPageRoleAssignmentService.Apply([page], AlbumPageRoleCodes.CheckedBy, second);

        AlbumPageRoleAssignment assignment = Assert.Single(page.RoleAssignments);
        Assert.Equal(second.Id, assignment.ParticipantId);
        Assert.Equal(second.FullName, assignment.FullName);
        Assert.Equal(
            1,
            AlbumPageRoleAssignmentService.Apply(
                [page],
                AlbumPageRoleCodes.CheckedBy,
                member: null));
        Assert.Empty(page.RoleAssignments);
    }

    [Fact]
    public void Resolver_UsesCurrentTeamProfileForStoredParticipantIdentity()
    {
        var assignment = new AlbumPageRoleAssignment
        {
            RoleCode = AlbumPageRoleCodes.Architect,
            ParticipantId = "architect-01",
            FamilyName = "Хуучин",
            GivenName = "Нэр",
            FullName = "Хуучин Нэр",
            Email = "architect@example.test",
        };
        ProjectParticipant[] participants =
        [
            new()
            {
                ParticipantId = "architect-01",
                FamilyName = "Энхбаатар",
                GivenName = "Мөнххолбоо",
                FullName = "Энхбаатар Мөнххолбоо",
                Email = "architect@example.test",
                Role = "Architect",
            },
        ];

        string? resolved = AlbumPageRoleAssignmentResolver.ResolveDocumentName(
            [assignment],
            AlbumPageRoleCodes.Architect,
            participants);

        Assert.Equal("Э.Мөнххолбоо", resolved);
    }

    [Fact]
    public void AlbumStore_RoundTripsSourceAndGeneratedPageRoleAssignments()
    {
        string path = Path.Combine(workDirectory, "page-roles.erksalbum");
        var album = new StudioAlbumDocument
        {
            Definition = new AlbumDefinition
            {
                Composition =
                [
                    new AlbumCompositionItem
                    {
                        Id = "drawing-list-and-notes",
                        RoleAssignments =
                        [
                            Assignment(AlbumPageRoleCodes.CheckedBy, "checker-01"),
                        ],
                    },
                ],
                Pages =
                [
                    new AlbumPageDefinition
                    {
                        SheetKey = "source|sheet-01",
                        RoleAssignments =
                        [
                            Assignment(AlbumPageRoleCodes.Architect, "architect-01"),
                        ],
                    },
                ],
            },
        };

        StudioAlbumDocumentStore.Save(album, path);
        StudioAlbumDocument loaded = StudioAlbumDocumentStore.Load(path);

        Assert.Equal(
            "architect-01",
            Assert.Single(loaded.Definition.Pages).RoleAssignments.Single().ParticipantId);
        Assert.Equal(
            "checker-01",
            Assert.Single(loaded.Definition.Composition).RoleAssignments.Single().ParticipantId);
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

    private static AlbumPageRoleAssignment Assignment(string roleCode, string participantId) => new()
    {
        RoleCode = roleCode,
        ParticipantId = participantId,
        FullName = participantId,
    };
}
