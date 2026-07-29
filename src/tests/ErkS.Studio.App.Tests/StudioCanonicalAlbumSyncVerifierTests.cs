using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCanonicalAlbumSyncVerifierTests
{
    [Fact]
    public void Verify_AcceptsOnlyTheExactCurrentCompleteRevision()
    {
        StudioCloudAlbumRevision expected = Revision(
            "revision-2",
            "abcdef",
            [
                Section("source:a", 1),
                Section("source:b", 2),
            ]);
        var album = new StudioCloudAlbum
        {
            AlbumId = "album",
            CurrentRevisionId = expected.RevisionId,
            Revisions = [Revision("revision-1", "old", [Section("legacy", 1)]), expected],
        };

        StudioCloudAlbumRevision actual =
            StudioCanonicalAlbumSyncVerifier.Verify(
                [album],
                "album",
                "revision-2",
                "ABCDEF");

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Verify_RejectsARevisionThatIsNoLongerCurrent()
    {
        var album = new StudioCloudAlbum
        {
            AlbumId = "album",
            CurrentRevisionId = "revision-3",
            Revisions =
            [
                Revision("revision-2", "hash-2", [Section("source:a", 1)]),
                Revision("revision-3", "hash-3", [Section("source:b", 1)]),
            ],
        };

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            StudioCanonicalAlbumSyncVerifier.Verify(
                [album],
                "album",
                "revision-2",
                "hash-2"));

        Assert.Contains("different revision", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsAnIncompleteManifestEvenWhenRevisionAndHashMatch()
    {
        StudioCloudAlbumRevision revision = Revision(
            "revision-2",
            "hash-2",
            [Section("source:a", 2)]);
        var album = new StudioCloudAlbum
        {
            AlbumId = "album",
            CurrentRevisionId = revision.RevisionId,
            Revisions = [revision],
        };

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            StudioCanonicalAlbumSyncVerifier.Verify(
                [album],
                "album",
                "revision-2",
                "hash-2"));

        Assert.Contains("manifest", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RejectsAHashMismatch()
    {
        StudioCloudAlbumRevision revision = Revision(
            "revision-2",
            "server-hash",
            [Section("source:a", 1)]);
        var album = new StudioCloudAlbum
        {
            AlbumId = "album",
            CurrentRevisionId = revision.RevisionId,
            Revisions = [revision],
        };

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            StudioCanonicalAlbumSyncVerifier.Verify(
                [album],
                "album",
                "revision-2",
                "local-hash"));

        Assert.Contains("hash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static StudioCloudAlbumRevision Revision(
        string revisionId,
        string hash,
        List<StudioCloudAlbumSection> sections) => new()
    {
        RevisionId = revisionId,
        PdfSha256 = hash,
        PageCount = sections.SelectMany(section => section.PageNumbers).DefaultIfEmpty().Max(),
        SectionManifest = sections,
    };

    private static StudioCloudAlbumSection Section(string code, params int[] pages) => new()
    {
        Code = code,
        Label = code,
        Order = pages.FirstOrDefault(),
        PageNumbers = pages,
        Status = "Available",
        ComponentKind = "Generated",
    };
}
