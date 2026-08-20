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

    /// <summary>
    /// What a newly added source is assumed to be before its package is read.
    ///
    /// AutoCAD carries both kinds of drawing - a building's sheets and the project's general
    /// plan - so it has no default worth guessing. Treating it as a building assigned every
    /// general-plan DWG to a building group, which later required a building sub-cover the
    /// album does not draw and stopped the project syncing. It is left unspecified so the
    /// package's own content decides, or the person adding it says.
    /// </summary>
    public static ProjectDesignSourcePurpose DefaultPurpose(
        DesignSourceKind kind) =>
        kind switch
        {
            DesignSourceKind.Revit => ProjectDesignSourcePurpose.Building,
            DesignSourceKind.CityGen =>
                ProjectDesignSourcePurpose.GeneralPlan,
            _ => ProjectDesignSourcePurpose.Unspecified,
        };

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

    /// <param name="allowNewBuildingGroups">
    /// Whether a building the project has never listed may be added. Only an album that
    /// composes buildings may: elsewhere a new group buys nothing and still demands a
    /// building sub-cover that album never draws, which blocks the project's sync.
    /// </param>
    public static bool ApplyPackageBuildingGroupAssignments(
        ProjectWorkspace project,
        ProjectDesignSource source,
        SheetPackageSource packageSource,
        IEnumerable<SheetPackageEntry> entries,
        bool allowNewBuildingGroups = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(packageSource);
        ArgumentNullException.ThrowIfNull(entries);
        if (EffectivePurpose(source) == ProjectDesignSourcePurpose.GeneralPlan)
            return false;

        project.SheetBuildingAssignments ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (SheetPackageEntry entry in entries)
        {
            string key = SheetRecord.MakeKey(packageSource, entry, source.Id);
            if (project.SheetBuildingAssignments.ContainsKey(key))
                continue;

            string buildingId = (entry.BuildingId ?? "").Trim();
            string buildingName = (entry.BuildingName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(buildingId) &&
                string.IsNullOrWhiteSpace(buildingName))
            {
                continue;
            }

            // The package can name a building this project has never seen - the next
            // building of the set, or one drawn against another project's groups.
            // Skipping it left those sheets outside every building, so the group the
            // exporter declared is created here instead.
            ProjectBuildingGroup? group =
                FindBuildingGroup(project, buildingId, buildingName);
            if (group is null)
            {
                if (!allowNewBuildingGroups)
                    continue;
                group = CreateBuildingGroup(project, buildingId, buildingName);
            }

            project.SheetBuildingAssignments[key] = group.Id;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Gives a building source a building group when it has none. A source is often
    /// recognised as a building only once its package is read - by then nobody picked a
    /// group for it, and without one every sheet it delivers stays outside the building
    /// composition.
    /// </summary>
    public static bool EnsureBuildingGroupForSource(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        if (EffectivePurpose(source) != ProjectDesignSourcePurpose.Building)
            return false;

        string groupId = BuildingGroupId(source);
        if (!string.IsNullOrWhiteSpace(groupId) &&
            project.BuildingGroups.Any(group =>
                group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // The source's own sheets may already be filed under a building - by hand, or by an
        // earlier package - while the source itself never recorded one. Adopting that group
        // keeps the source with its sheets instead of adding an empty second building.
        ProjectBuildingGroup? resolved =
            FindGroupAlreadyHoldingSheetsOf(project, source);
        if (resolved is null)
        {
            string name = SuggestBuildingGroupName(source);
            resolved =
                FindBuildingGroup(project, "", name) ??
                CreateBuildingGroup(project, "", name);
        }

        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[BuildingGroupIdKey] = resolved.Id;
        return true;
    }

    /// <summary>
    /// The single building the source's sheets are already filed under, or null when they
    /// are filed under none or under several - there the source has no one building to join.
    /// </summary>
    private static ProjectBuildingGroup? FindGroupAlreadyHoldingSheetsOf(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        string sourceId = (source.Id ?? "").Trim();
        if (string.IsNullOrWhiteSpace(sourceId))
            return null;

        string prefix = sourceId.ToLowerInvariant() + "|";
        string[] groupIds = (project.SheetBuildingAssignments ?? [])
            .Where(assignment => assignment.Key.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
            .Select(assignment => assignment.Value?.Trim() ?? "")
            .Where(groupId => !string.IsNullOrWhiteSpace(groupId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return groupIds.Length == 1
            ? project.BuildingGroups.FirstOrDefault(group =>
                group.Id.Equals(groupIds[0], StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private static ProjectBuildingGroup? FindBuildingGroup(
        ProjectWorkspace project,
        string buildingId,
        string buildingName) =>
        project.BuildingGroups.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(buildingId) &&
             candidate.Id.Equals(
                 buildingId,
                 StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(buildingName) &&
             candidate.Name.Equals(
                 buildingName,
                 StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Adds a building group to the project. A declared id is kept when it is still free so
    /// the next package carrying the same identity resolves to this group rather than adding
    /// a second one beside it.
    /// </summary>
    private static ProjectBuildingGroup CreateBuildingGroup(
        ProjectWorkspace project,
        string buildingId,
        string buildingName)
    {
        string id = (buildingId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id) ||
            project.BuildingGroups.Any(group =>
                group.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            id = Guid.NewGuid().ToString("N");
        }

        string name = (buildingName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = (buildingId ?? "").Trim();

        var created = new ProjectBuildingGroup
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name)
                ? $"Барилга {project.BuildingGroups.Count + 1}"
                : name,
            Order = project.BuildingGroups.Count + 1,
        };
        project.BuildingGroups.Add(created);
        return created;
    }

    private static string SuggestBuildingGroupName(ProjectDesignSource source)
    {
        string title = (source.NativeDocumentTitle ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileName((source.NativeDocumentPath ?? "").Trim());

        title = Path.GetFileNameWithoutExtension(title).Trim();
        return string.IsNullOrWhiteSpace(title)
            ? (source.Name ?? "").Trim()
            : title;
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
        if (normalized.Length == 0)
            return false;

        // AutoCAD sends the drawing mark as the sheet's discipline, and the general-plan album
        // marks its general-plan sheets ЕТ, which spells out neither "ерөнхий төлөвлөгөө" nor
        // "general plan" - so every general-plan DWG was classified as a building. A mark is a
        // code, so match it whole. ИДБ is deliberately not here: that is Инженерийн дэд бүтэц,
        // a different discipline of the same album, and this purpose makes a source the owner
        // of the project's general plan, Project Land and location scheme.
        if (normalized is "ет" or "et")
            return true;

        // Content kinds arrive as template slot ids such as "general-plan-zoning", where the
        // separator is a hyphen rather than the space these phrases were written with.
        string spaced = normalized.Replace('-', ' ').Replace('_', ' ');
        return spaced.Contains("ерөнхий төлөвлөгөө", StringComparison.Ordinal) ||
               spaced.Contains("general plan", StringComparison.Ordinal) ||
               spaced.Contains("site plan", StringComparison.Ordinal) ||
               spaced.Contains("master plan", StringComparison.Ordinal);
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
