namespace ErkS.Platform.Core;

/// <summary>
/// Project-owned map extents used by the Studio-generated site context page.
/// Only compact rendered snapshots are stored with the project; map tiles are
/// never treated as native project source files.
/// </summary>
public sealed class ProjectSiteContextMap
{
    public string OwnerProjectId { get; set; } = "";

    public ProjectMapViewport LocationScheme { get; set; } =
        ProjectMapViewport.CreateLocationScheme();

    public ProjectMapViewport SurroundingsOverview { get; set; } =
        ProjectMapViewport.CreateSurroundingsOverview();

    public ProjectSiteBoundary Boundary { get; set; } = new();

    public ProjectSitePlanFeatures PlanFeatures { get; set; } = new();

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public void Normalize(string? projectId = null)
    {
        string normalizedProjectId = projectId?.Trim() ?? "";
        OwnerProjectId = OwnerProjectId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(normalizedProjectId) &&
            string.IsNullOrWhiteSpace(OwnerProjectId))
        {
            OwnerProjectId = normalizedProjectId;
        }

        LocationScheme ??= ProjectMapViewport.CreateLocationScheme();
        SurroundingsOverview ??= ProjectMapViewport.CreateSurroundingsOverview();
        Boundary ??= new ProjectSiteBoundary();
        PlanFeatures ??= new ProjectSitePlanFeatures();
        LocationScheme.Normalize(ProjectMapViewportKinds.LocationScheme);
        SurroundingsOverview.Normalize(ProjectMapViewportKinds.SurroundingsOverview);
        Boundary.Normalize();
        PlanFeatures.Normalize(Boundary.SourceId, Boundary.SourceManifestSha256);
    }

    public void ConfigureForProject(string projectId)
    {
        string normalizedProjectId = RequireProjectId(projectId);
        if (!string.IsNullOrWhiteSpace(OwnerProjectId) &&
            !OwnerProjectId.Equals(normalizedProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Site context belongs to project '{OwnerProjectId}', not '{normalizedProjectId}'.");
        }

        OwnerProjectId = normalizedProjectId;
        Normalize(normalizedProjectId);
    }

    public ProjectSiteContextMap CreateProjectSnapshot(string projectId)
    {
        string normalizedProjectId = RequireProjectId(projectId);
        if (!string.IsNullOrWhiteSpace(OwnerProjectId) &&
            !OwnerProjectId.Equals(normalizedProjectId, StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectSiteContextMap { OwnerProjectId = normalizedProjectId };
        }

        Normalize(normalizedProjectId);
        return new ProjectSiteContextMap
        {
            OwnerProjectId = normalizedProjectId,
            LocationScheme = LocationScheme.Clone(),
            SurroundingsOverview = SurroundingsOverview.Clone(),
            Boundary = Boundary.Clone(),
            PlanFeatures = PlanFeatures.Clone(),
            UpdatedAtUtc = UpdatedAtUtc,
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

public sealed class ProjectSitePlanFeatures
{
    public string SourceId { get; set; } = "";
    public string SourceManifestSha256 { get; set; } = "";
    public List<ProjectSiteRoadOverlay> Roads { get; set; } = [];
    public List<ProjectSiteBuildingOverlay> Buildings { get; set; } = [];
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public bool HasGeometry => Roads.Any(road => road.HasGeometry) ||
                               Buildings.Any(building => building.HasGeometry);

    public void Normalize(string? expectedSourceId = null, string? expectedManifestSha256 = null)
    {
        SourceId = SourceId?.Trim() ?? "";
        SourceManifestSha256 = SourceManifestSha256?.Trim().ToLowerInvariant() ?? "";
        string normalizedExpectedSourceId = expectedSourceId?.Trim() ?? "";
        string normalizedExpectedHash = expectedManifestSha256?.Trim().ToLowerInvariant() ?? "";
        if (!string.IsNullOrWhiteSpace(normalizedExpectedSourceId) &&
            !string.IsNullOrWhiteSpace(SourceId) &&
            !SourceId.Equals(normalizedExpectedSourceId, StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        if (!string.IsNullOrWhiteSpace(normalizedExpectedHash) &&
            !string.IsNullOrWhiteSpace(SourceManifestSha256) &&
            !SourceManifestSha256.Equals(normalizedExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        Roads = (Roads ?? []).Where(road => road is not null).ToList();
        Buildings = (Buildings ?? []).Where(building => building is not null).ToList();
        bool hasSourceIdentity = !string.IsNullOrWhiteSpace(SourceId) ||
                                 !string.IsNullOrWhiteSpace(SourceManifestSha256) ||
                                 Roads.Count > 0 ||
                                 Buildings.Count > 0;
        if (hasSourceIdentity && string.IsNullOrWhiteSpace(SourceId))
            SourceId = normalizedExpectedSourceId;
        if (hasSourceIdentity && string.IsNullOrWhiteSpace(SourceManifestSha256))
            SourceManifestSha256 = normalizedExpectedHash;
        foreach (ProjectSiteRoadOverlay road in Roads)
            road.Normalize();
        foreach (ProjectSiteBuildingOverlay building in Buildings)
            building.Normalize();
        Roads.RemoveAll(road => !road.HasGeometry);
        Buildings.RemoveAll(building => !building.HasGeometry);
    }

    public ProjectSitePlanFeatures Clone() => new()
    {
        SourceId = SourceId,
        SourceManifestSha256 = SourceManifestSha256,
        Roads = Roads.Select(road => road.Clone()).ToList(),
        Buildings = Buildings.Select(building => building.Clone()).ToList(),
        UpdatedAtUtc = UpdatedAtUtc,
    };

    private void Clear()
    {
        SourceId = "";
        SourceManifestSha256 = "";
        Roads = [];
        Buildings = [];
        UpdatedAtUtc = null;
    }
}

public sealed class ProjectSiteRoadOverlay
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ProjectGeoCoordinate> Path { get; set; } = [];

    public bool HasGeometry => Path.Count >= 2;

    public void Normalize()
    {
        Id = Id?.Trim() ?? "";
        Name = Name?.Trim() ?? "";
        Path = NormalizeCoordinates(Path, closeRing: false);
        if (Path.Count < 2)
            Path.Clear();
    }

    public ProjectSiteRoadOverlay Clone() => new()
    {
        Id = Id,
        Name = Name,
        Path = Path.Select(CloneCoordinate).ToList(),
    };

    internal static List<ProjectGeoCoordinate> NormalizeCoordinates(
        IEnumerable<ProjectGeoCoordinate>? source,
        bool closeRing)
    {
        var points = (source ?? [])
            .Where(IsValidCoordinate)
            .Select(CloneCoordinate)
            .ToList();
        for (int index = points.Count - 1; index > 0; index--)
        {
            if (NearlyEqual(points[index - 1], points[index]))
                points.RemoveAt(index);
        }
        if (closeRing && points.Count >= 3 && !NearlyEqual(points[0], points[^1]))
            points.Add(CloneCoordinate(points[0]));
        return points;
    }

    internal static ProjectGeoCoordinate CloneCoordinate(ProjectGeoCoordinate point) => new()
    {
        Longitude = point.Longitude,
        Latitude = point.Latitude,
    };

    private static bool IsValidCoordinate(ProjectGeoCoordinate? point) =>
        point is not null &&
        double.IsFinite(point.Longitude) &&
        double.IsFinite(point.Latitude) &&
        point.Longitude is >= -180d and <= 180d &&
        point.Latitude is >= -85d and <= 85d;

    private static bool NearlyEqual(ProjectGeoCoordinate left, ProjectGeoCoordinate right) =>
        Math.Abs(left.Longitude - right.Longitude) <= 1e-10 &&
        Math.Abs(left.Latitude - right.Latitude) <= 1e-10;
}

public sealed class ProjectSiteBuildingOverlay
{
    public string Id { get; set; } = "";
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string BuildingType { get; set; } = "";
    public List<ProjectGeoCoordinate> Ring { get; set; } = [];

    public bool HasGeometry => Ring.Count >= 4;

    public void Normalize()
    {
        Id = Id?.Trim() ?? "";
        Number = Number?.Trim() ?? "";
        Name = Name?.Trim() ?? "";
        BuildingType = BuildingType?.Trim() ?? "";
        Ring = ProjectSiteRoadOverlay.NormalizeCoordinates(Ring, closeRing: true);
        if (Ring.Count < 4)
            Ring.Clear();
    }

    public ProjectSiteBuildingOverlay Clone() => new()
    {
        Id = Id,
        Number = Number,
        Name = Name,
        BuildingType = BuildingType,
        Ring = Ring.Select(ProjectSiteRoadOverlay.CloneCoordinate).ToList(),
    };
}

public sealed class ProjectSiteBoundary
{
    public string SourceId { get; set; } = "";
    public string SourceDocumentName { get; set; } = "";
    public string SourceManifestSha256 { get; set; } = "";
    public string SourceCrsName { get; set; } = "";
    public int SourceEpsg { get; set; }
    public string CoordinateMode { get; set; } = "";
    public double AreaSquareMeters { get; set; }
    public List<ProjectGeoCoordinate> Ring { get; set; } = [];
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public bool HasGeometry => Ring.Count >= 4;

    public void Normalize()
    {
        SourceId = SourceId?.Trim() ?? "";
        SourceDocumentName = SourceDocumentName?.Trim() ?? "";
        SourceManifestSha256 = SourceManifestSha256?.Trim().ToLowerInvariant() ?? "";
        SourceCrsName = SourceCrsName?.Trim() ?? "";
        CoordinateMode = CoordinateMode?.Trim() ?? "";
        SourceEpsg = SourceEpsg is >= 32645 and <= 32650 ? SourceEpsg : 0;
        AreaSquareMeters = double.IsFinite(AreaSquareMeters)
            ? Math.Max(0.0, AreaSquareMeters)
            : 0.0;
        Ring = (Ring ?? [])
            .Where(point => point is not null &&
                            double.IsFinite(point.Longitude) &&
                            double.IsFinite(point.Latitude) &&
                            point.Longitude is >= -180d and <= 180d &&
                            point.Latitude is >= -85d and <= 85d)
            .Select(point => new ProjectGeoCoordinate
            {
                Longitude = point.Longitude,
                Latitude = point.Latitude,
            })
            .ToList();
        RemoveConsecutiveDuplicates(Ring);
        if (Ring.Count >= 3 && !NearlyEqual(Ring[0], Ring[^1]))
        {
            Ring.Add(new ProjectGeoCoordinate
            {
                Longitude = Ring[0].Longitude,
                Latitude = Ring[0].Latitude,
            });
        }
        if (Ring.Count < 4)
            Ring.Clear();
    }

    public ProjectSiteBoundary Clone() => new()
    {
        SourceId = SourceId,
        SourceDocumentName = SourceDocumentName,
        SourceManifestSha256 = SourceManifestSha256,
        SourceCrsName = SourceCrsName,
        SourceEpsg = SourceEpsg,
        CoordinateMode = CoordinateMode,
        AreaSquareMeters = AreaSquareMeters,
        Ring = Ring.Select(point => new ProjectGeoCoordinate
        {
            Longitude = point.Longitude,
            Latitude = point.Latitude,
        }).ToList(),
        UpdatedAtUtc = UpdatedAtUtc,
    };

    private static void RemoveConsecutiveDuplicates(List<ProjectGeoCoordinate> points)
    {
        for (int index = points.Count - 1; index > 0; index--)
        {
            if (NearlyEqual(points[index - 1], points[index]))
                points.RemoveAt(index);
        }
    }

    private static bool NearlyEqual(ProjectGeoCoordinate left, ProjectGeoCoordinate right) =>
        Math.Abs(left.Longitude - right.Longitude) <= 1e-10 &&
        Math.Abs(left.Latitude - right.Latitude) <= 1e-10;
}

public sealed class ProjectGeoCoordinate
{
    public double Longitude { get; set; }
    public double Latitude { get; set; }
}

public sealed class ProjectMapLandmark
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public ProjectGeoCoordinate Coordinate { get; set; } = new();
    public string Color { get; set; } = ProjectMapAnnotationDefaults.AccentColor;
    public double Size { get; set; } = 1d;
    public bool IsProjectSite { get; set; }

    public bool HasCoordinate => ProjectMapAnnotationDefaults.IsValidCoordinate(Coordinate);

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = Name?.Trim() ?? "";
        Coordinate ??= new ProjectGeoCoordinate();
        Color = ProjectMapAnnotationDefaults.NormalizeColor(Color);
        Size = double.IsFinite(Size) ? Math.Clamp(Size, 0.65d, 1.8d) : 1d;
        Number = Math.Max(0, Number);
    }

    public ProjectMapLandmark Clone() => new()
    {
        Id = Id,
        Number = Number,
        Name = Name,
        Coordinate = ProjectMapAnnotationDefaults.CloneCoordinate(Coordinate),
        Color = Color,
        Size = Size,
        IsProjectSite = IsProjectSite,
    };
}

public sealed class ProjectMapDistanceMeasure
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ProjectGeoCoordinate> Path { get; set; } = [];
    public string Color { get; set; } = ProjectMapAnnotationDefaults.MeasureColor;
    public double StrokeWidth { get; set; } = 1d;

    public bool HasGeometry => Path.Count >= 2;

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = Name?.Trim() ?? "";
        Path = ProjectSiteRoadOverlay.NormalizeCoordinates(Path, closeRing: false);
        Color = ProjectMapAnnotationDefaults.NormalizeColor(
            Color,
            ProjectMapAnnotationDefaults.MeasureColor);
        StrokeWidth = double.IsFinite(StrokeWidth)
            ? Math.Clamp(StrokeWidth, 0.65d, 2.5d)
            : 1d;
    }

    public ProjectMapDistanceMeasure Clone() => new()
    {
        Id = Id,
        Name = Name,
        Path = Path.Select(ProjectMapAnnotationDefaults.CloneCoordinate).ToList(),
        Color = Color,
        StrokeWidth = StrokeWidth,
    };
}

public sealed class ProjectMapRadiusMeasure
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ProjectGeoCoordinate Center { get; set; } = new();
    public List<double> RadiiMeters { get; set; } = [];
    public List<string> RingColors { get; set; } = [];
    public double RadiusMeters { get; set; } = 500d;
    public string Color { get; set; } = ProjectMapAnnotationDefaults.MeasureColor;
    public double StrokeWidth { get; set; } = 1d;

    public bool HasGeometry =>
        ProjectMapAnnotationDefaults.IsValidCoordinate(Center) &&
        (RadiiMeters?.Any(radius => double.IsFinite(radius) && radius > 0) == true ||
         double.IsFinite(RadiusMeters) && RadiusMeters > 0);

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = Name?.Trim() ?? "";
        Center ??= new ProjectGeoCoordinate();
        Color = ProjectMapAnnotationDefaults.NormalizeColor(
            Color,
            ProjectMapAnnotationDefaults.MeasureColor);
        RadiiMeters ??= [];
        RingColors ??= [];
        List<(double Radius, string Color)> normalizedRings = RadiiMeters
            .Select((radius, index) => (
                Radius: radius,
                Color: index < RingColors.Count ? RingColors[index] : Color))
            .Where(item => double.IsFinite(item.Radius) && item.Radius > 0)
            .Select(item => (
                Radius: Math.Clamp(item.Radius, 1d, 1_000_000d),
                Color: ProjectMapAnnotationDefaults.NormalizeColor(item.Color, Color)))
            .OrderBy(item => item.Radius)
            .GroupBy(item => item.Radius)
            .Select(group => group.First())
            .ToList();
        if (normalizedRings.Count == 0)
        {
            normalizedRings.Add((
                double.IsFinite(RadiusMeters) && RadiusMeters > 0
                    ? Math.Clamp(RadiusMeters, 1d, 1_000_000d)
                    : 500d,
                Color));
        }

        RadiiMeters = normalizedRings.Select(item => item.Radius).ToList();
        RingColors = normalizedRings.Select(item => item.Color).ToList();
        RadiusMeters = RadiiMeters[0];
        Color = RingColors[0];
        StrokeWidth = double.IsFinite(StrokeWidth)
            ? Math.Clamp(StrokeWidth, 0.65d, 2.5d)
            : 1d;
    }

    public ProjectMapRadiusMeasure Clone() => new()
    {
        Id = Id,
        Name = Name,
        Center = ProjectMapAnnotationDefaults.CloneCoordinate(Center),
        RadiiMeters = RadiiMeters?.ToList() ?? [],
        RingColors = RingColors?.ToList() ?? [],
        RadiusMeters = RadiusMeters,
        Color = Color,
        StrokeWidth = StrokeWidth,
    };
}

internal static class ProjectMapAnnotationDefaults
{
    public const string AccentColor = "#e5484d";
    public const string MeasureColor = "#1668dc";

    public static bool IsValidCoordinate(ProjectGeoCoordinate? point) =>
        point is not null &&
        double.IsFinite(point.Longitude) &&
        double.IsFinite(point.Latitude) &&
        point.Longitude is >= -180d and <= 180d &&
        point.Latitude is >= -85d and <= 85d;

    public static ProjectGeoCoordinate CloneCoordinate(ProjectGeoCoordinate? point) => new()
    {
        Longitude = point?.Longitude ?? 0d,
        Latitude = point?.Latitude ?? 0d,
    };

    public static string NormalizeColor(string? value, string fallback = AccentColor)
    {
        string normalized = value?.Trim() ?? "";
        if (normalized.Length == 7 &&
            normalized[0] == '#' &&
            normalized.Skip(1).All(Uri.IsHexDigit))
        {
            return normalized.ToLowerInvariant();
        }

        return fallback;
    }
}

public sealed class ProjectMapViewport
{
    public string Kind { get; set; } = ProjectMapViewportKinds.LocationScheme;
    public string ProviderId { get; set; } = ProjectMapProviderIds.OpenStreetMap;
    public double CenterLatitude { get; set; } = 47.9184d;
    public double CenterLongitude { get; set; } = 106.9177d;
    public double Zoom { get; set; } = 15d;
    public double DetailZoom { get; set; }
    public double Bearing { get; set; }
    public List<ProjectMapLandmark> Landmarks { get; set; } = [];
    public List<ProjectMapDistanceMeasure> DistanceMeasures { get; set; } = [];
    public List<ProjectMapRadiusMeasure> RadiusMeasures { get; set; } = [];
    public string SnapshotRelativePath { get; set; } = "";
    public string SnapshotSha256 { get; set; } = "";
    public int SnapshotPixelWidth { get; set; }
    public int SnapshotPixelHeight { get; set; }
    public string Attribution { get; set; } = "";
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public bool HasSnapshot => !string.IsNullOrWhiteSpace(SnapshotRelativePath);

    public void Normalize(string fallbackKind)
    {
        Kind = string.IsNullOrWhiteSpace(Kind) ? fallbackKind : Kind.Trim();
        ProviderId = ProjectMapProviderIds.Normalize(ProviderId);
        CenterLatitude = double.IsFinite(CenterLatitude)
            ? Math.Clamp(CenterLatitude, -85d, 85d)
            : 47.9184d;
        CenterLongitude = double.IsFinite(CenterLongitude)
            ? Math.Clamp(CenterLongitude, -180d, 180d)
            : 106.9177d;
        Zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, 1d, 22d) : 15d;
        DetailZoom = double.IsFinite(DetailZoom) && DetailZoom > 0
            ? Math.Clamp(Math.Max(Zoom, DetailZoom), 1d, 22d)
            : Zoom;
        Bearing = double.IsFinite(Bearing) ? Bearing % 360d : 0d;
        Landmarks = (Landmarks ?? [])
            .Where(item => item is not null)
            .ToList();
        DistanceMeasures = (DistanceMeasures ?? [])
            .Where(item => item is not null)
            .ToList();
        RadiusMeasures = (RadiusMeasures ?? [])
            .Where(item => item is not null)
            .ToList();
        foreach (ProjectMapLandmark landmark in Landmarks)
            landmark.Normalize();
        foreach (ProjectMapDistanceMeasure measure in DistanceMeasures)
            measure.Normalize();
        foreach (ProjectMapRadiusMeasure measure in RadiusMeasures)
            measure.Normalize();
        Landmarks.RemoveAll(item => !item.HasCoordinate);
        DistanceMeasures.RemoveAll(item => !item.HasGeometry);
        RadiusMeasures.RemoveAll(item => !item.HasGeometry);
        NormalizeLandmarkNumbers(Landmarks);
        SnapshotRelativePath = SnapshotRelativePath?.Trim() ?? "";
        SnapshotSha256 = SnapshotSha256?.Trim() ?? "";
        SnapshotPixelWidth = Math.Max(0, SnapshotPixelWidth);
        SnapshotPixelHeight = Math.Max(0, SnapshotPixelHeight);
        Attribution = Attribution?.Trim() ?? "";
    }

    public ProjectMapViewport Clone() => new()
    {
        Kind = Kind,
        ProviderId = ProviderId,
        CenterLatitude = CenterLatitude,
        CenterLongitude = CenterLongitude,
        Zoom = Zoom,
        DetailZoom = DetailZoom,
        Bearing = Bearing,
        Landmarks = Landmarks.Select(item => item.Clone()).ToList(),
        DistanceMeasures = DistanceMeasures.Select(item => item.Clone()).ToList(),
        RadiusMeasures = RadiusMeasures.Select(item => item.Clone()).ToList(),
        SnapshotRelativePath = SnapshotRelativePath,
        SnapshotSha256 = SnapshotSha256,
        SnapshotPixelWidth = SnapshotPixelWidth,
        SnapshotPixelHeight = SnapshotPixelHeight,
        Attribution = Attribution,
        UpdatedAtUtc = UpdatedAtUtc,
    };

    private static void NormalizeLandmarkNumbers(List<ProjectMapLandmark> landmarks)
    {
        var used = new HashSet<int>();
        int next = 1;
        foreach (ProjectMapLandmark landmark in landmarks)
        {
            if (landmark.Number <= 0 || !used.Add(landmark.Number))
            {
                while (used.Contains(next))
                    next++;
                landmark.Number = next;
                used.Add(next);
            }

            next = Math.Max(next, landmark.Number + 1);
        }
    }

    public static ProjectMapViewport CreateLocationScheme() => new()
    {
        Kind = ProjectMapViewportKinds.LocationScheme,
        Zoom = 15d,
    };

    public static ProjectMapViewport CreateSurroundingsOverview() => new()
    {
        Kind = ProjectMapViewportKinds.SurroundingsOverview,
        Zoom = 12d,
    };
}

public static class ProjectMapViewportKinds
{
    public const string LocationScheme = "LocationScheme";
    public const string SurroundingsOverview = "SurroundingsOverview";
}

public static class ProjectMapProviderIds
{
    public const string OpenStreetMap = "OpenStreetMap";
    public const string OpenTopoMap = "OpenTopoMap";
    public const string GoogleRoad = "GoogleRoad";
    public const string GoogleSatellite = "GoogleSatellite";
    public const string AzureRoad = "AzureRoad";
    public const string AzureAerial = "AzureAerial";

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        OpenStreetMap,
        OpenTopoMap,
        GoogleRoad,
        GoogleSatellite,
        AzureRoad,
        AzureAerial,
    };

    public static string Normalize(string? providerId) =>
        !string.IsNullOrWhiteSpace(providerId) && Known.Contains(providerId.Trim())
            ? Known.First(value => value.Equals(providerId.Trim(), StringComparison.OrdinalIgnoreCase))
            : OpenStreetMap;
}
