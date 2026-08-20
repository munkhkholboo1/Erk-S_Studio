namespace ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

/// <summary>Хэсэгчилсэн ерөнхий төлөвлөгөөний эхний зураглалын дараалал.</summary>
public sealed class PartialMasterPlanDrawingSequence : IUrbanPlanningDrawingSequence
{
    public const string StageType = "partial-plan";
    public string ProjectStageType => StageType;
    public IReadOnlyList<UrbanPlanningDrawingSlot> Drawings { get; } =
        UrbanPlanningDrawingSequenceFactory.CreatePartialPlanSequence();
}
