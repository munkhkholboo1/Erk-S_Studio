using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// Studio-owned classification for a native design source. The values live in
/// the source metadata so older project files remain readable.
/// </summary>
public enum ProjectDesignSourcePurpose
{
    Unspecified,
    GeneralPlan,
    Building,
}

public static class ProjectDesignSourceClassification
{
    private const string ExplicitPurposeKey = "source.purpose";
    private const string DetectedPurposeKey = "source.detectedPurpose";
    private const string BuildingGroupIdKey = "source.buildingGroupId";

    public static ProjectDesignSourcePurpose ExplicitPurpose(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadPurpose(source, ExplicitPurposeKey);
    }

    public static ProjectDesignSourcePurpose DetectedPurpose(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReadPurpose(source, DetectedPurposeKey);
    }

    public static ProjectDesignSourcePurpose EffectivePurpose(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ProjectDesignSourcePurpose explicitPurpose = ExplicitPurpose(source);
        if (explicitPurpose != ProjectDesignSourcePurpose.Unspecified)
            return explicitPurpose;

        ProjectDesignSourcePurpose detectedPurpose = DetectedPurpose(source);
        if (detectedPurpose != ProjectDesignSourcePurpose.Unspecified)
            return detectedPurpose;

        // Existing CityGen sources predate explicit source classification. They
        // remain general-plan sources unless the user explicitly says otherwise.
        return source.Kind == DesignSourceKind.CityGen
            ? ProjectDesignSourcePurpose.GeneralPlan
            : ProjectDesignSourcePurpose.Unspecified;
    }

    public static bool IsGeneralPlan(ProjectDesignSource source) =>
        EffectivePurpose(source) == ProjectDesignSourcePurpose.GeneralPlan;

    public static bool IsExplicitlyBuilding(ProjectDesignSource source) =>
        ExplicitPurpose(source) == ProjectDesignSourcePurpose.Building;

    public static string BuildingGroupId(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return source.Metadata.TryGetValue(BuildingGroupIdKey, out string? value)
            ? value.Trim()
            : "";
    }

    public static void SetExplicitPurpose(
        ProjectDesignSource source,
        ProjectDesignSourcePurpose purpose,
        string buildingGroupId = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[ExplicitPurposeKey] = purpose.ToString();

        string normalizedGroupId = buildingGroupId?.Trim() ?? "";
        if (purpose == ProjectDesignSourcePurpose.Building &&
            !string.IsNullOrWhiteSpace(normalizedGroupId))
        {
            source.Metadata[BuildingGroupIdKey] = normalizedGroupId;
        }
        else
        {
            source.Metadata[BuildingGroupIdKey] = "";
        }
    }

    public static void RecordDetectedPurpose(
        ProjectDesignSource source,
        SheetPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(manifest);
        ProjectDesignSourcePurpose detected = Detect(manifest);
        if (detected == ProjectDesignSourcePurpose.Unspecified)
            return;

        ProjectDesignSourcePurpose previous = DetectedPurpose(source);
        // A partial package must never downgrade a source that was already
        // identified as a general plan.
        if (previous == ProjectDesignSourcePurpose.GeneralPlan &&
            detected == ProjectDesignSourcePurpose.Building)
        {
            return;
        }

        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[DetectedPurposeKey] = detected.ToString();
    }

    public static bool ApplyDefaultBuildingGroupAssignments(
        ProjectWorkspace project,
        ProjectDesignSource source,
        IEnumerable<string> sheetKeys)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sheetKeys);
        if (EffectivePurpose(source) != ProjectDesignSourcePurpose.Building)
            return false;

        string groupId = BuildingGroupId(source);
        if (string.IsNullOrWhiteSpace(groupId) ||
            !project.BuildingGroups.Any(group =>
                group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        project.SheetBuildingAssignments ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (string sheetKey in sheetKeys
                     .Where(key => !string.IsNullOrWhiteSpace(key))
                     .Select(key => key.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // A manual per-sheet assignment is more specific than the source
            // default and must survive later package refreshes.
            if (project.SheetBuildingAssignments.ContainsKey(sheetKey))
                continue;
            project.SheetBuildingAssignments[sheetKey] = groupId;
            changed = true;
        }
        return changed;
    }

    private static ProjectDesignSourcePurpose Detect(SheetPackageManifest manifest)
    {
        if (manifest.Source.Application == SheetSourceApplication.CityGen)
            return ProjectDesignSourcePurpose.GeneralPlan;

        if (manifest.Sheets.Any(entry =>
                IsGeneralPlanText(entry.ContentKind) ||
                IsGeneralPlanText(entry.Discipline)))
        {
            return ProjectDesignSourcePurpose.GeneralPlan;
        }

        return manifest.PackageScope == SheetPackageScope.FullSnapshot &&
               manifest.Sheets.Count > 0
            ? ProjectDesignSourcePurpose.Building
            : ProjectDesignSourcePurpose.Unspecified;
    }

    private static bool IsGeneralPlanText(string? value)
    {
        string normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized.Contains("ерөнхий төлөвлөгөө", StringComparison.Ordinal) ||
               normalized.Contains("general plan", StringComparison.Ordinal) ||
               normalized.Contains("site plan", StringComparison.Ordinal) ||
               normalized.Contains("master plan", StringComparison.Ordinal);
    }

    private static ProjectDesignSourcePurpose ReadPurpose(
        ProjectDesignSource source,
        string key)
    {
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return source.Metadata.TryGetValue(key, out string? value) &&
               Enum.TryParse(value, ignoreCase: true, out ProjectDesignSourcePurpose parsed)
            ? parsed
            : ProjectDesignSourcePurpose.Unspecified;
    }
}
