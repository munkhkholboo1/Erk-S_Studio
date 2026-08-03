namespace ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;
public static class BuildingWorkingDrawingAlbumCatalog
{
    public static IReadOnlyList<IBuildingWorkingDrawingDiscipline> All { get; } = [new ArchitectureWorkingDrawingAlbum(), new StructureWorkingDrawingAlbum(), new HvacWorkingDrawingAlbum(), new ElectricalWorkingDrawingAlbum(), new PlumbingWorkingDrawingAlbum(), new CommunicationsWorkingDrawingAlbum()];
    public static void EnsureAlbums(ProjectWorkspace workspace)
    {
        if (!BuildingWorkingDrawingAlbumTemplate.Supports(workspace.Identity.ProjectType, workspace.Identity.StageCode)) return;
        ProjectAlbumRecord? originalPrimary = workspace.Deliverables.Albums.FirstOrDefault(x => x.IsPrimary);
        foreach (ProjectAlbumRecord existing in workspace.Deliverables.Albums) existing.IsPrimary = false;
        foreach (IBuildingWorkingDrawingDiscipline discipline in All)
        {
            ProjectAlbumRecord? album = workspace.Deliverables.Albums.FirstOrDefault(x => x.Id.Equals(discipline.Id, StringComparison.OrdinalIgnoreCase));
            if (album is null && discipline.Mark == "БА" && originalPrimary is not null) { album = originalPrimary; album.Id = discipline.Id; }
            if (album is null) { album = new ProjectAlbumRecord { Id = discipline.Id, DocumentPath = $"albums/{discipline.Id}.album.json", OutputFolder = ProjectWorkspace.DefaultOutputRelativePath }; workspace.Deliverables.Albums.Add(album); }
            album.Type = workspace.Identity.ProjectType; album.Title = discipline.Title; album.IsPrimary = discipline.Mark == "БА";
        }
    }
    public static bool MatchesMark(IBuildingWorkingDrawingDiscipline discipline, string? mark)
    {
        string value = (mark ?? "").Replace(" ", "").ToUpperInvariant();
        return discipline.Mark == "ДГ,ХТ" ? value is "ДГ,ХТ" or "ДГХТ" or "ДГ" or "ХТ" : value.Equals(discipline.Mark, StringComparison.OrdinalIgnoreCase);
    }
}
