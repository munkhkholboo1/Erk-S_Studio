using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Studio is free but opens only for an account holding an active Platform or
/// CityGen licence. These cover the decision table of the companion contract.
/// </summary>
public sealed class StudioCompanionPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EnforcementOff_OpensWhateverTheServerSays()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: false,
            StudioCompanionServerAnswer.Stated,
            stated: Grant(false));

        Assert.Equal(StudioCompanionOutcome.NotEnforced, decision.Outcome);
        Assert.True(decision.AllowsStudio);
    }

    [Fact]
    public void ServerWithoutTheField_OpensStudio()
    {
        // An older deployment states nothing. Reading silence as a refusal
        // would lock out every client that reaches one.
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.FieldAbsent,
            stated: null);

        Assert.Equal(StudioCompanionOutcome.AllowedByUnknownServer, decision.Outcome);
        Assert.True(decision.AllowsStudio);
    }

    [Fact]
    public void ServerGrantsCompanion_OpensStudio()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.Stated,
            stated: Grant(true));

        Assert.Equal(StudioCompanionOutcome.Allowed, decision.Outcome);
        Assert.True(decision.AllowsStudio);
    }

    [Fact]
    public void ServerRefusesCompanion_ClosesStudio()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.Stated,
            stated: Grant(false),
            cached: Grant(true));

        // The server's word overrides a cached grant from a better day.
        Assert.Equal(StudioCompanionOutcome.BlockedNoLicense, decision.Outcome);
        Assert.False(decision.AllowsStudio);
        Assert.False(decision.NeedsOnlineCheck);
    }

    [Fact]
    public void OfflineWithFreshCache_OpensStudioOnGrace()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: Grant(true, checkedAtUtc: Now.AddDays(-6)));

        Assert.Equal(StudioCompanionOutcome.AllowedByGrace, decision.Outcome);
        Assert.True(decision.AllowsStudio);
    }

    [Fact]
    public void OfflineWithStaleCache_RequiresAnOnlineCheck()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: Grant(true, checkedAtUtc: Now.AddDays(-8)));

        Assert.Equal(StudioCompanionOutcome.BlockedGraceExpired, decision.Outcome);
        Assert.True(decision.NeedsOnlineCheck);
    }

    [Fact]
    public void OfflineWithNoCache_RequiresAnOnlineCheck()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: null);

        Assert.Equal(StudioCompanionOutcome.BlockedNeverChecked, decision.Outcome);
        Assert.True(decision.NeedsOnlineCheck);
    }

    [Fact]
    public void OfflineWithCachedRefusal_StaysClosed()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: Grant(false));

        Assert.Equal(StudioCompanionOutcome.BlockedNoLicense, decision.Outcome);
    }

    [Fact]
    public void GrantCannotOutliveTheLicenceThatIssuedIt()
    {
        // Checked an hour ago, well inside the grace window, but the granting
        // licence expired yesterday.
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: new StudioCompanionEntitlement(
                StudioCompanion: true,
                CompanionExpiresAtUtc: Now.AddDays(-1),
                CheckedAtUtc: Now.AddHours(-1)));

        Assert.Equal(StudioCompanionOutcome.BlockedGraceExpired, decision.Outcome);
    }

    [Fact]
    public void ClockRolledBack_InvalidatesTheGrace()
    {
        // A cache stamped in the future is the signature of a clock moved back
        // to stretch the grace window.
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: Grant(true, checkedAtUtc: Now.AddHours(1)));

        Assert.Equal(StudioCompanionOutcome.BlockedGraceExpired, decision.Outcome);
    }

    [Fact]
    public void SmallClockSkew_DoesNotInvalidateTheGrace()
    {
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: Grant(true, checkedAtUtc: Now.AddMinutes(1)));

        Assert.Equal(StudioCompanionOutcome.AllowedByGrace, decision.Outcome);
    }

    [Fact]
    public void StatedAnswerWithoutTheStatement_FallsBackToTheCache()
    {
        // Claiming the server spoke without carrying its words proves nothing.
        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.Stated,
            stated: null,
            cached: Grant(true, checkedAtUtc: Now.AddDays(-1)));

        Assert.Equal(StudioCompanionOutcome.AllowedByGrace, decision.Outcome);
    }

    [Fact]
    public void StoredGrant_IsReadBackOnTheDeviceThatEarnedIt()
    {
        StudioCompanionEntitlement? grant = StudioCompanionPolicy.ReadStoredGrant(
            studioCompanion: true,
            companionExpiresAtUtc: Now.AddMonths(3),
            checkedAtUtc: Now.AddDays(-2),
            storedDeviceFingerprint: "device-a",
            currentDeviceFingerprint: "device-a");

        Assert.NotNull(grant);
        Assert.True(grant!.StudioCompanion);
        Assert.Equal(Now.AddDays(-2), grant.CheckedAtUtc);
    }

    [Fact]
    public void StoredGrant_FromAnotherDeviceIsRefused()
    {
        // A store copied to another machine proves nothing about this one.
        Assert.Null(StudioCompanionPolicy.ReadStoredGrant(
            studioCompanion: true,
            companionExpiresAtUtc: null,
            checkedAtUtc: Now,
            storedDeviceFingerprint: "device-a",
            currentDeviceFingerprint: "device-b"));
    }

    [Fact]
    public void StoredGrant_NeverConfirmedIsRefused()
    {
        Assert.Null(StudioCompanionPolicy.ReadStoredGrant(
            studioCompanion: true,
            companionExpiresAtUtc: null,
            checkedAtUtc: default,
            storedDeviceFingerprint: "device-a",
            currentDeviceFingerprint: "device-a"));
    }

    [Fact]
    public void RefusedStoredGrant_LeavesStudioNeedingAnOnlineCheck()
    {
        StudioCompanionEntitlement? grant = StudioCompanionPolicy.ReadStoredGrant(
            studioCompanion: true,
            companionExpiresAtUtc: null,
            checkedAtUtc: Now,
            storedDeviceFingerprint: "device-a",
            currentDeviceFingerprint: "device-b");

        StudioCompanionDecision decision = Evaluate(
            enforcementEnabled: true,
            StudioCompanionServerAnswer.NotContacted,
            stated: null,
            cached: grant);

        Assert.Equal(StudioCompanionOutcome.BlockedNeverChecked, decision.Outcome);
    }

    private static StudioCompanionEntitlement Grant(
        bool studioCompanion,
        DateTimeOffset? checkedAtUtc = null) =>
        new(studioCompanion, null, checkedAtUtc ?? Now);

    private static StudioCompanionDecision Evaluate(
        bool enforcementEnabled,
        StudioCompanionServerAnswer answer,
        StudioCompanionEntitlement? stated,
        StudioCompanionEntitlement? cached = null) =>
        StudioCompanionPolicy.Evaluate(
            enforcementEnabled,
            answer,
            stated,
            cached,
            Now);
}
