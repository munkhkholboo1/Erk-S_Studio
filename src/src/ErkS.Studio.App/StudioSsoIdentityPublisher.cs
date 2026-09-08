namespace ErkS.Studio;

/// <summary>
/// Who a handoff token was minted for.
///
/// 🔴 THE TOKEN IS OPAQUE, SO THE SCOPE HAS TO TRAVEL BESIDE IT. There are two
/// issue routes - one authenticated as the signed-in person, one as the
/// machine's own seat - and the bytes they return look identical here. Without
/// this the publisher could not tell a person's proof from a seat's, and the
/// union the seat model refuses would be one wrong call away: a seated machine
/// with somebody signed in beside it would publish THEIR token under the seat's
/// name and be answered with their entitlement.
/// </summary>
internal enum StudioSsoHandoffScope
{
    None,

    /// <summary>Minted on /sso/handoff-token, as the signed-in person.</summary>
    Person,

    /// <summary>Minted on /sso/seat-handoff-token, as this machine's seat.</summary>
    Seat,
}

/// <summary>Why a freshly minted token was or was not taken.</summary>
internal enum StudioSsoHandoffAcceptance
{
    /// <summary>Usable for the record it was asked for.</summary>
    Accepted,

    /// <summary>The server answered without a token. Its fault, and reported.</summary>
    NoToken,

    /// <summary>
    /// The identity moved while the request was in flight, so the token is bound
    /// to a number this record no longer carries. Not an error: the next publish
    /// asks again.
    /// </summary>
    GenerationMoved,
}

/// <summary>
/// Whether a minted token may be published against the record it was asked for.
///
/// 🔴 PULLED OUT BECAUSE IT WAS THE ONE BRANCH NO TEST COULD REACH. Inside the
/// service it sits behind an HTTP call that needs a live session, so it went
/// unmeasured while everything around it was covered - the shape of a check
/// that quietly stops checking. As a value it costs nothing to prove.
///
/// Three outcomes rather than a boolean, because two of them need different
/// handling: a server that answers without a token has failed and should say so,
/// while an identity that moved mid-flight is ordinary and silent.
/// </summary>
internal static class StudioSsoHandoffAcceptancePolicy
{
    public static StudioSsoHandoffAcceptance Of(
        string? issuedToken,
        long issuedGeneration,
        long recordGeneration)
    {
        if (string.IsNullOrWhiteSpace(issuedToken))
            return StudioSsoHandoffAcceptance.NoToken;

        // 🔴 THE ECHO IS CHECKED, NOT ASSUMED. A token bound to another
        // generation would be published against this record and refused by the
        // server later, with nothing on this side able to say why.
        return issuedGeneration == recordGeneration
            ? StudioSsoHandoffAcceptance.Accepted
            : StudioSsoHandoffAcceptance.GenerationMoved;
    }
}

/// <summary>Where a token of a given scope is minted, and with whose credential.</summary>
internal readonly record struct StudioSsoHandoffRoute(string Path, bool UsesSeatCredential);

/// <summary>
/// Which route mints which scope, as a value rather than as two branches at a
/// call site.
///
/// 🔴 THIS EXISTS BECAUSE A SABOTAGE SURVIVED. Forcing the seat path onto the
/// person's route left every test green: the publisher's scope check sees a
/// token LABELLED Seat and cannot tell that it was minted with the wrong
/// credential. That is the union the seat model refuses, reopened by one
/// character - and nothing said so.
///
/// Pairing the path with the credential in one value means the two cannot be
/// chosen separately, and the pairing itself is now something a test can read.
/// </summary>
internal static class StudioSsoHandoffRoutes
{
    public const string PersonPath = "/api/cloud-era/v1/sso/handoff-token";
    public const string SeatPath = "/api/cloud-era/v1/sso/seat-handoff-token";

    public static StudioSsoHandoffRoute For(StudioSsoHandoffScope scope) => scope switch
    {
        StudioSsoHandoffScope.Person => new StudioSsoHandoffRoute(PersonPath, false),
        StudioSsoHandoffScope.Seat => new StudioSsoHandoffRoute(SeatPath, true),
        _ => throw new InvalidOperationException(
            "A handoff token with no scope has no route: it could be minted as " +
            "either identity, and picking one would be a guess about who this " +
            "device is working as."),
    };
}

/// <summary>What Studio knows about this device when it publishes the record.</summary>
internal sealed record StudioSsoIdentityInputs(
    string? SignedInEmail,
    string? SeatBotId,
    string? SeatOrganizationId,
    bool SeatUnlocked,
    string CanonicalFingerprint,
    string LegacyFingerprint,
    string? HandoffToken,
    DateTimeOffset? HandoffTokenExpiresAtUtc,
    StudioSsoHandoffScope HandoffTokenScope,
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
        //
        // 🔴 AND THE SCOPE MUST MATCH THE SUBJECT. Two routes mint these now -
        // one as the signed-in person, one as the machine's own seat - and the
        // tokens are opaque, so nothing in the bytes says which. On a seated
        // machine somebody is often signed in beside the seat, and publishing
        // THEIR token under the seat's name would have a plugin present a
        // person's proof and be answered with the person's entitlement: the
        // exact union the seat model exists to refuse, reached by calling the
        // wrong method. Checked here rather than trusted at the call site,
        // because a call site is one edit away from being wrong.
        string token = (inputs.HandoffToken ?? "").Trim();
        DateTimeOffset? tokenExpires = inputs.HandoffTokenExpiresAtUtc;

        // 🔴 A PUBLISH WITH NO TOKEN IN HAND MUST NOT STRIP THE ONE ALREADY OUT
        // THERE. Studio holds the token in memory only, so after a restart it
        // has none - and rewriting the record without it would take a perfectly
        // good proof away from four products until a network call happened to
        // succeed. The published record is the token's home, so it is read back
        // from there.
        //
        // Only across the SAME identity, and only from a record that verifies. A
        // token names who it was minted for: keeping one across a sign-out or a
        // switch would publish one person's proof under another's name, and
        // accepting one from a record that fails its own signature would let
        // an edited file donate a token to a real identity.
        StudioSsoHandoffScope scope = inputs.HandoffTokenScope;
        if (token.Length == 0 &&
            previous is { HandoffToken.Length: > 0 } &&
            previous.ExpiresAtUtc > inputs.NowUtc &&
            previous.IdentityForm().Equals(record.IdentityForm(), StringComparison.Ordinal) &&
            StudioSsoIdentitySignature.Verify(previous))
        {
            token = previous.HandoffToken;

            // The stored expiry IS the token's expiry: a record carrying a token
            // was clamped to it when written, so reading it back recovers both
            // halves of one fact.
            tokenExpires = previous.ExpiresAtUtc;

            // A carried token was published under this same identity, so its
            // scope is whatever that identity requires. Derived rather than
            // remembered: the record has no field for it, and inventing one
            // would put a claim on disk that nothing checks.
            scope = record.IdentityKind == StudioSsoIdentityKind.Bot
                ? StudioSsoHandoffScope.Seat
                : StudioSsoHandoffScope.Person;
        }

        bool scopeMatchesSubject =
            (record.IdentityKind == StudioSsoIdentityKind.Person &&
             scope == StudioSsoHandoffScope.Person) ||
            (record.IdentityKind == StudioSsoIdentityKind.Bot &&
             scope == StudioSsoHandoffScope.Seat);
        bool tokenBelongsHere =
            record.State == StudioSsoIdentityState.Active &&
            scopeMatchesSubject &&
            token.Length > 0;
        record.HandoffToken = tokenBelongsHere ? token : null;

        // 🔴 ONE FACT, ONE LIFETIME. The token has an expiry of its own, and a
        // record that outlived the proof inside it would say «valid until
        // Tuesday» while the thing that makes it usable stopped on Sunday - a
        // reader would pass every check and then be refused by the server, with
        // nothing on this side saying why. So the record's life is the EARLIER
        // of the two. When the record is renewed the token is re-minted with it;
        // offline, both run down together, which is the intended «open Studio
        // once a week» rather than a second clock nobody is watching.
        if (tokenBelongsHere && tokenExpires is DateTimeOffset tokenExpiry)
        {
            DateTimeOffset limit = tokenExpiry.ToUniversalTime();
            if (limit < record.ExpiresAtUtc)
                record.ExpiresAtUtc = limit;
        }

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
        // Renewal counts only when it actually EXTENDS the record. A token that
        // cannot be re-minted - offline, say - leaves the clamped expiry where
        // it was, and without this the same bytes would be rewritten on every
        // account refresh for the last two days of the record's life.
        return previous.ExpiresAtUtc - nowUtc <= Lifetime / 3 &&
            next.ExpiresAtUtc > previous.ExpiresAtUtc;
    }
}
