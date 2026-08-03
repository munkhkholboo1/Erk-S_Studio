using ErkS.Platform.Core.ProjectTypes;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Studio.App.Tests;

public sealed class StudioProjectCreationClassificationTests
{
    [Theory]
    [InlineData(BuildingDesignProjectType.TypeId, "model-design", StudioCloudTemplateIds.BuildingArchitectureConcept)]
    [InlineData(BuildingDesignProjectType.TypeId, "working-drawings", "")]
    [InlineData(UrbanPlanningProjectType.TypeId, PartialMasterPlanDrawingSequence.StageType, "")]
    public void ResolveTemplateId_UsesConceptTemplateOnlyForBuildingConceptProjects(
        string projectType,
        string stageType,
        string expected)
    {
        Assert.Equal(expected, StudioProjectCreationClassification.ResolveTemplateId(projectType, stageType));
    }

    [Fact]
    public void ValidateCloudEnvelope_RejectsInformationFromAnotherProject()
    {
        var cloud = new StudioCloudProjectDetail
        {
            Project = new StudioCloudProjectSummary { ProjectId = "project-a" },
            ProjectInformation = new StudioCloudProjectInformation { ProjectId = "project-b" },
        };

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => StudioProjectCloudIsolation.ValidateEnvelope(cloud));

        Assert.Contains("project-b", error.Message);
        Assert.Contains("project-a", error.Message);
    }

    [Theory]
    [InlineData("working-drawings", BuildingDesignProjectType.TypeId)]
    [InlineData(PartialMasterPlanDrawingSequence.StageType, UrbanPlanningProjectType.TypeId)]
    public void ResolveCloudProjectType_UsesStageWhenOlderServerOmitsDomain(
        string stageType,
        string expectedProjectType)
    {
        Assert.Equal(
            expectedProjectType,
            StudioProjectCreationClassification.ResolveCloudProjectType("", stageType));
    }
}
