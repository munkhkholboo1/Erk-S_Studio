using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCompanionEnforcementTests
{
    // These describe the rule itself. It is held back for now by
    // LicensingIsOpen, and stays under test so that the release which opens
    // licensing turns on something still known to be correct rather than
    // something nothing has exercised in months.

    [Fact]
    public void DevelopmentBuild_NeverEnforces()
    {
        // Today's builds are all -dev, which is what keeps this work unblocked.
        Assert.False(StudioCompanionEnforcement.WouldEnforce(
            "https://erk-s.mn",
            isDevelopmentBuild: true));
    }

    [Fact]
    public void OfficialBuildAgainstTheLiveServer_Enforces()
    {
        Assert.True(StudioCompanionEnforcement.WouldEnforce(
            "https://erk-s.mn",
            isDevelopmentBuild: false));
    }

    [Fact]
    public void LoopbackServer_DoesNotEnforce()
    {
        // A development database holds no real licences.
        Assert.False(StudioCompanionEnforcement.WouldEnforce(
            "http://127.0.0.1:5055",
            isDevelopmentBuild: false));
        Assert.False(StudioCompanionEnforcement.WouldEnforce(
            "http://localhost:5055",
            isDevelopmentBuild: false));
    }

    [Fact]
    public void ReleaseSmokeRun_DoesNotEnforce()
    {
        // CI publishes the product with a release label and then runs it. A
        // licence prompt there would hang a job nobody can answer.
        Assert.False(StudioCompanionEnforcement.WouldEnforce(
            "https://erk-s.mn",
            isDevelopmentBuild: false,
            commandLineArguments: ["ErkS.Studio.exe", "--release-smoke-test", "--release-smoke-output=x"]));
    }

    [Fact]
    public void ReleaseUpdateHoldRun_DoesNotEnforce()
    {
        Assert.False(StudioCompanionEnforcement.WouldEnforce(
            "https://erk-s.mn",
            isDevelopmentBuild: false,
            commandLineArguments: ["ErkS.Studio.exe", "--release-update-hold-test"]));
    }

    [Fact]
    public void OrdinaryLaunchArguments_StillEnforce()
    {
        Assert.True(StudioCompanionEnforcement.WouldEnforce(
            "https://erk-s.mn",
            isDevelopmentBuild: false,
            commandLineArguments: ["ErkS.Studio.exe"]));
    }

    [Fact]
    public void UnknownServer_StillEnforces()
    {
        // An address we cannot parse must not be mistaken for a local one.
        Assert.True(StudioCompanionEnforcement.WouldEnforce(
            "",
            isDevelopmentBuild: false));
        Assert.True(StudioCompanionEnforcement.WouldEnforce(
            "not a url",
            isDevelopmentBuild: false));
    }

    [Fact]
    public void WhileLicensingIsClosed_NobodyIsEnforcedAgainst()
    {
        // There is no licence to hold yet: the two-licence model is not open,
        // nobody has been told how to buy one, and nothing has been decided
        // for the people already working. A real project is being drawn by
        // four of them, and locking them out of it over a rule none of them
        // could satisfy would be the opposite of what this release is for.
        Assert.False(StudioCompanionEnforcement.LicensingIsOpen);
        Assert.False(StudioCompanionEnforcement.IsEnabledFor(
            "https://erk-s.mn",
            isDevelopmentBuild: false));
        Assert.False(StudioCompanionEnforcement.IsEnabledFor(
            "https://erk-s.mn",
            isDevelopmentBuild: false,
            commandLineArguments: ["ErkS.Studio.exe"]));
    }

    [Fact]
    public void TheHoldIsABuildConstantRatherThanASetting()
    {
        // The original design has no way for an official build to be talked
        // out of enforcement at run time, because that would be the bypass the
        // rule exists to prevent. Holding it back must not become that bypass:
        // it is decided at compile time and cannot be reached from a
        // configuration file, an environment variable or a command line.
        Assert.True(typeof(StudioCompanionEnforcement)
            .GetField(
                nameof(StudioCompanionEnforcement.LicensingIsOpen),
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)
            ?.IsLiteral);
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
