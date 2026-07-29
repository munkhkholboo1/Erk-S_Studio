using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCloudDiagnosticReasonCodeTests
{
    [Fact]
    public void BuildingCompositionConflictKeepsItsExactDiagnosticReason()
    {
        var conflict = new StudioBuildingCompositionConflictException(
            ["building-1:name"]);

        string reason = StudioCloudDiagnosticReasonCode.Resolve(
            conflict,
            "cloud_sync_failed");

        Assert.Equal("building_composition_conflict", reason);
    }
}
