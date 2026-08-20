namespace ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

/// <summary>Хөгжлийн ерөнхий төлөвлөгөөний эхний зураглалын дараалал.</summary>
public sealed class MasterPlanDrawingSequence : IUrbanPlanningDrawingSequence
{
    public const string StageType = "master-plan";
    public string ProjectStageType => StageType;
    public IReadOnlyList<UrbanPlanningDrawingSlot> Drawings { get; } =
        UrbanPlanningDrawingSequenceFactory.CreateMasterPlanSequence();
}
