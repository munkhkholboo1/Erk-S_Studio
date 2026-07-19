namespace ErkS.Platform.Core;

public static class DeliverableRevisionStatuses
{
    public const string Draft = "Draft";
    public const string ReadyForReview = "ReadyForReview";
    public const string InReview = "InReview";
    public const string ChangesRequested = "ChangesRequested";
    public const string Approved = "Approved";
    public const string Released = "Released";
    public const string Superseded = "Superseded";
    public const string Archived = "Archived";
}

public sealed record DeliverableRevisionInput
{
    public string PdfPath { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public IReadOnlyList<string> SourcePackageIds { get; init; } = [];
    public int FoundationVersion { get; init; } = 1;
    public string CompanySnapshotId { get; init; } = "";
    public int PageCount { get; init; }
    public string PageSizeSummary { get; init; } = "";
    public string CreatedBy { get; init; } = "";
    public string AuditNote { get; init; } = "";
}

public static class DeliverableRevisionLifecycle
{
    public static DeliverableRevisionRecord CreateDraft(
        StudioAlbumDocument album,
        DeliverableRevisionInput input,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(album);
        Validate(input);
        album.Revisions ??= [];
        DeliverableRevisionRecord? parent = album.Revisions
            .OrderByDescending(item => item.RevisionNumber > 0 ? item.RevisionNumber : item.Version)
            .FirstOrDefault();
        if (parent is not null && IsSameSnapshot(parent, input) &&
            parent.Status.Equals(DeliverableRevisionStatuses.Draft, StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }

        int number = album.Revisions.Count == 0
            ? 1
            : album.Revisions.Max(item => Math.Max(item.RevisionNumber, item.Version)) + 1;
        DeliverableRevisionRecord revision = new()
        {
            RevisionId = Guid.NewGuid().ToString("N"),
            RevisionNumber = number,
            Version = number,
            ParentRevisionId = parent?.RevisionId ?? "",
            Status = DeliverableRevisionStatuses.Draft,
            ReviewStatus = DeliverableRevisionStatuses.Draft,
            FoundationVersion = input.FoundationVersion,
            CompanySnapshotId = input.CompanySnapshotId.Trim(),
            SourcePackageIds = NormalizeIds(input.SourcePackageIds),
            PdfPath = input.PdfPath.Trim(),
            Sha256 = input.Sha256.Trim().ToLowerInvariant(),
            PageCount = input.PageCount,
            PageSizeSummary = input.PageSizeSummary.Trim(),
            CreatedBy = input.CreatedBy.Trim(),
            CreatedAtUtc = createdAtUtc,
            AuditNote = input.AuditNote.Trim(),
        };

        if (parent is not null &&
            (parent.Status.Equals(DeliverableRevisionStatuses.Draft, StringComparison.OrdinalIgnoreCase) ||
             parent.Status.Equals(DeliverableRevisionStatuses.ChangesRequested, StringComparison.OrdinalIgnoreCase)))
        {
            parent.Status = DeliverableRevisionStatuses.Superseded;
            parent.ReviewStatus = DeliverableRevisionStatuses.Superseded;
            parent.SupersededByRevisionId = revision.RevisionId;
        }

        album.Revisions.Add(revision);
        album.Status = DeliverableRevisionStatuses.Draft;
        return revision;
    }

    public static void UpdateDraft(
        StudioAlbumDocument album,
        string revisionId,
        DeliverableRevisionInput input)
    {
        DeliverableRevisionRecord revision = Find(album, revisionId);
        if (!revision.Status.Equals(DeliverableRevisionStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a Draft revision can be edited. Create a new revision instead.");
        Validate(input);
        revision.FoundationVersion = input.FoundationVersion;
        revision.CompanySnapshotId = input.CompanySnapshotId.Trim();
        revision.SourcePackageIds = NormalizeIds(input.SourcePackageIds);
        revision.PdfPath = input.PdfPath.Trim();
        revision.Sha256 = input.Sha256.Trim().ToLowerInvariant();
        revision.PageCount = input.PageCount;
        revision.PageSizeSummary = input.PageSizeSummary.Trim();
        revision.AuditNote = input.AuditNote.Trim();
    }

    public static DeliverableRevisionRecord Transition(
        StudioAlbumDocument album,
        string revisionId,
        string targetStatus,
        DateTimeOffset changedAtUtc,
        string actor = "",
        string note = "")
    {
        DeliverableRevisionRecord revision = Find(album, revisionId);
        string target = NormalizeStatus(targetStatus);
        if (!AllowedTransitions(revision.Status).Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Deliverable revision cannot move from '{revision.Status}' to '{target}'.");
        }

        revision.Status = target;
        revision.ReviewStatus = target;
        album.Status = target;
        if (target.Equals(DeliverableRevisionStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            revision.ApprovalRecord = new DeliverableApprovalRecord
            {
                Status = DeliverableRevisionStatuses.Approved,
                ApprovedBy = actor.Trim(),
                ApprovedAtUtc = changedAtUtc,
                Note = note.Trim(),
            };
        }
        if (target.Equals(DeliverableRevisionStatuses.Released, StringComparison.OrdinalIgnoreCase))
            revision.ReleasedAtUtc = changedAtUtc;
        return revision;
    }

    public static ProjectArchiveRecord Archive(
        ProjectArchive archive,
        StudioAlbumDocument album,
        string revisionId,
        string title,
        DateTimeOffset archivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(archive);
        archive.Items ??= [];
        DeliverableRevisionRecord revision = Find(album, revisionId);
        if (!revision.Status.Equals(DeliverableRevisionStatuses.Released, StringComparison.OrdinalIgnoreCase) &&
            !revision.Status.Equals(DeliverableRevisionStatuses.Superseded, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only a Released or Superseded revision can be archived.");
        }

        ProjectArchiveRecord? existing = archive.Items.FirstOrDefault(item =>
            item.RevisionId.Equals(revision.RevisionId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        ProjectArchiveRecord item = new()
        {
            DeliverableId = album.AlbumId,
            Type = album.PackageType,
            Title = title.Trim(),
            Status = DeliverableRevisionStatuses.Archived,
            RevisionId = revision.RevisionId,
            RevisionNumber = revision.RevisionNumber,
            FoundationVersion = revision.FoundationVersion,
            CompanySnapshotId = revision.CompanySnapshotId,
            PageCount = revision.PageCount,
            PageSizeSummary = revision.PageSizeSummary,
            CreatedBy = revision.CreatedBy,
            AuditNote = revision.AuditNote,
            RelativePath = revision.PdfPath,
            Sha256 = revision.Sha256,
            ArchivedAtUtc = archivedAtUtc,
        };
        archive.Items.Add(item);
        return item;
    }

    private static DeliverableRevisionRecord Find(StudioAlbumDocument album, string revisionId)
    {
        ArgumentNullException.ThrowIfNull(album);
        return album.Revisions.SingleOrDefault(item =>
                   item.RevisionId.Equals(revisionId?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Deliverable revision was not found.");
    }

    private static IReadOnlyList<string> AllowedTransitions(string status) => status switch
    {
        DeliverableRevisionStatuses.Draft => [DeliverableRevisionStatuses.ReadyForReview, DeliverableRevisionStatuses.Superseded],
        DeliverableRevisionStatuses.ReadyForReview => [DeliverableRevisionStatuses.InReview, DeliverableRevisionStatuses.ChangesRequested],
        DeliverableRevisionStatuses.InReview => [DeliverableRevisionStatuses.ChangesRequested, DeliverableRevisionStatuses.Approved],
        DeliverableRevisionStatuses.ChangesRequested => [DeliverableRevisionStatuses.Superseded],
        DeliverableRevisionStatuses.Approved => [DeliverableRevisionStatuses.Released],
        DeliverableRevisionStatuses.Released => [DeliverableRevisionStatuses.Superseded, DeliverableRevisionStatuses.Archived],
        DeliverableRevisionStatuses.Superseded => [DeliverableRevisionStatuses.Archived],
        _ => [],
    };

    private static string NormalizeStatus(string status)
    {
        string value = status?.Trim() ?? "";
        string[] known =
        [
            DeliverableRevisionStatuses.Draft,
            DeliverableRevisionStatuses.ReadyForReview,
            DeliverableRevisionStatuses.InReview,
            DeliverableRevisionStatuses.ChangesRequested,
            DeliverableRevisionStatuses.Approved,
            DeliverableRevisionStatuses.Released,
            DeliverableRevisionStatuses.Superseded,
            DeliverableRevisionStatuses.Archived,
        ];
        return known.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown deliverable revision status '{value}'.");
    }

    private static void Validate(DeliverableRevisionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.PdfPath))
            throw new InvalidOperationException("Revision PDF path is required.");
        string hash = input.Sha256?.Trim() ?? "";
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Revision PDF SHA-256 is invalid.");
        if (input.FoundationVersion < 1)
            throw new InvalidOperationException("Revision foundation version must be positive.");
        if (input.PageCount < 1)
            throw new InvalidOperationException("Revision page count must be positive.");
    }

    private static bool IsSameSnapshot(DeliverableRevisionRecord revision, DeliverableRevisionInput input) =>
        revision.Sha256.Equals(input.Sha256?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        revision.FoundationVersion == input.FoundationVersion &&
        revision.CompanySnapshotId.Equals(input.CompanySnapshotId?.Trim(), StringComparison.Ordinal) &&
        revision.SourcePackageIds.SequenceEqual(NormalizeIds(input.SourcePackageIds), StringComparer.OrdinalIgnoreCase);

    private static List<string> NormalizeIds(IEnumerable<string>? values) => (values ?? [])
        .Select(value => value?.Trim() ?? "")
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
