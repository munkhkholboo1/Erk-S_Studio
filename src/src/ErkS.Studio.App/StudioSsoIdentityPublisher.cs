namespace ErkS.Studio;

/// <summary>What Studio knows about this device when it publishes the record.</summary>
internal sealed record StudioSsoIdentityInputs(
    string? SignedInEmail,
    string? SeatBotId,
    string? SeatOrganizationId,
    bool SeatUnlocked,
    string CanonicalFingerprint,
    string LegacyFingerprint,
    string? HandoffToken,
    DateTimeOffset NowUtc);

/// <summary>
/// Turns what Studio knows into the record the other products read.
///
/// Pure on purpose. The wiring that calls it runs on every account-UI refresh
/// and could not be tested without a window; this half is where the rules live
/// and it can be measured directly.
///
/// 🔴 THE SEAT WINS OVER THE PERSON, AND IT IS NOT A RANKING. A machine holding
/// an organisation's seat publishes the SEAT, even while an employee is signed
/// in with their own account - the same rule StudioEffectiveAuthority holds for
/// sessions, for the same reason. «If the seat does not grant it, fall back to
/// the person» sounds helpful and is the hole the whole seat model exists to
/// close: a person who is an admin in their own right must not become one on a
/// machine handed to them with a draughtsman's seat.
///
/// A consequence stated because it looks like a bug: on a seated machine a
/// plugin can resolve LESS than the person signed in beside it could. That is
/// the design working.
/// </summary>
internal static class StudioSsoIdentityPublisher
{
    /// <summary>
    /// How long the record stays usable when Studio is never opened again.
    ///
    /// Seven days, matching the offline grace the plugins already implement, so
    /// SSO does not quietly shorten what people have today. Offline is not what
    /// ends a licence - the licence's own expiry is; this is only how long a
    /// device may go without Studio confirming who it is.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <param name="generationFloor">
    /// The highest generation a reader could already be holding, when the stored
    /// record itself cannot be used. Zero when nothing is stored at all.
    /// </param>
    public static StudioSsoIdentityRecord Build(
        StudioSsoIdentityInputs inputs,
        StudioSsoIdentityRecord? previous,
        long generationFloor = 0)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var record = new StudioSsoIdentityRecord
        {
            FormatVersion = StudioSsoIdentityRecord.CurrentFormatVersion,
            Device = new StudioSsoDeviceFingerprints
            {
                CanonicalFingerprint = (inputs.CanonicalFingerprint ?? "").Trim(),
                LegacyFingerprint = (inputs.LegacyFingerprint ?? "").Trim(),
            },
            IssuedAtUtc = inputs.NowUtc.ToUniversalTime(),
            ExpiresAtUtc = inputs.NowUtc.ToUniversalTime() + Lifetime,
        };

        string botId = (inputs.SeatBotId ?? "").Trim();
        string organizationId = (inputs.SeatOrganizationId ?? "").Trim();
        string email = (inputs.SignedInEmail ?? "").Trim();

        if (botId.Length > 0)
        {
            record.BotId = botId;
            record.OrganizationId = organizationId;
            if (inputs.SeatUnlocked)
            {
                record.State = StudioSsoIdentityState.Active;
                record.IdentityKind = StudioSsoIdentityKind.Bot;

                // 🔴 THE PERSON'S ADDRESS IS DELIBERATELY LEFT OUT OF A BOT
                // RECORD. Somebody is often signed in beside the seat, and
                // writing their address into a record whose subject is the seat
                // is an invitation for a reader to use it - which is the exact
                // union this model refuses. The seat is the subject; who is
                // standing at the machine is the server's business through the
                // handoff token, not a field the plugins get to read.
                record.AccountEmail = "";
            }
            else
            {
                // Locked. The state and the organisation are readable so the
                // refusal can name the four keystrokes that fix it; nothing
                // that would let a plugin act as the seat is here.
                record.State = StudioSsoIdentityState.BotLocked;
                record.IdentityKind = "";
                record.AccountEmail = "";
            }
        }
        else if (email.Length > 0)
        {
            record.State = StudioSsoIdentityState.Active;
            record.IdentityKind = StudioSsoIdentityKind.Person;
            record.AccountEmail = email;
        }
        else
        {
            record.State = StudioSsoIdentityState.SignedOut;
        }

        // A token belongs to an active identity. Carrying one into SignedOut or
        // BotLocked would hand a reader a proof for an identity this record is
        // explicitly refusing to assert.
        string? token = (inputs.HandoffToken ?? "").Trim();
        record.HandoffToken =
            record.State == StudioSsoIdentityState.Active && token.Length > 0
                ? token
                : null;

        record.Generation = NextGeneration(record, previous, generationFloor);
        record.StateSignature = StudioSsoIdentitySignature.Compute(record);
        return record;
    }

    /// <summary>
    /// Moves only when the identity does - and never backwards.
    ///
    /// A record that fails its own signature is rewritten and counted as a
    /// change: it was hand-edited or carried here from another machine, and
    /// either way what a reader last saw is not what this device published.
    /// The number still advances from whatever that record claimed, because a
    /// counter that goes backwards tells a plugin holding the higher value that
    /// nothing has happened since.
    /// </summary>
    private static long NextGeneration(
        StudioSsoIdentityRecord next,
        StudioSsoIdentityRecord? previous,
        long generationFloor)
    {
        if (previous is null)
        {
            // 🔴 «NO USABLE RECORD» IS NOT «NO RECORD». A newer Studio may have
            // written format 2 at generation 50; this build cannot read it and
            // must not answer with 1, because a plugin holding 50 reads a lower
            // number as «nothing has happened since». The generation is the one
            // field whose meaning is fixed across formats, so it survives the
            // shape around it and becomes the floor.
            return Math.Max(1, generationFloor + 1);
        }

        bool trustworthy = StudioSsoIdentitySignature.Verify(previous);
        bool sameIdentity = previous.IdentityForm().Equals(
            next.IdentityForm(),
            StringComparison.Ordinal);
        bool sameFormat = previous.FormatVersion == next.FormatVersion;

        return trustworthy && sameIdentity && sameFormat
            ? Math.Max(1, previous.Generation)
            : Math.Max(1, previous.Generation) + 1;
    }

    /// <summary>
    /// Whether the stored record still says what this device would say now.
    /// Used to leave a good record alone instead of rewriting it on every menu
    /// click - the credential store is not free and a needless write is a
    /// needless chance to corrupt something that was right.
    ///
    /// The expiry is deliberately NOT compared for equality: it moves on every
    /// build. It is renewed only once the stored one has run down past the
    /// renewal point, which is what keeps a running Studio from rewriting the
    /// record every few seconds.
    /// </summary>
    public static bool NeedsRewrite(
        StudioSsoIdentityRecord next,
        StudioSsoIdentityRecord? previous,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (previous is null)
            return true;
        if (previous.FormatVersion != next.FormatVersion)
            return true;
        if (!StudioSsoIdentitySignature.Verify(previous))
            return true;
        if (!previous.IdentityForm().Equals(next.IdentityForm(), StringComparison.Ordinal))
            return true;
        if ((previous.HandoffToken ?? "") != (next.HandoffToken ?? ""))
            return true;

        // Renewed when a third of the life is left. Early enough that a person
        // who opens Studio once a week never meets the lapse, and late enough
        // that a machine left running does not rewrite the record all day.
        return previous.ExpiresAtUtc - nowUtc <= Lifetime / 3;
    }
}
