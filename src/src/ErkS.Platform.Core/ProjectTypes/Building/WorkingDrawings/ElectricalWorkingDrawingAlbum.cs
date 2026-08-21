namespace ErkS.Platform.Core.ProjectTypes.Building.WorkingDrawings;
/// <summary>
/// The saved project keeps the old id, so only the visible mark follows PFA's
/// Хүчит-төхөөрөмж-first spelling; both orders are still recognised on import.
/// </summary>
public sealed class ElectricalWorkingDrawingAlbum : IBuildingWorkingDrawingDiscipline { public string Id => "working-drawing-dg-ht"; public string Mark => "ХТ,ДГ"; public string Name => "Хүчит төхөөрөмж, дотор гэрэлтүүлэг"; public string Title => "Хүчит төхөөрөмж, дотор гэрэлтүүлгийн ажлын зураг"; }
