namespace ErkS.Platform.Core;

/// <summary>
/// What the server said about this account's Studio companion entitlement, and
/// when it said it. Studio is free but opens only for an account that holds an
/// active Platform or CityGen licence; this is the record of that grant.
/// </summary>
public sealed record StudioCompanionEntitlement(
    bool StudioCompanion,
    DateTimeOffset? CompanionExpiresAtUtc,
    DateTimeOffset CheckedAtUtc);

/// <summary>
/// How much the server told us this time. The three cases decide the outcome
/// and must not be collapsed: a server that never mentions entitlements is an
/// older deployment and must not lock anyone out, while a server that states
/// the field is authoritative.
/// </summary>
public enum StudioCompanionServerAnswer
{
    /// <summary>No answer at all: offline, or no account signed in.</summary>
    NotContacted,

    /// <summary>The server answered without stating entitlements (older deployment).</summary>
    FieldAbsent,

    /// <summary>The server stated the entitlement; its word is final.</summary>
    Stated,
}

public enum StudioCompanionOutcome
{
    /// <summary>Enforcement is not switched on for this build or server.</summary>
    NotEnforced,

    /// <summary>The server stated an active companion entitlement.</summary>
    Allowed,

    /// <summary>An older server did not state the field, so Studio opens.</summary>
    AllowedByUnknownServer,

    /// <summary>No server answer, but a cached grant is still inside its grace window.</summary>
    AllowedByGrace,

    /// <summary>The server stated there is no active licence behind this account.</summary>
    BlockedNoLicense,

    /// <summary>A cached grant exists but is too old, expired, or its clock is untrustworthy.</summary>
    BlockedGraceExpired,

    /// <summary>Nothing was ever confirmed on this device, so nothing can be trusted offline.</summary>
    BlockedNeverChecked,
}

public sealed record StudioCompanionDecision(StudioCompanionOutcome Outcome)
{
    public bool AllowsStudio => Outcome
        is StudioCompanionOutcome.NotEnforced
        or StudioCompanionOutcome.Allowed
        or StudioCompanionOutcome.AllowedByUnknownServer
        or StudioCompanionOutcome.AllowedByGrace;

    /// <summary>True when the block can only be cleared by reaching the server.</summary>
    public bool NeedsOnlineCheck => Outcome
        is StudioCompanionOutcome.BlockedGraceExpired
        or StudioCompanionOutcome.BlockedNeverChecked;
}

/// <summary>
/// Decides whether Studio may open. The rule is deliberately asymmetric: only
/// an explicit "no" from a server that knows about companion entitlements
/// closes Studio, because treating silence as a refusal would lock out every
/// client that reaches an older deployment.
/// </summary>
public static class StudioCompanionPolicy
{
    /// <summary>How long a confirmed grant keeps Studio open without a server.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    /// <summary>Absorbs ordinary clock skew; a larger backwards jump is treated as tampering.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Reads a grant out of device storage, or refuses it. A grant is only
    /// usable on the device that earned it: a store copied to another machine
    /// proves nothing about this one. A default <paramref name="checkedAtUtc"/>
    /// means nothing was ever confirmed here.
    /// </summary>
    public static StudioCompanionEntitlement? ReadStoredGrant(
        bool studioCompanion,
        DateTimeOffset? companionExpiresAtUtc,
        DateTimeOffset checkedAtUtc,
        string? storedDeviceFingerprint,
        string currentDeviceFingerprint)
    {
        if (checkedAtUtc == default)
            return null;
        if (!string.IsNullOrWhiteSpace(storedDeviceFingerprint) &&
            !storedDeviceFingerprint.Equals(currentDeviceFingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return new StudioCompanionEntitlement(
            studioCompanion,
            companionExpiresAtUtc,
            checkedAtUtc);
    }

    public static StudioCompanionDecision Evaluate(
        bool enforcementEnabled,
        StudioCompanionServerAnswer answer,
        StudioCompanionEntitlement? stated,
        StudioCompanionEntitlement? cached,
        DateTimeOffset nowUtc)
    {
        if (!enforcementEnabled)
            return new StudioCompanionDecision(StudioCompanionOutcome.NotEnforced);

        switch (answer)
        {
            case StudioCompanionServerAnswer.FieldAbsent:
                return new StudioCompanionDecision(StudioCompanionOutcome.AllowedByUnknownServer);

            case StudioCompanionServerAnswer.Stated when stated is not null:
                return new StudioCompanionDecision(stated.StudioCompanion
                    ? StudioCompanionOutcome.Allowed
                    : StudioCompanionOutcome.BlockedNoLicense);

            // A caller claiming the server stated something without carrying the
            // statement has proved nothing; fall through to the offline rules.
            case StudioCompanionServerAnswer.Stated:
            case StudioCompanionServerAnswer.NotContacted:
            default:
                return EvaluateOffline(cached, nowUtc);
        }
    }

    private static StudioCompanionDecision EvaluateOffline(
        StudioCompanionEntitlement? cached,
        DateTimeOffset nowUtc)
    {
        if (cached is null)
            return new StudioCompanionDecision(StudioCompanionOutcome.BlockedNeverChecked);
        if (!cached.StudioCompanion)
            return new StudioCompanionDecision(StudioCompanionOutcome.BlockedNoLicense);

        // A cache stamped in the future means the clock moved back under us.
        if (cached.CheckedAtUtc > nowUtc + ClockTolerance)
            return new StudioCompanionDecision(StudioCompanionOutcome.BlockedGraceExpired);
        if (nowUtc - cached.CheckedAtUtc > GracePeriod + ClockTolerance)
            return new StudioCompanionDecision(StudioCompanionOutcome.BlockedGraceExpired);

        // The grant cannot outlive the licence that issued it. Servers that do
        // not state an expiry leave the grace window as the only limit.
        if (cached.CompanionExpiresAtUtc is { } expiresAtUtc &&
            nowUtc > expiresAtUtc + ClockTolerance)
        {
            return new StudioCompanionDecision(StudioCompanionOutcome.BlockedGraceExpired);
        }

        return new StudioCompanionDecision(StudioCompanionOutcome.AllowedByGrace);
    }
}
