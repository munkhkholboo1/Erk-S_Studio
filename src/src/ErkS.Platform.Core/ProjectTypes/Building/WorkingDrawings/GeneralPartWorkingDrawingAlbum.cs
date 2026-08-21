namespace ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;
/// <summary>
/// The general part every building album opens with: the cover Studio composes,
/// the drawing list, the explanatory note and the general indicators. These were
/// folded into БА before, which is not the mark they carry.
/// </summary>
public sealed class GeneralPartWorkingDrawingAlbum : IBuildingWorkingDrawingDiscipline { public string Id => "working-drawing-eh"; public string Mark => "ЕХ"; public string Name => "Ерөнхий хэсэг"; public string Title => "Ерөнхий хэсэг"; }
