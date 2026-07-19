using ErkS.CloudEra.Client;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class LegacyLoopbackCapabilityTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5055", true, 404, true)]
    [InlineData("http://localhost:5055", true, 404, true)]
    [InlineData("https://erk-s.mn", true, 404, false)]
    [InlineData("http://127.0.0.1:5055", false, 404, false)]
    [InlineData("http://127.0.0.1:5055", true, 500, false)]
    public void CompatibilityMode_IsRestrictedToDevelopmentLoopback404(
        string serverUrl,
        bool developmentBuild,
        int statusCode,
        bool expected)
    {
        var error = new CloudEraContractMismatchException("capabilities failed", statusCode);

        bool allowed = StudioAccountService.CanUseLegacyLoopbackCapabilities(
            serverUrl,
            error,
            developmentBuild);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public void CompatibilityCapabilities_DoNotClaimOptimisticConcurrency()
    {
        CloudEraCapabilitiesSnapshot capabilities =
            StudioAccountService.CreateLegacyLoopbackCapabilities();

        Assert.True(CloudEraCapabilityPolicy.Supports(capabilities, CloudEraFeatures.Projects));
        Assert.True(CloudEraCapabilityPolicy.Supports(capabilities, CloudEraFeatures.AlbumRevisions));
        Assert.True(CloudEraCapabilityPolicy.Supports(capabilities, CloudEraFeatures.ParticipantRoleManagement));
        Assert.False(CloudEraCapabilityPolicy.Supports(capabilities, CloudEraFeatures.OptimisticConcurrency));
    }

    [Fact]
    public void CapabilityRefresh_IsRequiredWhenSnapshotIsMissingOrFeatureIsUnavailable()
    {
        Assert.True(StudioAccountService.NeedsCapabilityRefresh(
            null,
            CloudEraFeatures.ParticipantRoleManagement));

        var unavailable = new CloudEraCapabilitiesSnapshot(
            CloudEraCapabilityPolicy.SupportedApiVersion,
            "/api/cloud-era/v1",
            "",
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [CloudEraFeatures.ParticipantRoleManagement] = false,
            });

        Assert.True(StudioAccountService.NeedsCapabilityRefresh(
            unavailable,
            CloudEraFeatures.ParticipantRoleManagement));
        Assert.False(StudioAccountService.NeedsCapabilityRefresh(
            StudioAccountService.CreateLegacyLoopbackCapabilities(),
            CloudEraFeatures.ParticipantRoleManagement));
    }
}
