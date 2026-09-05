using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// From 2026-09-05 a source can be owned by a bot SEAT rather than a person,
/// and SRV empties registeredBy and custodianEmail on those rows deliberately -
/// a botId in an email field would be the single-field-two-meanings defect this
/// codebase keeps paying for.
///
/// The consequence measured on this side: every ownership question was answered
/// by walking a chain of person-shaped fields, so a bot-owned row answered ""
/// everywhere, and "" already meant "nobody owns this" - which the gates read as
/// permission. The client would have told a person they controlled a seat's
/// work; the server would then have refused, so the lie was silent.
/// </summary>
public sealed class ProjectSourceOwnershipTests
{
    [Theory]
    [InlineData("Bot", ProjectSourceOwnerKinds.Bot)]
    [InlineData("bot", ProjectSourceOwnerKinds.Bot)]
    [InlineData("Person", ProjectSourceOwnerKinds.Person)]
    [InlineData("", ProjectSourceOwnerKinds.Person)]
    [InlineData(null, ProjectSourceOwnerKinds.Person)]
    public void AKnownKindIsRead_AndEmptyMeansPerson(string? wire, string expected)
    {
        // Empty is person-owned by SRV's stated rule: the row predates seats and
        // the server resolves it from registeredBy without ever rewriting it.
        // Both sides must read it the same way or one of them invents an owner.
        Assert.Equal(expected, ProjectSourceOwnerKinds.Recognize(wire));
    }

    [Theory]
    [InlineData("Team")]
    [InlineData("Organization")]
    [InlineData("Seat")]
    public void AKindThisBuildDoesNotKnowIsUnknown_NotPerson(string wire)
    {
        // The distinction that costs the most to get wrong: ABSENT is a decided
        // answer, UNRECOGNISED is a newer server talking. Collapsing them would
        // hand a seat's source to whichever person the row happens to name.
        Assert.Equal(ProjectSourceOwnerKinds.Unknown, ProjectSourceOwnerKinds.Recognize(wire));
    }

    [Fact]
    public void ABotOwnedSourceNeverResolvesToAPerson_EvenWhenAnEmailIsReachable()
    {
        // The row a real server sends has both person fields empty. This one
        // does not, on purpose: if a stale mirror, a migration or a bug leaves
        // an email behind, the seat must still be the owner. "Prefer the email
        // when one is present" is the fallback this class exists to forbid.
        var source = new ProjectCloudSourceReference
        {
            SourceKey = "arch-model",
            SourceOwnerKind = "Bot",
            SourceOwnerRef = "bot_7f3a91c4e85b4d2f",
            RegisteredBy = "someone@example.com",
            CustodianEmail = "someone-else@example.com",
            OwnerEmail = "third@example.com",
        };

        ProjectSourceOwner owner = ProjectSourceOwnership.Of(source);

        Assert.True(owner.IsBotOwned);
        Assert.Equal("bot_7f3a91c4e85b4d2f", owner.Reference);
        Assert.Equal("", owner.ControllingPersonEmail);
        Assert.Equal("", ProjectSourceOwnership.ControllingPersonEmail(source));
    }

    [Fact]
    public void ARowWrittenBeforeSeatsExistedIsStillOwnedByItsRegistrant()
    {
        // Every source on disk today is one of these. If this went to "nobody"
        // the fix would have broken every existing project to protect a feature
        // that has never been used.
        var source = new ProjectCloudSourceReference
        {
            SourceKey = "arch-model",
            RegisteredBy = "Owner@Example.com",
        };

        ProjectSourceOwner owner = ProjectSourceOwnership.Of(source);

        Assert.True(owner.IsPersonOwned);
        Assert.Equal("owner@example.com", owner.ControllingPersonEmail);
    }

    [Fact]
    public void CustodyStillMovesControlBetweenPeople()
    {
        var source = new ProjectCloudSourceReference
        {
            SourceKey = "arch-model",
            RegisteredBy = "owner@example.com",
            CustodianEmail = "custodian@example.com",
        };

        Assert.Equal(
            "custodian@example.com",
            ProjectSourceOwnership.ControllingPersonEmail(source));
    }

    [Fact]
    public void TwoSEATSAreTwoOwners_NotOneEmptyBucket()
    {
        // The grouping key that decides whether two rows are the same stream.
        // Keyed on registeredBy, every bot-owned row shared one empty bucket -
        // and the caller keeps a single survivor per bucket.
        var first = new ProjectCloudSourceReference
        {
            SourceOwnerKind = "Bot",
            SourceOwnerRef = "bot_aaa",
        };
        var second = new ProjectCloudSourceReference
        {
            SourceOwnerKind = "Bot",
            SourceOwnerRef = "bot_bbb",
        };

        Assert.NotEqual(
            ProjectSourceOwnership.OwnerKey(first),
            ProjectSourceOwnership.OwnerKey(second));
    }

    [Fact]
    public void ASeatAndAPersonAreNeverTheSameOwner_EvenWithMatchingText()
    {
        // A botId that happened to read like an email must not collide with the
        // person of that name. The kind is part of the key for that reason.
        var seat = new ProjectCloudSourceReference
        {
            SourceOwnerKind = "Bot",
            SourceOwnerRef = "shared@example.com",
        };
        var person = new ProjectCloudSourceReference
        {
            SourceOwnerKind = "Person",
            SourceOwnerRef = "shared@example.com",
        };

        Assert.NotEqual(
            ProjectSourceOwnership.OwnerKey(seat),
            ProjectSourceOwnership.OwnerKey(person));
    }

    [Fact]
    public void APersonIsRefusedOnASeatsSource_RatherThanWavedThrough()
    {
        // THE GATE. It compared emails and skipped the refusal when the owner
        // was empty, which was right while empty meant "no registry row". On a
        // bot-owned row it meant "a seat owns this", and the refusal was skipped.
        ProjectWorkspace project = CloudProject();
        var source = new ProjectDesignSource { Id = "source-a" };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "stream-a");
        project.Sources.Add(source);
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceKey = "stream-a",
            SourceOwnerKind = "Bot",
            SourceOwnerRef = "bot_7f3a91c4e85b4d2f",
            RegisteredBy = "",
            CustodianEmail = "",
            Status = "Active",
        });

        ProjectSourceEditAuthority decision = ProjectCloudSyncAuthority.ResolveSource(
            project,
            source,
            "anyone@example.com");

        Assert.False(decision.CanEdit);
        // The refusal must say a SEAT owns it. "No owner, so go ahead" and
        // "somebody else owns it" are different sentences to the reader.
        Assert.Contains("бот суудал", decision.Message);
        // And it must not name a person as the owner of a seat's source.
        Assert.Equal("", decision.OwnerEmail);
    }

    [Fact]
    public void AnUnreadableOwnerKindIsRefused_NotGuessed()
    {
        ProjectWorkspace project = CloudProject();
        var source = new ProjectDesignSource { Id = "source-a" };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "stream-a");
        project.Sources.Add(source);
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceKey = "stream-a",
            SourceOwnerKind = "Cooperative",
            SourceOwnerRef = "coop-1",
            Status = "Active",
        });

        ProjectSourceEditAuthority decision = ProjectCloudSyncAuthority.ResolveSource(
            project,
            source,
            "anyone@example.com");

        Assert.False(decision.CanEdit);
    }

    [Fact]
    public void APersonOwnedSourceStillBehavesExactlyAsBefore()
    {
        // The change must not narrow anything for the 24 projects on disk.
        ProjectWorkspace project = CloudProject();
        var source = new ProjectDesignSource { Id = "source-a" };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "stream-a");
        project.Sources.Add(source);
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceKey = "stream-a",
            RegisteredBy = "owner@example.com",
            Status = "Active",
        });

        Assert.True(ProjectCloudSyncAuthority.ResolveSource(
            project,
            source,
            "owner@example.com").CanEdit);
        Assert.False(ProjectCloudSyncAuthority.ResolveSource(
            project,
            source,
            "other@example.com").CanEdit);
    }

    private static ProjectWorkspace CloudProject()
    {
        var project = new ProjectWorkspace();
        project.Identity.Name = "Test";
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "project-1";
        return project;
    }
}
