using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCompanionEnforcementTests
{
    [Fact]
    public void DevelopmentBuild_NeverEnforces()
    {
        // Today's builds are all -dev, which is what keeps this work unblocked.
        Assert.False(StudioCompanionEnforcement.IsEnabledFor(
            "https://erk-s.mn",
            isDevelopmentBuild: true));
    }

    [Fact]
    public void OfficialBuildAgainstTheLiveServer_Enforces()
    {
        Assert.True(StudioCompanionEnforcement.IsEnabledFor(
            "https://erk-s.mn",
            isDevelopmentBuild: false));
    }

    [Fact]
    public void LoopbackServer_DoesNotEnforce()
    {
        // A development database holds no real licences.
        Assert.False(StudioCompanionEnforcement.IsEnabledFor(
            "http://127.0.0.1:5055",
            isDevelopmentBuild: false));
        Assert.False(StudioCompanionEnforcement.IsEnabledFor(
            "http://localhost:5055",
            isDevelopmentBuild: false));
    }

    [Fact]
    public void ReleaseSmokeRun_DoesNotEnforce()
    {
        // CI publishes the product with a release label and then runs it. A
        // licence prompt there would hang a job nobody can answer.
        Assert.False(StudioCompanionEnforcement.IsEnabledFor(
            "https://erk-s.mn",
            isDevelopmentBuild: false,
            commandLineArguments: ["ErkS.Studio.exe", "--release-smoke-test", "--release-smoke-output=x"]));
    }

    [Fact]
    public void ReleaseUpdateHoldRun_DoesNotEnforce()
    {
        Assert.False(StudioCompanionEnforcement.IsEnabledFor(
            "https://erk-s.mn",
            isDevelopmentBuild: false,
            commandLineArguments: ["ErkS.Studio.exe", "--release-update-hold-test"]));
    }

    [Fact]
    public void OrdinaryLaunchArguments_StillEnforce()
    {
        Assert.True(StudioCompanionEnforcement.IsEnabledFor(
            "https://erk-s.mn",
            isDevelopmentBuild: false,
            commandLineArguments: ["ErkS.Studio.exe"]));
    }

    [Fact]
    public void UnknownServer_StillEnforces()
    {
        // An address we cannot parse must not be mistaken for a local one.
        Assert.True(StudioCompanionEnforcement.IsEnabledFor(
            "",
            isDevelopmentBuild: false));
        Assert.True(StudioCompanionEnforcement.IsEnabledFor(
            "not a url",
            isDevelopmentBuild: false));
    }
}

public sealed class StudioCloudEntitlementsTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SessionWithoutEntitlements_LeavesTheFieldNull()
    {
        // An older server states nothing; null is what makes Studio fail open.
        const string body = """
            { "accessToken": "t", "accountEmail": "a@b.mn", "licenseType": "Pro" }
            """;

        StudioSessionResponse? session =
            JsonSerializer.Deserialize<StudioSessionResponse>(body, Options);

        Assert.NotNull(session);
        Assert.Null(session!.Entitlements);
    }

    [Fact]
    public void SessionWithEntitlements_ReadsTheCompanionGrant()
    {
        const string body = """
            {
              "accessToken": "t",
              "entitlements": {
                "platformTier": "Pro",
                "cityGenTier": "None",
                "studioCompanion": true,
                "companionExpiresAtUtc": "2026-12-31T00:00:00+00:00",
                "features": { "studio.companion": true }
              }
            }
            """;

        StudioSessionResponse? session =
            JsonSerializer.Deserialize<StudioSessionResponse>(body, Options);

        Assert.NotNull(session?.Entitlements);
        Assert.True(session!.Entitlements!.StudioCompanion);
        Assert.Equal("Pro", session.Entitlements.PlatformTier);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            session.Entitlements.CompanionExpiresAtUtc);
        Assert.True(session.Entitlements.Features?["studio.companion"]);
    }

    [Fact]
    public void EntitlementsWithoutExpiry_LeavesTheGraceWindowAsTheOnlyLimit()
    {
        const string body = """
            { "accessToken": "t", "entitlements": { "studioCompanion": false } }
            """;

        StudioSessionResponse? session =
            JsonSerializer.Deserialize<StudioSessionResponse>(body, Options);

        Assert.NotNull(session?.Entitlements);
        Assert.False(session!.Entitlements!.StudioCompanion);
        Assert.Null(session.Entitlements.CompanionExpiresAtUtc);
    }
}
