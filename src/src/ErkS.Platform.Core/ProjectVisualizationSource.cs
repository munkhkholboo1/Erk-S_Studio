using System.Globalization;

namespace ErkS.Platform.Core;

/// <summary>
/// Studio-owned image source used to compose the concept album's
/// "Харагдах байдал" pages. The original image aspect ratio is immutable.
/// </summary>
public sealed class ProjectVisualizationSource
{
    /// <summary>Local workspace identity that owns this source.</summary>
    public string OwnerProjectId { get; set; } = "";

    /// <summary>False for the untouched placeholder on a newly created project.</summary>
    public bool IsConfigured { get; set; }

    public string Title { get; set; } = VisualizationPageLayoutPlanner.PageTitle;

    public int ImagesPerPage { get; set; } = VisualizationPageLayoutPlanner.DefaultImagesPerPage;

    public List<ProjectVisualizationImage> Images { get; set; } = [];

    public void Normalize() => Normalize(null);

    public void Normalize(string? projectId)
    {
        Title = string.IsNullOrWhiteSpace(Title)
            ? VisualizationPageLayoutPlanner.PageTitle
            : Title.Trim();
        ImagesPerPage = Math.Clamp(
            ImagesPerPage <= 0 ? VisualizationPageLayoutPlanner.DefaultImagesPerPage : ImagesPerPage,
            1,
            VisualizationPageLayoutPlanner.MaximumImagesPerPage);
        Images ??= [];
        string normalizedProjectId = projectId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(normalizedProjectId) &&
            string.IsNullOrWhiteSpace(OwnerProjectId) &&
            (IsConfigured || Images.Count > 0))
        {
            OwnerProjectId = normalizedProjectId;
        }
        else
        {
            OwnerProjectId = OwnerProjectId?.Trim() ?? "";
        }

        bool sourceBelongsToProject = string.IsNullOrWhiteSpace(normalizedProjectId) ||
            string.IsNullOrWhiteSpace(OwnerProjectId) ||
            OwnerProjectId.Equals(normalizedProjectId, StringComparison.OrdinalIgnoreCase);
        foreach (ProjectVisualizationImage image in Images)
        {
            if (string.IsNullOrWhiteSpace(image.Id))
                image.Id = Guid.NewGuid().ToString("N");
            image.OwnerProjectId = image.OwnerProjectId?.Trim() ?? "";
            image.LinkedSourcePath = image.LinkedSourcePath?.Trim() ?? "";
            image.Version = Math.Max(1, image.Version);
            if (sourceBelongsToProject &&
                !string.IsNullOrWhiteSpace(normalizedProjectId) &&
                string.IsNullOrWhiteSpace(image.OwnerProjectId))
            {
                image.OwnerProjectId = normalizedProjectId;
            }
            image.FocalPointX = double.IsFinite(image.FocalPointX)
                ? Math.Clamp(image.FocalPointX, 0d, 1d)
                : 0.5d;
            image.FocalPointY = double.IsFinite(image.FocalPointY)
                ? Math.Clamp(image.FocalPointY, 0d, 1d)
                : 0.5d;
        }

        if (sourceBelongsToProject && ImagesForProject(normalizedProjectId).Count > 0)
            IsConfigured = true;
    }

    public void ConfigureForProject(string projectId)
    {
        string normalizedProjectId = RequireProjectId(projectId);
        if (!string.IsNullOrWhiteSpace(OwnerProjectId) &&
            !OwnerProjectId.Equals(normalizedProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Visualization source belongs to project '{OwnerProjectId}', not '{normalizedProjectId}'.");
        }

        OwnerProjectId = normalizedProjectId;
        IsConfigured = true;
        Normalize(normalizedProjectId);
    }

    public bool IsConfiguredForProject(string projectId)
    {
        string normalizedProjectId = RequireProjectId(projectId);
        bool sourceMatches = string.Equals(
            OwnerProjectId,
            normalizedProjectId,
            StringComparison.OrdinalIgnoreCase);
        return sourceMatches && (IsConfigured || ImagesForProject(normalizedProjectId).Count > 0);
    }

    public IReadOnlyList<ProjectVisualizationImage> ImagesForProject(string? projectId)
    {
        string normalizedProjectId = projectId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedProjectId))
            return Images;
        if (!string.Equals(
                OwnerProjectId,
                normalizedProjectId,
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return Images
            .Where(image => string.Equals(
                image.OwnerProjectId,
                normalizedProjectId,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public ProjectVisualizationSource CreateProjectSnapshot(string projectId)
    {
        string normalizedProjectId = RequireProjectId(projectId);
        IReadOnlyList<ProjectVisualizationImage> images = ImagesForProject(normalizedProjectId);
        bool configured = IsConfiguredForProject(normalizedProjectId);
        return new ProjectVisualizationSource
        {
            OwnerProjectId = normalizedProjectId,
            IsConfigured = configured,
            Title = Title,
            ImagesPerPage = ImagesPerPage,
            Images = configured
                ? images.Select(image => image.Clone()).ToList()
                : [],
        };
    }

    private static string RequireProjectId(string? projectId)
    {
        string normalized = projectId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Project ID is required.", nameof(projectId));
        return normalized;
    }
}

public sealed class ProjectVisualizationImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerProjectId { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    /// <summary>Local image selected by the user and watched for revisions.</summary>
    public string LinkedSourcePath { get; set; } = "";
    public DateTimeOffset? LinkedSourceLastWriteTimeUtc { get; set; }
    public bool IsAvailable { get; set; } = true;
    /// <summary>
    /// Controls album composition without deleting the source reference.
    /// Missing values in older project files remain included by default.
    /// </summary>
    public bool IsIncludedInAlbum { get; set; } = true;
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public string Sha256 { get; set; } = "";
    public int Version { get; set; } = 1;
    public double FocalPointX { get; set; } = 0.5d;
    public double FocalPointY { get; set; } = 0.5d;
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public double AspectRatio => PixelWidth > 0 && PixelHeight > 0
        ? PixelWidth / (double)PixelHeight
        : 1d;

    public ProjectVisualizationImage Clone() => new()
    {
        Id = Id,
        OwnerProjectId = OwnerProjectId,
        RelativePath = RelativePath,
        OriginalFileName = OriginalFileName,
        LinkedSourcePath = LinkedSourcePath,
        LinkedSourceLastWriteTimeUtc = LinkedSourceLastWriteTimeUtc,
        IsAvailable = IsAvailable,
        IsIncludedInAlbum = IsIncludedInAlbum,
        ContentType = ContentType,
        SizeBytes = SizeBytes,
        PixelWidth = PixelWidth,
        PixelHeight = PixelHeight,
        Sha256 = Sha256,
        Version = Version,
        FocalPointX = FocalPointX,
        FocalPointY = FocalPointY,
        AddedAtUtc = AddedAtUtc,
    };
}

public enum VisualizationImageFitMode
{
    Contain,
    CenterCrop,
}

public sealed class VisualizationImageTilePlan
{
    public required ProjectVisualizationImage Image { get; init; }
    public required PageRectMm Frame { get; init; }
    public required VisualizationImageFitMode FitMode { get; init; }
    public required double CropFraction { get; init; }
}

public sealed class VisualizationAlbumPagePlan
{
    public required int PageIndex { get; init; }
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<VisualizationImageTilePlan> Tiles { get; init; }

    public string NavigationKey =>
        $"visualizations:{PageIndex.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Chooses a page composition by comparing image and tile aspect ratios.
/// Small centered crops are allowed; larger losses automatically use contain.
/// </summary>
public static class VisualizationPageLayoutPlanner
{
    public const string PageTitle = "ХАРАГДАХ БАЙДАЛ";
    public const int DefaultImagesPerPage = 4;
    public const int MaximumImagesPerPage = 6;
    public const double MaximumCropFraction = 0.12d;
    public const double TileGapMm = 5d;

    public static PageRectMm ContentArea => new()
    {
        X = 17.5d,
        Y = 16.5d,
        Width = 395d,
        Height = 245d,
    };

    public static IReadOnlyList<VisualizationAlbumPagePlan> Create(
        ProjectVisualizationSource? source,
        int firstPageNumber)
    {
        if (source is null)
            return [];

        source.Normalize();
        List<ProjectVisualizationImage> availableImages = source.Images
            .Where(image => image.IsAvailable && image.IsIncludedInAlbum)
            .ToList();
        if (availableImages.Count == 0)
            return [];

        int imagesPerPage = Math.Clamp(source.ImagesPerPage, 1, MaximumImagesPerPage);
        int pageCount = (int)Math.Ceiling(availableImages.Count / (double)imagesPerPage);
        int numberWidth = Math.Max(
            2,
            Math.Max(0, firstPageNumber + pageCount - 1)
                .ToString(CultureInfo.InvariantCulture)
                .Length);
        var result = new List<VisualizationAlbumPagePlan>(pageCount);

        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            List<ProjectVisualizationImage> images = availableImages
                .Skip(pageIndex * imagesPerPage)
                .Take(imagesPerPage)
                .ToList();
            IReadOnlyList<NormalizedRect> layout = SelectLayout(images);
            var tiles = images.Select((image, index) => CreateTile(image, layout[index])).ToList();
            result.Add(new VisualizationAlbumPagePlan
            {
                PageIndex = pageIndex,
                Number = (firstPageNumber + pageIndex).ToString($"D{numberWidth}", CultureInfo.InvariantCulture),
                Title = string.IsNullOrWhiteSpace(source.Title) ? PageTitle : source.Title,
                Tiles = tiles,
            });
        }

        return result;
    }

    /// <summary>
    /// Production entry point. A visualization source is publishable only when
    /// both its manifest and every image explicitly belong to this project.
    /// </summary>
    public static IReadOnlyList<VisualizationAlbumPagePlan> Create(
        ProjectVisualizationSource? source,
        string projectId,
        int firstPageNumber)
    {
        if (source is null)
            return [];

        ProjectVisualizationSource projectSource = source.CreateProjectSnapshot(projectId);
        return Create(projectSource, firstPageNumber);
    }

    private static VisualizationImageTilePlan CreateTile(
        ProjectVisualizationImage image,
        NormalizedRect normalized)
    {
        PageRectMm area = ContentArea;
        var frame = new PageRectMm
        {
            X = area.X + normalized.X * area.Width + TileGapMm * 0.5d,
            Y = area.Y + normalized.Y * area.Height + TileGapMm * 0.5d,
            Width = Math.Max(1d, normalized.Width * area.Width - TileGapMm),
            Height = Math.Max(1d, normalized.Height * area.Height - TileGapMm),
        };
        double sourceRatio = Math.Max(0.01d, image.AspectRatio);
        double tileRatio = Math.Max(0.01d, frame.Width / frame.Height);
        double visibleFraction = Math.Min(sourceRatio / tileRatio, tileRatio / sourceRatio);
        double cropFraction = Math.Clamp(1d - visibleFraction, 0d, 1d);
        return new VisualizationImageTilePlan
        {
            Image = image,
            Frame = frame,
            CropFraction = cropFraction,
            FitMode = cropFraction <= MaximumCropFraction
                ? VisualizationImageFitMode.CenterCrop
                : VisualizationImageFitMode.Contain,
        };
    }

    private static IReadOnlyList<NormalizedRect> SelectLayout(
        IReadOnlyList<ProjectVisualizationImage> images)
    {
        IReadOnlyList<IReadOnlyList<NormalizedRect>> candidates = LayoutCandidates(images.Count);
        return candidates
            .OrderBy(layout => LayoutScore(images, layout))
            .First();
    }

    private static double LayoutScore(
        IReadOnlyList<ProjectVisualizationImage> images,
        IReadOnlyList<NormalizedRect> layout)
    {
        PageRectMm area = ContentArea;
        double pageRatio = area.Width / area.Height;
        double score = 0d;
        for (int index = 0; index < images.Count; index++)
        {
            double sourceRatio = Math.Max(0.01d, images[index].AspectRatio);
            double tileRatio = Math.Max(
                0.01d,
                layout[index].Width * pageRatio / layout[index].Height);
            score += Math.Abs(Math.Log(sourceRatio / tileRatio));
            bool orientationMismatch = (sourceRatio >= 1d) != (tileRatio >= 1d);
            if (orientationMismatch)
                score += 0.35d;
        }
        return score;
    }

    private static IReadOnlyList<IReadOnlyList<NormalizedRect>> LayoutCandidates(int count) => count switch
    {
        1 => [Grid(1, 1, 1)],
        2 => [Grid(2, 1, 2), Grid(1, 2, 2)],
        3 =>
        [
            [Rect(0, 0, 0.58, 1), Rect(0.58, 0, 0.42, 0.5), Rect(0.58, 0.5, 0.42, 0.5)],
            [Rect(0, 0, 0.42, 0.5), Rect(0, 0.5, 0.42, 0.5), Rect(0.42, 0, 0.58, 1)],
            [Rect(0, 0, 1, 0.58), Rect(0, 0.58, 0.5, 0.42), Rect(0.5, 0.58, 0.5, 0.42)],
            [Rect(0, 0, 0.5, 0.42), Rect(0.5, 0, 0.5, 0.42), Rect(0, 0.42, 1, 0.58)],
        ],
        4 => [Grid(2, 2, 4), Grid(4, 1, 4), Grid(1, 4, 4)],
        5 =>
        [
            [Rect(0, 0, 0.55, 1), Rect(0.55, 0, 0.225, 0.5), Rect(0.775, 0, 0.225, 0.5), Rect(0.55, 0.5, 0.225, 0.5), Rect(0.775, 0.5, 0.225, 0.5)],
            [Rect(0, 0, 0.225, 0.5), Rect(0.225, 0, 0.225, 0.5), Rect(0, 0.5, 0.225, 0.5), Rect(0.225, 0.5, 0.225, 0.5), Rect(0.45, 0, 0.55, 1)],
            [Rect(0, 0, 1, 0.55), Rect(0, 0.55, 0.25, 0.45), Rect(0.25, 0.55, 0.25, 0.45), Rect(0.5, 0.55, 0.25, 0.45), Rect(0.75, 0.55, 0.25, 0.45)],
        ],
        _ => [Grid(3, 2, count), Grid(2, 3, count)],
    };

    private static IReadOnlyList<NormalizedRect> Grid(int columns, int rows, int count)
    {
        var result = new List<NormalizedRect>(count);
        for (int index = 0; index < count; index++)
        {
            int row = index / columns;
            int column = index % columns;
            result.Add(Rect(
                column / (double)columns,
                row / (double)rows,
                1d / columns,
                1d / rows));
        }
        return result;
    }

    private static NormalizedRect Rect(double x, double y, double width, double height) =>
        new(x, y, width, height);

    private readonly record struct NormalizedRect(
        double X,
        double Y,
        double Width,
        double Height);
}
