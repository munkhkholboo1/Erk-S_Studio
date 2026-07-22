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
        LocationScheme.Normalize(ProjectMapViewportKinds.LocationScheme);
        SurroundingsOverview.Normalize(ProjectMapViewportKinds.SurroundingsOverview);
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

public sealed class ProjectMapViewport
{
    public string Kind { get; set; } = ProjectMapViewportKinds.LocationScheme;
    public string ProviderId { get; set; } = ProjectMapProviderIds.OpenStreetMap;
    public double CenterLatitude { get; set; } = 47.9184d;
    public double CenterLongitude { get; set; } = 106.9177d;
    public double Zoom { get; set; } = 15d;
    public double Bearing { get; set; }
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
        Bearing = double.IsFinite(Bearing) ? Bearing % 360d : 0d;
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
        Bearing = Bearing,
        SnapshotRelativePath = SnapshotRelativePath,
        SnapshotSha256 = SnapshotSha256,
        SnapshotPixelWidth = SnapshotPixelWidth,
        SnapshotPixelHeight = SnapshotPixelHeight,
        Attribution = Attribution,
        UpdatedAtUtc = UpdatedAtUtc,
    };

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
