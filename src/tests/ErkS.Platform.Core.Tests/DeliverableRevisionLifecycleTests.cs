using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class DeliverableRevisionLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReleasedRevision_ContentCannotBeEditedOrMovedBackToDraft()
    {
        StudioAlbumDocument album = new();
        DeliverableRevisionRecord revision = DeliverableRevisionLifecycle.CreateDraft(
            album,
            Input('a'),
            Now);
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.ReadyForReview, Now.AddMinutes(1));
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.InReview, Now.AddMinutes(2));
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.Approved, Now.AddMinutes(3));
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.Released, Now.AddMinutes(4));

        Assert.Throws<InvalidOperationException>(() =>
            DeliverableRevisionLifecycle.UpdateDraft(album, revision.RevisionId, Input('b')));
        Assert.Throws<InvalidOperationException>(() =>
            DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.Draft, Now.AddMinutes(5)));
        Assert.Equal(new string('a', 64), revision.Sha256);
        Assert.Equal(Now.AddMinutes(4), revision.ReleasedAtUtc);
    }

    [Fact]
    public void ChangeAfterRelease_CreatesChildRevisionAndPreservesReleasedSnapshot()
    {
        StudioAlbumDocument album = new();
        DeliverableRevisionRecord released = DeliverableRevisionLifecycle.CreateDraft(album, Input('a'), Now);
        AdvanceToReleased(album, released, Now);

        DeliverableRevisionRecord successor = DeliverableRevisionLifecycle.CreateDraft(
            album,
            Input('b') with { FoundationVersion = 9, CompanySnapshotId = "company-v2" },
            Now.AddHours(1));

        Assert.Equal(2, successor.RevisionNumber);
        Assert.Equal(released.RevisionId, successor.ParentRevisionId);
        Assert.Equal(1, released.FoundationVersion);
        Assert.Equal("company-v1", released.CompanySnapshotId);
        Assert.Equal(DeliverableRevisionStatuses.Released, released.Status);
        Assert.Equal(9, successor.FoundationVersion);
        Assert.Equal("company-v2", successor.CompanySnapshotId);
    }

    [Fact]
    public void Archive_RequiresReleasedRevisionAndCapturesImmutableHash()
    {
        StudioAlbumDocument album = new();
        DeliverableRevisionRecord draft = DeliverableRevisionLifecycle.CreateDraft(album, Input('a'), Now);
        ProjectArchive archive = new();

        Assert.Throws<InvalidOperationException>(() =>
            DeliverableRevisionLifecycle.Archive(archive, album, draft.RevisionId, "Concept album", Now));

        AdvanceToReleased(album, draft, Now);
        ProjectArchiveRecord item = DeliverableRevisionLifecycle.Archive(
            archive,
            album,
            draft.RevisionId,
            "Concept album",
            Now.AddHours(1));

        Assert.Equal(draft.RevisionId, item.RevisionId);
        Assert.Equal(draft.Sha256, item.Sha256);
        Assert.Equal(DeliverableRevisionStatuses.Archived, item.Status);
        Assert.Equal(draft.FoundationVersion, item.FoundationVersion);
        Assert.Equal(draft.CompanySnapshotId, item.CompanySnapshotId);
    }

    [Fact]
    public void RebuildSameDraft_IsIdempotent()
    {
        StudioAlbumDocument album = new();
        DeliverableRevisionRecord first = DeliverableRevisionLifecycle.CreateDraft(album, Input('a'), Now);

        DeliverableRevisionRecord retry = DeliverableRevisionLifecycle.CreateDraft(album, Input('a'), Now.AddMinutes(1));

        Assert.Same(first, retry);
        Assert.Single(album.Revisions);
    }

    private static DeliverableRevisionInput Input(char hashCharacter) => new()
    {
        PdfPath = "albums/concept.pdf",
        Sha256 = new string(hashCharacter, 64),
        SourcePackageIds = ["source-package-1"],
        FoundationVersion = 1,
        CompanySnapshotId = "company-v1",
        PageCount = 3,
        PageSizeSummary = "A3 landscape",
        CreatedBy = "architect@erks.local",
        AuditNote = "Studio canonical build",
    };

    private static void AdvanceToReleased(
        StudioAlbumDocument album,
        DeliverableRevisionRecord revision,
        DateTimeOffset timestamp)
    {
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.ReadyForReview, timestamp.AddMinutes(1));
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.InReview, timestamp.AddMinutes(2));
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.Approved, timestamp.AddMinutes(3));
        DeliverableRevisionLifecycle.Transition(album, revision.RevisionId, DeliverableRevisionStatuses.Released, timestamp.AddMinutes(4));
    }
}
