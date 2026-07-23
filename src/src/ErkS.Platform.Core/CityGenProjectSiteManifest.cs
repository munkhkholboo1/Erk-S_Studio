using System.Security.Cryptography;
using System.Text.Json;

namespace ErkS.Platform.Core;

public sealed class CityGenProjectSiteReconciliationResult
{
    public bool Changed { get; internal set; }
    public bool Imported { get; internal set; }
    public int ErrorCount { get; internal set; }
    public string SourceId { get; internal set; } = "";
    public string SourceDocumentName { get; internal set; } = "";
    public string Message { get; internal set; } = "";
}

public static class CityGenProjectSiteManifestContract
{
    public const string Schema = "erks.citygen.project-site";
    public const int CurrentSchemaVersion = 1;
    public const string SidecarSuffix = ".erks-citygen-site.json";

    public static string ResolveSidecarPath(string? nativeDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(nativeDocumentPath))
            return "";
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(nativeDocumentPath.Trim());
        }
        catch
        {
            return "";
        }

        if (fullPath.EndsWith(SidecarSuffix, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        if (!Path.GetExtension(fullPath).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            return "";
        return Path.Combine(
            Path.GetDirectoryName(fullPath) ?? "",
            Path.GetFileNameWithoutExtension(fullPath) + SidecarSuffix);
    }
}

public static class CityGenProjectSiteReconciler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static CityGenProjectSiteReconciliationResult Reconcile(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.SiteContext ??= new ProjectSiteContextMap();
        project.SiteContext.Normalize(project.ProjectId);

        List<ManifestCandidate> candidates = [];
        var result = new CityGenProjectSiteReconciliationResult();
        foreach ((ProjectDesignSource source, string path) in EnumerateCandidatePaths(project.Sources))
        {
            if (!File.Exists(path))
                continue;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                CityGenManifest manifest = JsonSerializer.Deserialize<CityGenManifest>(bytes, JsonOptions)
                    ?? throw new InvalidDataException("CityGen project-site manifest is empty.");
                string manifestSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
                ManifestConversion conversion = ConvertManifest(source, manifest, manifestSha256);
                candidates.Add(new ManifestCandidate(
                    source,
                    conversion.Boundary,
                    conversion.PlanFeatures,
                    manifest.UpdatedAtUtc,
                    File.GetLastWriteTimeUtc(path)));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                result.ErrorCount++;
                result.Message = exception.Message;
            }
        }

        ProjectSiteContextSourceLock? sourceLock =
            ProjectSiteContextEditingPolicy.ResolveCanonicalSourceLock(project);
        if (sourceLock is not null)
        {
            candidates = candidates
                .Where(candidate =>
                    ProjectSiteContextEditingPolicy.MatchesCanonicalSource(
                        project,
                        candidate.Source))
                .ToList();
            if (candidates.Count == 0)
            {
                result.Message =
                    "Байршлын схем нь Cloud ERA-д бүртгэлтэй ерөнхий төлөвлөгөөний " +
                    "эх үүсвэрийн хариуцагчид түгжигдсэн байна. Энэ төхөөрөмжийн өөр " +
                    "эх үүсвэрээр төслийн хил болон газрын зургийг солихгүй.";
                return result;
            }
        }

        if (candidates.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(result.Message))
                result.Message = "Холбосон CityGen төслийн талбайн өөрчлөлт алга.";
            return result;
        }

        string currentSourceId = project.SiteContext.Boundary.SourceId;
        ManifestCandidate selected = candidates
            .OrderByDescending(candidate =>
                !string.IsNullOrWhiteSpace(currentSourceId) &&
                candidate.Source.Id.Equals(currentSourceId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.Source.Kind == DesignSourceKind.CityGen)
            .ThenByDescending(candidate => candidate.ManifestUpdatedAtUtc)
            .ThenByDescending(candidate => candidate.FileUpdatedAtUtc)
            .ThenBy(candidate => candidate.Source.Id, StringComparer.OrdinalIgnoreCase)
            .First();

        ProjectSiteBoundary existing = project.SiteContext.Boundary;
        ProjectSitePlanFeatures existingPlanFeatures = project.SiteContext.PlanFeatures;
        if (existing.SourceId.Equals(selected.Boundary.SourceId, StringComparison.OrdinalIgnoreCase) &&
            existing.SourceManifestSha256.Equals(
                selected.Boundary.SourceManifestSha256,
                StringComparison.OrdinalIgnoreCase) &&
            existingPlanFeatures.SourceId.Equals(selected.Source.Id, StringComparison.OrdinalIgnoreCase) &&
            existingPlanFeatures.SourceManifestSha256.Equals(
                selected.Boundary.SourceManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            result.SourceId = selected.Source.Id;
            result.SourceDocumentName = selected.Boundary.SourceDocumentName;
            result.Message = "CityGen төслийн талбай өөрчлөгдөөгүй.";
            return result;
        }

        project.SiteContext.Boundary = selected.Boundary;
        project.SiteContext.PlanFeatures = selected.PlanFeatures;
        FitMapViewports(project.SiteContext, selected.Boundary);
        project.SiteContext.UpdatedAtUtc = DateTimeOffset.UtcNow;
        result.Changed = true;
        result.Imported = true;
        result.SourceId = selected.Source.Id;
        result.SourceDocumentName = selected.Boundary.SourceDocumentName;
        result.Message =
            $"CityGen төслийн талбай шинэчлэгдлээ: {selected.Boundary.SourceDocumentName} · " +
            $"{selected.Boundary.SourceCrsName}.";
        return result;
    }

    public static IEnumerable<string> EnumerateSidecarPaths(IEnumerable<ProjectDesignSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return EnumerateCandidatePaths(sources)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<(ProjectDesignSource Source, string Path)> EnumerateCandidatePaths(
        IEnumerable<ProjectDesignSource> sources)
    {
        foreach (ProjectDesignSource source in sources.Where(source =>
                     source is not null &&
                     source.Kind is DesignSourceKind.AutoCad or DesignSourceKind.CityGen))
        {
            string explicitPath = source.Metadata.TryGetValue("CityGenProjectSiteManifestPath", out string? configured)
                ? configured
                : "";
            string path = !string.IsNullOrWhiteSpace(explicitPath)
                ? TryGetFullPath(explicitPath)
                : CityGenProjectSiteManifestContract.ResolveSidecarPath(source.NativeDocumentPath);
            if (!string.IsNullOrWhiteSpace(path))
                yield return (source, path);
        }
    }

    private static ManifestConversion ConvertManifest(
        ProjectDesignSource source,
        CityGenManifest manifest,
        string manifestSha256)
    {
        if (!manifest.Schema.Equals(CityGenProjectSiteManifestContract.Schema, StringComparison.Ordinal) ||
            manifest.SchemaVersion != CityGenProjectSiteManifestContract.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported CityGen project-site schema '{manifest.Schema}' v{manifest.SchemaVersion}.");
        }
        if (manifest.SourceCrs.Epsg is < 32645 or > 32650)
            throw new InvalidDataException("CityGen project site must use UTM84-45N through UTM84-50N.");
        if (!manifest.Geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) ||
            manifest.Geometry.Coordinates.Count == 0)
        {
            throw new InvalidDataException("CityGen project-site polygon is missing.");
        }

        List<ProjectGeoCoordinate> ring = manifest.Geometry.Coordinates[0]
            .Where(point => point is { Length: >= 2 })
            .Select(point => new ProjectGeoCoordinate
            {
                Longitude = point[0],
                Latitude = point[1],
            })
            .ToList();
        var boundary = new ProjectSiteBoundary
        {
            SourceId = source.Id,
            SourceDocumentName = string.IsNullOrWhiteSpace(manifest.SourceDocument.Name)
                ? source.NativeDocumentTitle
                : manifest.SourceDocument.Name,
            SourceManifestSha256 = manifestSha256,
            SourceCrsName = manifest.SourceCrs.Name,
            SourceEpsg = manifest.SourceCrs.Epsg,
            CoordinateMode = manifest.CoordinateMode,
            AreaSquareMeters = manifest.AreaSquareMeters,
            Ring = ring,
            UpdatedAtUtc = manifest.UpdatedAtUtc,
        };
        boundary.Normalize();
        if (!boundary.HasGeometry)
            throw new InvalidDataException("CityGen project-site polygon has fewer than three valid points.");
        return new ManifestConversion(
            boundary,
            ConvertPlanFeatures(source, manifest, manifestSha256));
    }

    private static ProjectSitePlanFeatures ConvertPlanFeatures(
        ProjectDesignSource source,
        CityGenManifest manifest,
        string manifestSha256)
    {
        var converted = new ProjectSitePlanFeatures
        {
            SourceId = source.Id,
            SourceManifestSha256 = manifestSha256,
            UpdatedAtUtc = manifest.UpdatedAtUtc,
        };
        CityGenPlanFeatures planFeatures = manifest.PlanFeatures ?? new CityGenPlanFeatures();

        foreach (CityGenRoadFeature road in planFeatures.Roads ?? [])
        {
            if (!road.Geometry.Type.Equals("LineString", StringComparison.OrdinalIgnoreCase))
                continue;
            var overlay = new ProjectSiteRoadOverlay
            {
                Id = road.Id,
                Name = road.Name,
                Path = (road.Geometry.Coordinates ?? [])
                    .Where(point => point is { Length: >= 2 })
                    .Select(point => new ProjectGeoCoordinate
                    {
                        Longitude = point[0],
                        Latitude = point[1],
                    })
                    .ToList(),
            };
            overlay.Normalize();
            if (overlay.HasGeometry)
                converted.Roads.Add(overlay);
        }

        foreach (CityGenBuildingFeature building in planFeatures.Buildings ?? [])
        {
            if (!building.Geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) ||
                building.Geometry.Coordinates.Count == 0)
            {
                continue;
            }
            var overlay = new ProjectSiteBuildingOverlay
            {
                Id = building.Id,
                Number = building.Number,
                Name = building.Name,
                BuildingType = building.BuildingType,
                Ring = building.Geometry.Coordinates[0]
                    .Where(point => point is { Length: >= 2 })
                    .Select(point => new ProjectGeoCoordinate
                    {
                        Longitude = point[0],
                        Latitude = point[1],
                    })
                    .ToList(),
            };
            overlay.Normalize();
            if (overlay.HasGeometry)
                converted.Buildings.Add(overlay);
        }

        converted.Normalize(source.Id, manifestSha256);
        return converted;
    }

    private static void FitMapViewports(ProjectSiteContextMap siteContext, ProjectSiteBoundary boundary)
    {
        double west = boundary.Ring.Min(point => point.Longitude);
        double south = boundary.Ring.Min(point => point.Latitude);
        double east = boundary.Ring.Max(point => point.Longitude);
        double north = boundary.Ring.Max(point => point.Latitude);
        double centerLongitude = (west + east) * 0.5;
        double centerLatitude = InverseMercatorLatitude(
            (MercatorLatitude(south) + MercatorLatitude(north)) * 0.5);
        double locationZoom = ComputeFitZoom(west, south, east, north, paddingFactor: 1.55);
        double overviewZoom = Math.Clamp(locationZoom - 3.0, 1.0, 19.0);
        ApplyViewportIfUnconfigured(
            siteContext.LocationScheme,
            centerLatitude,
            centerLongitude,
            locationZoom);
        ApplyViewportIfUnconfigured(
            siteContext.SurroundingsOverview,
            centerLatitude,
            centerLongitude,
            overviewZoom);
    }

    private static void ApplyViewportIfUnconfigured(
        ProjectMapViewport viewport,
        double latitude,
        double longitude,
        double zoom)
    {
        if (HasStudioOwnedComposition(viewport))
        {
            viewport.Normalize(viewport.Kind);
            return;
        }

        viewport.CenterLatitude = latitude;
        viewport.CenterLongitude = longitude;
        viewport.Zoom = zoom;
        viewport.DetailZoom = zoom;
        viewport.Bearing = 0;
        viewport.Normalize(viewport.Kind);
    }

    private static bool HasStudioOwnedComposition(ProjectMapViewport viewport) =>
        viewport.HasSnapshot ||
        !string.IsNullOrWhiteSpace(viewport.SnapshotSha256) ||
        viewport.SnapshotPixelWidth > 0 ||
        viewport.SnapshotPixelHeight > 0 ||
        viewport.UpdatedAtUtc.HasValue ||
        !string.IsNullOrWhiteSpace(viewport.Attribution) ||
        !ProjectMapProviderIds.Normalize(viewport.ProviderId).Equals(
            ProjectMapProviderIds.OpenStreetMap,
            StringComparison.OrdinalIgnoreCase) ||
        Math.Abs(viewport.Bearing) > 1e-9;

    private static double ComputeFitZoom(
        double west,
        double south,
        double east,
        double north,
        double paddingFactor)
    {
        const double tileSize = 256.0;
        const double viewportWidth = 1000.0;
        const double viewportHeight = 1206.0;
        double longitudeFraction = Math.Max((east - west) / 360.0, 1e-12);
        double latitudeFraction = Math.Max(
            Math.Abs(MercatorLatitude(north) - MercatorLatitude(south)) / (2.0 * Math.PI),
            1e-12);
        double zoomX = Math.Log2(viewportWidth / paddingFactor / tileSize / longitudeFraction);
        double zoomY = Math.Log2(viewportHeight / paddingFactor / tileSize / latitudeFraction);
        return Math.Clamp(Math.Floor(Math.Min(zoomX, zoomY) * 4.0) / 4.0, 1.0, 19.0);
    }

    private static double MercatorLatitude(double latitude)
    {
        double radians = Math.Clamp(latitude, -85.0, 85.0) * Math.PI / 180.0;
        return Math.Log(Math.Tan(Math.PI / 4.0 + radians / 2.0));
    }

    private static double InverseMercatorLatitude(double value) =>
        (2.0 * Math.Atan(Math.Exp(value)) - Math.PI / 2.0) * 180.0 / Math.PI;

    private static string TryGetFullPath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path.Trim());
        }
        catch
        {
            return "";
        }
    }

    private sealed record ManifestCandidate(
        ProjectDesignSource Source,
        ProjectSiteBoundary Boundary,
        ProjectSitePlanFeatures PlanFeatures,
        DateTimeOffset ManifestUpdatedAtUtc,
        DateTime FileUpdatedAtUtc);

    private sealed record ManifestConversion(
        ProjectSiteBoundary Boundary,
        ProjectSitePlanFeatures PlanFeatures);

    private sealed class CityGenManifest
    {
        public string Schema { get; set; } = "";
        public int SchemaVersion { get; set; }
        public CityGenSourceDocument SourceDocument { get; set; } = new();
        public CityGenCoordinateReference SourceCrs { get; set; } = new();
        public CityGenGeometry Geometry { get; set; } = new();
        public CityGenPlanFeatures? PlanFeatures { get; set; }
        public double AreaSquareMeters { get; set; }
        public string CoordinateMode { get; set; } = "";
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private sealed class CityGenSourceDocument
    {
        public string Name { get; set; } = "";
    }

    private sealed class CityGenCoordinateReference
    {
        public int Epsg { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class CityGenGeometry
    {
        public string Type { get; set; } = "";
        public List<List<double[]>> Coordinates { get; set; } = [];
    }

    private sealed class CityGenLineGeometry
    {
        public string Type { get; set; } = "";
        public List<double[]> Coordinates { get; set; } = [];
    }

    private sealed class CityGenPlanFeatures
    {
        public List<CityGenRoadFeature> Roads { get; set; } = [];
        public List<CityGenBuildingFeature> Buildings { get; set; } = [];
    }

    private sealed class CityGenRoadFeature
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public CityGenLineGeometry Geometry { get; set; } = new();
    }

    private sealed class CityGenBuildingFeature
    {
        public string Id { get; set; } = "";
        public string Number { get; set; } = "";
        public string Name { get; set; } = "";
        public string BuildingType { get; set; } = "";
        public CityGenGeometry Geometry { get; set; } = new();
    }
}
