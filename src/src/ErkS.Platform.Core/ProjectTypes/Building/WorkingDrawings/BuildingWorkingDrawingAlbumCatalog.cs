namespace ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;

/// <summary>
/// Working drawings are issued one album per building, and inside an album the
/// sheets run set by set: ЕХ first, then БА, ББ and the rest. The concept stage
/// composes the opposite way - every building of the project in a single album.
/// </summary>
public static class BuildingWorkingDrawingAlbumCatalog
{
    public const string BuildingAlbumIdPrefix = "working-drawing-building-";

    /// <summary>Album id used until the project lists a building of its own.</summary>
    public const string UnassignedBuildingAlbumId = "working-drawing-album";

    /// <summary>The sets in the order they are bound, matching PFA's catalog.</summary>
    public static IReadOnlyList<IBuildingWorkingDrawingDiscipline> All { get; } =
    [
        new GeneralPartWorkingDrawingAlbum(),
        new ArchitectureWorkingDrawingAlbum(),
        new StructureWorkingDrawingAlbum(),
        new HvacWorkingDrawingAlbum(),
        new HeatMechanicalWorkingDrawingAlbum(),
        new PlumbingWorkingDrawingAlbum(),
        new ElectricalWorkingDrawingAlbum(),
        new CommunicationsWorkingDrawingAlbum(),
        new AutomationWorkingDrawingAlbum(),
    ];

    public static void EnsureAlbums(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!BuildingWorkingDrawingAlbumTemplate.Supports(
                workspace.Identity.ProjectType,
                workspace.Identity.StageCode))
        {
            return;
        }

        // Studio loads and saves exactly one album document per project, the
        // primary one. Whatever record the project already had keeps its
        // document and becomes the first building's album, so no pages are
        // orphaned when a project moves off the old per-discipline records.
        ProjectAlbumRecord? carried =
            workspace.Deliverables.Albums.FirstOrDefault(album => album.IsPrimary) ??
            workspace.Deliverables.Albums.FirstOrDefault();

        var wanted = workspace.BuildingGroups
            .OrderBy(group => group.Order)
            .Select(group => (Id: BuildingAlbumId(group.Id), Title: AlbumTitle(group.Name)))
            .ToList();
        if (wanted.Count == 0)
            wanted.Add((UnassignedBuildingAlbumId, AlbumTitle("")));

        var albums = new List<ProjectAlbumRecord>(wanted.Count);
        foreach ((string id, string title) in wanted)
        {
            ProjectAlbumRecord? album = workspace.Deliverables.Albums.FirstOrDefault(existing =>
                existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (album is null && albums.Count == 0 && carried is not null)
            {
                album = carried;
                album.Id = id;
            }

            album ??= new ProjectAlbumRecord
            {
                Id = id,
                DocumentPath = $"albums/{id}.album.json",
                OutputFolder = ProjectWorkspace.DefaultOutputRelativePath,
            };
            album.Type = workspace.Identity.ProjectType;
            album.Title = title;
            album.IsPrimary = albums.Count == 0;
            albums.Add(album);
        }

        workspace.Deliverables.Albums.Clear();
        workspace.Deliverables.Albums.AddRange(albums);
    }

    public static string BuildingAlbumId(string buildingGroupId) =>
        BuildingAlbumIdPrefix + (buildingGroupId ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// The building an album belongs to, or "" for the album a project without
    /// buildings still needs.
    /// </summary>
    public static string BuildingGroupIdOf(string? albumId)
    {
        string id = (albumId ?? "").Trim();
        return id.StartsWith(BuildingAlbumIdPrefix, StringComparison.OrdinalIgnoreCase)
            ? id[BuildingAlbumIdPrefix.Length..]
            : "";
    }

    public static IBuildingWorkingDrawingDiscipline? FindByMark(string? mark) =>
        All.FirstOrDefault(discipline => MatchesMark(discipline, mark));

    /// <summary>
    /// Position of a mark in the album. An unknown or absent mark sorts with the
    /// general part, which is where Studio's own generated pages belong.
    /// </summary>
    public static int SectionOrder(string? mark)
    {
        for (int index = 0; index < All.Count; index++)
        {
            if (MatchesMark(All[index], mark))
                return index;
        }
        return 0;
    }

    public static bool MatchesMark(IBuildingWorkingDrawingDiscipline discipline, string? mark)
    {
        ArgumentNullException.ThrowIfNull(discipline);
        string value = (mark ?? "").Replace(" ", "").ToUpperInvariant();
        if (value.Length == 0)
            return false;

        return discipline.Mark switch
        {
            // Either order of the two-letter set, and either letter alone.
            "ХТ,ДГ" => value is "ХТ,ДГ" or "ХТДГ" or "ДГ,ХТ" or "ДГХТ" or "ХТ" or "ДГ",
            // PFA numbers the general part with the Latin spelling as well.
            "ЕХ" => value is "ЕХ" or "EX",
            _ => value.Equals(discipline.Mark, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static string AlbumTitle(string buildingName)
    {
        string name = (buildingName ?? "").Trim();
        return name.Length == 0
            ? "Ажлын зургийн альбом"
            : $"{name} - ажлын зургийн альбом";
    }
}
