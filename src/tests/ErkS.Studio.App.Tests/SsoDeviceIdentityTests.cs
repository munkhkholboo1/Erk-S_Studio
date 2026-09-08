using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The record the other four products read instead of signing in.
///
/// The user's decision: «PFA PFR CGA CGM бүгдэд бүртгэлээрээ нэвтрэх функц
/// байхгүй болно. зөвхөн студиод нэвтэрснээр лиценз таньдаг болох ёстой.»
///
/// 🔴 THESE TESTS GUARD A CONTRACT, NOT AN IMPLEMENTATION DETAIL. Four
/// repositories will read what this writes, and none of them has a sign-in to
/// fall back to when it changes shape. Every assertion here is something a
/// reader on the other side depends on.
/// </summary>
public sealed class SsoDeviceIdentityTests
{
    private const string Canonical = "SHA256:9F2CC41A";
    private const string Legacy = "SHA256:41AB770E";
    private const string Person = "owner@erk-s-design.mn";
    private const string BotId = "bot_7f3a91c4e85b4d2f";
    private const string OrgId = "org_2a91f4c7";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 11, 4, 22, TimeSpan.Zero);

    [Fact]
    public void ASIGNEDInPersonPublishesTheirOwnIdentity()
    {
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });

        Assert.Equal(StudioSsoIdentityState.Active, record.State);
        Assert.Equal(StudioSsoIdentityKind.Person, record.IdentityKind);
        Assert.Equal(Person, record.AccountEmail);
        Assert.Equal("", record.BotId);
        Assert.Equal(1, record.Generation);
    }

    [Fact]
    public void ALOCKEDSeatPublishesTheSTATEAndNothingToActOn()
    {
        // Named rather than absent. A plugin that finds no record has to tell
        // somebody to install Studio; one that finds this has to tell them to
        // type four digits. Collapsing the two sends people looking for a
        // sign-in dialog that no longer exists.
        StudioSsoIdentityRecord record = Build(Inputs() with
        {
            SignedInEmail = Person,
            SeatBotId = BotId,
            SeatOrganizationId = OrgId,
            SeatUnlocked = false,
        });

        Assert.Equal(StudioSsoIdentityState.BotLocked, record.State);
        Assert.Equal(OrgId, record.OrganizationId);
        Assert.Equal(BotId, record.BotId);
        Assert.Equal("", record.IdentityKind);
        Assert.Equal("", record.AccountEmail);
        Assert.Null(record.HandoffToken);
    }

    [Fact]
    public void ANUnlockedSeatPublishesTheSEATAndNOTThePersonBesideIt()
    {
        // 🔴 THE UNION THIS MODEL EXISTS TO REFUSE. Somebody is signed in with
        // their own account AND the machine holds an organisation's seat. The
        // record's subject is the seat; writing the person's address into it
        // would invite a reader to fall back on their rights, which is exactly
        // how a draughtsman's machine would start granting an admin's powers.
        StudioSsoIdentityRecord record = Build(Inputs() with
        {
            SignedInEmail = Person,
            SeatBotId = BotId,
            SeatOrganizationId = OrgId,
            SeatUnlocked = true,
        });

        Assert.Equal(StudioSsoIdentityState.Active, record.State);
        Assert.Equal(StudioSsoIdentityKind.Bot, record.IdentityKind);
        Assert.Equal(BotId, record.BotId);
        Assert.Equal("", record.AccountEmail);
    }

    [Fact]
    public void NOBODYSignedInIsWrittenDownRatherThanLeftBlank()
    {
        // «No record» and «nobody signed in» get different messages, so they
        // have to be different states. If signing out deleted the record the
        // two would be indistinguishable and one message would be wrong every
        // time it was shown.
        StudioSsoIdentityRecord record = Build(Inputs());

        Assert.Equal(StudioSsoIdentityState.SignedOut, record.State);
        Assert.Equal("", record.IdentityKind);
        Assert.Equal("", record.AccountEmail);
        Assert.Null(record.HandoffToken);
    }

    [Fact]
    public void ATOKENIsOmittedRatherThanWrittenEmpty()
    {
        // 🔴 ABSENCE MUST NOT BE A VALUE. A reader that finds "" has to decide
        // whether a blank proof is a proof; a reader that finds no property at
        // all has nothing to decide. The distinction is the difference between
        // «no way to prove this identity yet» and «proof supplied, and empty».
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });
        string json = JsonSerializer.Serialize(
            record,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("handoffToken", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATOKENIsDroppedWhenTheIdentityIsNotAsserted()
    {
        // Carrying a proof into a state that refuses to name an identity would
        // hand a reader the means to act as something this record is explicitly
        // declining to claim.
        StudioSsoIdentityRecord locked = Build(Inputs() with
        {
            SeatBotId = BotId,
            SeatOrganizationId = OrgId,
            SeatUnlocked = false,
            HandoffToken = "handoff-abc",
        });
        StudioSsoIdentityRecord signedOut = Build(Inputs() with { HandoffToken = "handoff-abc" });
        StudioSsoIdentityRecord unlockedSeat = Build(Inputs() with
        {
            SignedInEmail = Person,
            SeatBotId = BotId,
            SeatOrganizationId = OrgId,
            SeatUnlocked = true,
            HandoffToken = "handoff-abc",
        });
        StudioSsoIdentityRecord active = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
        });

        Assert.Null(locked.HandoffToken);
        Assert.Null(signedOut.HandoffToken);
        Assert.Equal("handoff-abc", active.HandoffToken);

        // 🔴 THE SHARP ONE. An UNLOCKED seat is Active, so the state test alone
        // lets a token through - and a PERSON-scoped token there is whoever is
        // signed in beside the seat. A plugin presenting it would be answered
        // with THAT PERSON's entitlement while the record says Bot: the union
        // the seat model exists to refuse, reached by calling the wrong route.
        Assert.Null(unlockedSeat.HandoffToken);
    }

    [Fact]
    public void ASEATScopedTokenISPublishedOnASeatRecord()
    {
        // The positive control for the refusal above, and the thing that stops
        // it from being «bot records never carry tokens» - which was true only
        // while the server had no seat route. The seat's own credential mints
        // this one, so it names the SEAT and belongs here.
        StudioSsoIdentityRecord record = Build(Inputs() with
        {
            SignedInEmail = Person,
            SeatBotId = BotId,
            SeatOrganizationId = OrgId,
            SeatUnlocked = true,
            HandoffToken = "seat-handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(5),
            HandoffTokenScope = StudioSsoHandoffScope.Seat,
        });

        Assert.Equal(StudioSsoIdentityKind.Bot, record.IdentityKind);
        Assert.Equal("seat-handoff-abc", record.HandoffToken);
        Assert.Equal("", record.AccountEmail);
    }

    [Fact]
    public void ASEATScopedTokenIsREFUSEDOnAPersonRecord()
    {
        // The mirror, and it is not symmetry for its own sake: a seat token
        // names an organisation's seat, so publishing it under a person's record
        // would have a plugin resolve the ORGANISATION's entitlement for
        // somebody working under their own licence.
        StudioSsoIdentityRecord record = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "seat-handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(5),
            HandoffTokenScope = StudioSsoHandoffScope.Seat,
        });

        Assert.Equal(StudioSsoIdentityKind.Person, record.IdentityKind);
        Assert.Null(record.HandoffToken);
    }

    [Fact]
    public void ATOKENWithNOScopeIsRefusedEverywhere()
    {
        // The scope is how the publisher tells one route's token from the
        // other's; a token that arrived without one cannot be placed, and
        // guessing would be exactly the mistake this field exists to prevent.
        StudioSsoIdentityRecord person = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
            HandoffTokenScope = StudioSsoHandoffScope.None,
        });

        Assert.Null(person.HandoffToken);
    }

    [Fact]
    public void ATOKENSLifeCapsTheRecordsLife()
    {
        // 🔴 ONE FACT, ONE LIFETIME. A record outliving the proof inside it says
        // «valid until Tuesday» while the thing that makes it usable stopped on
        // Sunday - a reader passes every check on this side and is refused by
        // the server, with nothing here able to say why.
        StudioSsoIdentityRecord record = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(2),
        });

        Assert.Equal(Now.AddDays(2), record.ExpiresAtUtc);
    }

    [Fact]
    public void ALONGERLivedTokenDoesNotExtendTheRecord()
    {
        // The cap is a floor-and-ceiling question and only one direction is
        // right. A token good for a year does not make the record good for a
        // year: the record's own life is what says how long this device may go
        // without Studio confirming who it is.
        StudioSsoIdentityRecord record = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(365),
        });

        Assert.Equal(Now + StudioSsoIdentityPublisher.Lifetime, record.ExpiresAtUtc);
    }

    [Fact]
    public void REPUBLISHINGTheSameIdentityDoesNotMoveTheGeneration()
    {
        // The generation is what a plugin watches to know its cached
        // entitlement went stale. Studio republishes on every account-UI
        // refresh, so a counter that moved each time would tell four products
        // to re-resolve because somebody opened a menu.
        StudioSsoIdentityInputs inputs = Inputs() with { SignedInEmail = Person };
        StudioSsoIdentityRecord first = Build(inputs);
        StudioSsoIdentityRecord second = StudioSsoIdentityPublisher.Build(
            inputs with { NowUtc = Now.AddMinutes(30) },
            first);

        Assert.Equal(first.Generation, second.Generation);
    }

    [Fact]
    public void EVERYIdentityChangeMovesTheGeneration()
    {
        // The positive control for the test above, and the reason it is safe:
        // «it did not move» is only meaningful beside a case where it must.
        StudioSsoIdentityRecord signedOut = Build(Inputs());
        StudioSsoIdentityRecord signedIn = StudioSsoIdentityPublisher.Build(
            Inputs() with { SignedInEmail = Person },
            signedOut);
        StudioSsoIdentityRecord seated = StudioSsoIdentityPublisher.Build(
            Inputs() with
            {
                SignedInEmail = Person,
                SeatBotId = BotId,
                SeatOrganizationId = OrgId,
                SeatUnlocked = true,
            },
            signedIn);
        StudioSsoIdentityRecord locked = StudioSsoIdentityPublisher.Build(
            Inputs() with
            {
                SignedInEmail = Person,
                SeatBotId = BotId,
                SeatOrganizationId = OrgId,
                SeatUnlocked = false,
            },
            seated);

        Assert.Equal(1, signedOut.Generation);
        Assert.Equal(2, signedIn.Generation);
        Assert.Equal(3, seated.Generation);
        Assert.Equal(4, locked.Generation);
    }

    [Fact]
    public void ATAMPEREDRecordIsRewrittenAndTheCounterStillGoesFORWARD()
    {
        // A record edited by hand, or carried here from another machine, is not
        // what this device published - so it is replaced. The number still
        // advances from whatever that record claimed, because a plugin holding
        // the higher value would read a lower one as «nothing has happened».
        StudioSsoIdentityRecord forged = Build(Inputs() with { SignedInEmail = Person });
        forged.Generation = 41;
        forged.AccountEmail = "someone.else@example.com";

        StudioSsoIdentityRecord rewritten = StudioSsoIdentityPublisher.Build(
            Inputs() with { SignedInEmail = Person },
            forged);

        Assert.Equal(42, rewritten.Generation);
        Assert.Equal(Person, rewritten.AccountEmail);
        Assert.True(StudioSsoIdentitySignature.Verify(rewritten));
    }

    [Fact]
    public void THESignatureCoversTheFieldsAReaderActsOn()
    {
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });
        Assert.True(StudioSsoIdentitySignature.Verify(record));

        record.State = StudioSsoIdentityState.Active;
        record.IdentityKind = StudioSsoIdentityKind.Bot;
        Assert.False(StudioSsoIdentitySignature.Verify(record));
    }

    [Fact]
    public void EDITINGTheFingerprintBreaksTheSignature()
    {
        // What the tripwire genuinely stops: somebody changing a field by hand.
        // Every field the canonical form covers behaves this way; the fingerprint
        // is asserted because it is the one people reach for first.
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });
        record.Device.CanonicalFingerprint = "SHA256:0000FFFF";

        Assert.False(StudioSsoIdentitySignature.Verify(record));
    }

    [Fact]
    public void AVERBATIMCopyOnAnotherMachineVerifiesPERFECTLY()
    {
        // 🔴 THIS TEST EXISTS TO STOP A CLAIM, NOT A BUG. The contract said - and
        // the type note beside this signature said - that it catches a record
        // being copied to another machine. It does not, and CGM measured why:
        // the key is derived from the fingerprint that travels INSIDE the
        // record, so a copy is self-consistent and passes.
        //
        // A false assurance is worse than none, because somebody reads it and
        // stops adding the protection they would otherwise have added. What
        // actually stops a copied record from being useful is the server: the
        // token carries a device claim checked with the server's own secret.
        //
        // Asserted as TRUE rather than deleted, so the claim cannot come back
        // into the documentation without a red test underneath it.
        StudioSsoIdentityRecord onThisMachine =
            Build(Inputs() with { SignedInEmail = Person, HandoffToken = "handoff-abc" });

        var copiedElsewhere = new StudioSsoIdentityRecord
        {
            FormatVersion = onThisMachine.FormatVersion,
            Generation = onThisMachine.Generation,
            State = onThisMachine.State,
            IdentityKind = onThisMachine.IdentityKind,
            AccountEmail = onThisMachine.AccountEmail,
            BotId = onThisMachine.BotId,
            OrganizationId = onThisMachine.OrganizationId,
            Device = new StudioSsoDeviceFingerprints
            {
                CanonicalFingerprint = onThisMachine.Device.CanonicalFingerprint,
                LegacyFingerprint = onThisMachine.Device.LegacyFingerprint,
            },
            HandoffToken = onThisMachine.HandoffToken,
            IssuedAtUtc = onThisMachine.IssuedAtUtc,
            ExpiresAtUtc = onThisMachine.ExpiresAtUtc,
            StateSignature = onThisMachine.StateSignature,
        };

        Assert.True(
            StudioSsoIdentitySignature.Verify(copiedElsewhere),
            "if this ever goes red the signature gained a machine binding and the " +
            "contract's wording has to be revisited - in the good direction");
    }

    [Fact]
    public void AFreshRecordIsLEFTAloneAndAStaleOneIsRenewed()
    {
        StudioSsoIdentityInputs inputs = Inputs() with { SignedInEmail = Person };
        StudioSsoIdentityRecord stored = Build(inputs);

        StudioSsoIdentityRecord soon = StudioSsoIdentityPublisher.Build(
            inputs with { NowUtc = Now.AddHours(1) },
            stored);
        Assert.False(StudioSsoIdentityPublisher.NeedsRewrite(soon, stored, Now.AddHours(1)));

        DateTimeOffset late = Now + StudioSsoIdentityPublisher.Lifetime -
            (StudioSsoIdentityPublisher.Lifetime / 4);
        StudioSsoIdentityRecord renewed = StudioSsoIdentityPublisher.Build(
            inputs with { NowUtc = late },
            stored);
        Assert.True(StudioSsoIdentityPublisher.NeedsRewrite(renewed, stored, late));
    }

    [Fact]
    public void ATOKENArrivingIsAReasonToRewrite()
    {
        // The day the server starts minting handoff tokens, the identity has not
        // changed and the record still has to be republished - otherwise the
        // token sits in Studio and never reaches the products that need it.
        StudioSsoIdentityInputs inputs = Inputs() with { SignedInEmail = Person };
        StudioSsoIdentityRecord stored = Build(inputs);
        StudioSsoIdentityRecord withToken = StudioSsoIdentityPublisher.Build(
            inputs with { HandoffToken = "handoff-abc" },
            stored);

        Assert.True(StudioSsoIdentityPublisher.NeedsRewrite(withToken, stored, Now));
        Assert.Equal(stored.Generation, withToken.Generation);
    }

    [Fact]
    public void BOTHFingerprintsTravelInEveryRecord()
    {
        // One machine, two valid values. A reader holding only one proves
        // nothing about a record stored under the other, and Studio has already
        // paid once for the day the canonical form moved.
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });

        Assert.Equal(Canonical, record.Device.CanonicalFingerprint);
        Assert.Equal(Legacy, record.Device.LegacyFingerprint);
    }

    [Fact]
    public void ARecordFromAnUNKNOWNShapeIsNotRead()
    {
        // Out of range means «republish», not «trust it anyway». Applying this
        // build's meaning to a shape it does not know is the quiet half of every
        // format break.
        var store = new FakeCredentialStore();
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });
        record.FormatVersion = StudioSsoIdentityRecord.CurrentFormatVersion + 1;
        StudioSsoIdentityStore.Write(store, record);

        StudioSsoIdentityReadResult result = StudioSsoIdentityStore.Read(store);
        Assert.Equal(StudioSsoIdentityReadOutcome.Unreadable, result.Outcome);
        Assert.Null(result.Record);
    }

    [Fact]
    public void ANEWERStudiosRecordDoesNotRestartTheCounter()
    {
        // 🔴 THE COUNTER IS THE ONE THING THAT MAY NEVER GO BACKWARDS. A newer
        // Studio writes format 2 at generation 50; this build cannot read the
        // shape and used to answer with 1 - and a plugin holding 50 reads any
        // lower number as «nothing has happened since», so it would keep serving
        // an entitlement that had already been replaced. The generation is the
        // one field whose meaning is fixed across formats, so it survives.
        var store = new FakeCredentialStore();
        StudioSsoIdentityRecord fromTheFuture = Build(Inputs() with { SignedInEmail = Person });
        fromTheFuture.FormatVersion = StudioSsoIdentityRecord.CurrentFormatVersion + 1;
        fromTheFuture.Generation = 50;
        StudioSsoIdentityStore.Write(store, fromTheFuture);

        StudioSsoIdentityReadResult result = StudioSsoIdentityStore.Read(store);
        StudioSsoIdentityRecord next = StudioSsoIdentityPublisher.Build(
            Inputs() with { SignedInEmail = Person },
            result.Record,
            result.GenerationFloor);

        Assert.Equal(50, result.GenerationFloor);
        Assert.Equal(51, next.Generation);
    }

    [Fact]
    public void ASTOREThatCannotBeReadIsNOTWrittenOver()
    {
        // Nothing is known about what an unreachable store holds - possibly a
        // good record at a much higher generation. Overwriting it would be
        // guessing with the value four products depend on, so the publish
        // refuses and says so instead.
        //
        // 🔴 THE FAKE READS BY THROWING AND WRITES BY SUCCEEDING, AND THAT IS
        // THE WHOLE TEST. A store that failed both ways would return false with
        // or without the guard - the assertion would pass while proving
        // nothing, because the write it is supposed to prevent was going to
        // fail anyway. The case that matters is the transient read failure over
        // a store that would happily accept the clobbering write.
        var store = new ReadThrowsWriteSucceedsCredentialStore();
        StudioSsoIdentityRecord existing = Build(Inputs() with { SignedInEmail = Person });
        existing.Generation = 50;
        store.Seed(StudioSsoIdentityStore.CredentialTarget, existing);
        var account = new StudioAccountService(store);

        Assert.False(account.PublishSsoIdentity(null, null, seatUnlocked: false));
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void AWORKINGStoreIsActuallyPublishedTo()
    {
        // The positive control for the test above. Without it «returned false»
        // would prove only that the method can return false.
        var store = new FakeCredentialStore();
        var account = new StudioAccountService(store);

        Assert.True(account.PublishSsoIdentity(null, null, seatUnlocked: false));

        StudioSsoIdentityReadResult result = StudioSsoIdentityStore.Read(store);
        Assert.Equal(StudioSsoIdentityReadOutcome.Found, result.Outcome);
        Assert.Equal(StudioSsoIdentityState.SignedOut, result.Record!.State);
        Assert.True(StudioSsoIdentitySignature.Verify(result.Record));
    }

    [Fact]
    public void THERecordSurvivesTheStoreRoundTrip()
    {
        // The positive control for the test above: without it, «returned null»
        // would prove only that the store never returns anything.
        var store = new FakeCredentialStore();
        StudioSsoIdentityRecord written = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
        });

        Assert.True(StudioSsoIdentityStore.Write(store, written));
        StudioSsoIdentityReadResult result = StudioSsoIdentityStore.Read(store);
        StudioSsoIdentityRecord? read = result.Record;

        Assert.Equal(StudioSsoIdentityReadOutcome.Found, result.Outcome);
        Assert.NotNull(read);
        Assert.Equal(written.State, read!.State);
        Assert.Equal(written.AccountEmail, read.AccountEmail);
        Assert.Equal(written.HandoffToken, read.HandoffToken);
        Assert.Equal(written.Generation, read.Generation);
        Assert.True(StudioSsoIdentitySignature.Verify(read));
    }

    [Fact]
    public void ASTOREThatThrowsReadsAsEmptyRatherThanTakingTheUIDown()
    {
        // The caller is the account refresh - the one screen a person needs when
        // their identity is in doubt. A credential store that cannot be reached
        // must rebuild the record, not close the window.
        var store = new ThrowingCredentialStore();

        // 🔴 AND IT IS ITS OWN OUTCOME, NOT «EMPTY». PFR had folded «the record
        // will not parse» and «the store cannot be reached» into one refusal,
        // and Master split them because the person's next step differs: opening
        // Studio repairs a bad record and does nothing at all for a store this
        // account cannot read. The same split has to exist on the writing side,
        // where one of the two means «do not write».
        StudioSsoIdentityReadResult result = StudioSsoIdentityStore.Read(store);
        Assert.Equal(StudioSsoIdentityReadOutcome.StoreUnavailable, result.Outcome);
        Assert.Null(result.Record);

        Assert.False(StudioSsoIdentityStore.Write(
            store,
            Build(Inputs() with { SignedInEmail = Person })));
    }

    [Fact]
    public void APUBLISHEDTokenSurvivesAPublishThatHasNone()
    {
        // 🔴 STUDIO HOLDS THE TOKEN IN MEMORY ONLY, so after a restart it has
        // none - and rewriting the record without it would take a perfectly good
        // proof away from four products until a network call happened to
        // succeed. The published record is the token's home and it is read back
        // from there.
        StudioSsoIdentityInputs signedIn = Inputs() with { SignedInEmail = Person };
        StudioSsoIdentityRecord published = Build(signedIn with
        {
            HandoffToken = "handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(5),
        });

        // Republished with nothing in hand - the shape of the next launch,
        // where the scope is unknown because the token is not held any more.
        StudioSsoIdentityRecord afterRestart = StudioSsoIdentityPublisher.Build(
            signedIn with
            {
                NowUtc = Now.AddHours(2),
                HandoffTokenScope = StudioSsoHandoffScope.None,
            },
            published);

        Assert.Equal("handoff-abc", afterRestart.HandoffToken);
        Assert.Equal(published.Generation, afterRestart.Generation);
        Assert.Equal(Now.AddDays(5), afterRestart.ExpiresAtUtc);
    }

    [Fact]
    public void ATOKENIsNOTCarriedAcrossAChangeOfIdentity()
    {
        // A token names who it was minted for. Keeping one when the person
        // changes would publish one person's proof under another's name - and
        // the server, asked to resolve it, would answer for whoever it was
        // minted for.
        StudioSsoIdentityRecord published = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(5),
        });

        StudioSsoIdentityRecord somebodyElse = StudioSsoIdentityPublisher.Build(
            Inputs() with { SignedInEmail = "second@erk-s-design.mn" },
            published);
        StudioSsoIdentityRecord signedOut = StudioSsoIdentityPublisher.Build(
            Inputs(),
            published);

        Assert.Null(somebodyElse.HandoffToken);
        Assert.Null(signedOut.HandoffToken);
    }

    [Fact]
    public void AFORGEDRecordCannotDonateAToken()
    {
        // The carry-forward reads a token out of a file. Accepting one from a
        // record that fails its own signature would let an edited file hand a
        // proof to a real identity - the one thing the signature is there to
        // stop, undone by the convenience beside it.
        StudioSsoIdentityRecord published = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(5),
        });
        published.StateSignature = "not-a-signature";

        StudioSsoIdentityRecord next = StudioSsoIdentityPublisher.Build(
            Inputs() with { SignedInEmail = Person },
            published);

        Assert.Null(next.HandoffToken);
    }

    [Fact]
    public void ANEXPIREDPublishedTokenIsNotCarried()
    {
        // The record and the token run out together by construction, so an
        // expired stored record is an expired token. Carrying it would publish
        // a proof the server has already stopped accepting.
        StudioSsoIdentityRecord published = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
            HandoffTokenExpiresAtUtc = Now.AddDays(2),
        });

        StudioSsoIdentityRecord next = StudioSsoIdentityPublisher.Build(
            Inputs() with { SignedInEmail = Person, NowUtc = Now.AddDays(3) },
            published);

        Assert.Null(next.HandoffToken);
    }

    [Fact]
    public void ASTATEThisBuildDoesNotRecogniseIsUnreadable()
    {
        // The rule for this existed as a method and nothing called it. It is not
        // cosmetic: a record whose state means nothing here would otherwise be
        // trusted to donate its token and to be the base the generation counts
        // from, on the strength of a word this build has never heard of.
        var store = new FakeCredentialStore();
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });
        record.State = "Suspended";
        record.Generation = 12;
        StudioSsoIdentityStore.Write(store, record);

        StudioSsoIdentityReadResult result = StudioSsoIdentityStore.Read(store);

        Assert.Equal(StudioSsoIdentityReadOutcome.Unreadable, result.Outcome);
        Assert.Null(result.Record);
        Assert.Equal(12, result.GenerationFloor);
    }

    [Fact]
    public void ANEmptyStoreIsNOTFoundRatherThanUnreadable()
    {
        // The fourth outcome, and the one that decides whether the counter
        // starts at 1. Collapsing it into «unreadable» would be harmless today
        // and would quietly disable the floor above.
        StudioSsoIdentityReadResult result =
            StudioSsoIdentityStore.Read(new FakeCredentialStore());

        Assert.Equal(StudioSsoIdentityReadOutcome.NotFound, result.Outcome);
        Assert.Equal(0, result.GenerationFloor);
    }

    [Fact]
    public void THECredentialTargetIsTheONEPublishedName()
    {
        // Four other repositories read this string. It is asserted here so that
        // changing it cannot happen quietly - the test is the announcement.
        Assert.Equal(
            "Erk-S Platform/SSO/Device Identity",
            StudioSsoIdentityStore.CredentialTarget);
    }

    [Fact]
    public void THERecordSurvivesTheREALCredentialManager()
    {
        // 🔴 THE FAKE PROVES THE RULES AND NOTHING ABOUT THE STORE. Every test
        // above runs against a dictionary, so all of them would stay green if
        // the record could not actually be written to Windows - which is the one
        // failure that stops four products and shows up on nobody's screen.
        //
        // Written to a scratch target and removed again, so a test run never
        // disturbs the entry Studio actually publishes. The target is the only
        // thing changed: the payload, the serializer and the native calls are
        // the ones the real path uses.
        string target = "Erk-S Platform/SSO/Device Identity (test " +
            Guid.NewGuid().ToString("N") + ")";
        StudioSsoIdentityRecord written = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
        });

        try
        {
            WindowsCredentialVault.Write(target, "Erk-S Studio", written);
            StudioSsoIdentityRecord? read =
                WindowsCredentialVault.Read<StudioSsoIdentityRecord>(target);

            Assert.NotNull(read);
            Assert.Equal(written.State, read!.State);
            Assert.Equal(written.AccountEmail, read.AccountEmail);
            Assert.Equal(written.HandoffToken, read.HandoffToken);
            Assert.Equal(written.Generation, read.Generation);

            // The signature travelling intact is the part a reader in another
            // product depends on: the blob is UTF-16 through a native API, and a
            // round trip that changed one byte of base64 would be invisible
            // until four plugins started refusing a record that looks correct.
            Assert.Equal(written.StateSignature, read.StateSignature);
            Assert.True(StudioSsoIdentitySignature.Verify(read));
        }
        finally
        {
            try
            {
                WindowsCredentialVault.Delete(target);
            }
            catch (Exception)
            {
                // Cleanup only. A failure to remove a scratch entry must not
                // mask what the assertions above already decided.
            }
        }
    }

    [Fact]
    public void AMINTEDTokenIsJudgedAgainstTheRecordItWasAskedFor()
    {
        // 🔴 THIS WAS THE ONE BRANCH NO TEST COULD REACH. It sat behind an HTTP
        // call needing a live session, so it went unmeasured while everything
        // around it was covered - a check that had quietly stopped being
        // checked. I named the gap rather than hiding it; this closes it.
        Assert.Equal(
            StudioSsoHandoffAcceptance.Accepted,
            StudioSsoHandoffAcceptancePolicy.Of("token", 7, 7));

        // The identity moved while the request was in flight. The token names a
        // generation this record no longer carries, so publishing it would put a
        // proof on disk that the server will refuse later - with nothing on this
        // side able to say why.
        Assert.Equal(
            StudioSsoHandoffAcceptance.GenerationMoved,
            StudioSsoHandoffAcceptancePolicy.Of("token", 6, 7));

        // Separate from the above because they are handled differently: a server
        // that answers without a token has failed and says so, while a moved
        // identity is ordinary and silent. One boolean would have merged a fault
        // with a normal race.
        Assert.Equal(
            StudioSsoHandoffAcceptance.NoToken,
            StudioSsoHandoffAcceptancePolicy.Of("", 7, 7));
        Assert.Equal(
            StudioSsoHandoffAcceptance.NoToken,
            StudioSsoHandoffAcceptancePolicy.Of(null, 7, 7));

        // An absent token is reported as absent even when the generation is also
        // wrong: the first thing that failed is the thing to say.
        Assert.Equal(
            StudioSsoHandoffAcceptance.NoToken,
            StudioSsoHandoffAcceptancePolicy.Of(null, 6, 7));
    }

    [Fact]
    public void THEMINTINGCallAsksTheAcceptancePolicy()
    {
        // The seam still has no injection point, so the wiring is read from
        // source - narrowly, and only for the call that replaced the inline
        // branch this test exists to have made measurable.
        string service = ReadAppSource("StudioAccountService.cs");
        int method = service.IndexOf(
            "public async Task<bool> EnsureSsoHandoffTokenAsync(",
            StringComparison.Ordinal);
        string body = service[method..service.IndexOf("\n    }", method, StringComparison.Ordinal)];

        Assert.Contains("StudioSsoHandoffAcceptancePolicy.Of(", body, StringComparison.Ordinal);
        Assert.Contains("StudioSsoHandoffAcceptance.NoToken", body, StringComparison.Ordinal);
        Assert.Contains("StudioSsoHandoffAcceptance.GenerationMoved", body, StringComparison.Ordinal);
    }

    [Fact]
    public void EACHScopeIsMintedOnItsOwnRouteWithItsOwnCredential()
    {
        // 🔴 WRITTEN AFTER A SABOTAGE SURVIVED. Forcing the seat path onto the
        // person's route left every test green: the publisher sees a token
        // LABELLED Seat and has no way to know it was minted as whoever was
        // signed in beside the machine. The union the seat model refuses was one
        // character away and nothing said so.
        //
        // Pairing the path with the credential in one value is what makes the
        // choice measurable at all - they can no longer be picked separately.
        StudioSsoHandoffRoute person = StudioSsoHandoffRoutes.For(StudioSsoHandoffScope.Person);
        StudioSsoHandoffRoute seat = StudioSsoHandoffRoutes.For(StudioSsoHandoffScope.Seat);

        Assert.Equal("/api/cloud-era/v1/sso/handoff-token", person.Path);
        Assert.False(person.UsesSeatCredential);

        Assert.Equal("/api/cloud-era/v1/sso/seat-handoff-token", seat.Path);
        Assert.True(seat.UsesSeatCredential);

        // Neither may borrow the other's credential.
        Assert.NotEqual(person.Path, seat.Path);
        Assert.NotEqual(person.UsesSeatCredential, seat.UsesSeatCredential);
    }

    [Fact]
    public void ASCOPELESSTokenHasNoRouteAtALL()
    {
        // Defaulting to one of the two would be a guess about who this device is
        // working as, made at the moment there is least reason to guess.
        Assert.Throws<InvalidOperationException>(
            () => StudioSsoHandoffRoutes.For(StudioSsoHandoffScope.None));
    }

    [Fact]
    public void THEMINTINGCallTakesBothHalvesFromTheSAMERoute()
    {
        // The seam has no injection point - the two posts are private members of
        // a service that needs a live session - so this is read from source, and
        // it is read narrowly: the credential branch must be the route's own
        // flag, and both posts must take the route's own path. That is precisely
        // the pair the surviving sabotage broke.
        string service = ReadAppSource("StudioAccountService.cs");
        int method = service.IndexOf(
            "public async Task<bool> EnsureSsoHandoffTokenAsync(",
            StringComparison.Ordinal);
        Assert.True(method > 0, "the token method was not found");
        string body = service[method..service.IndexOf("\n    }", method, StringComparison.Ordinal)];

        Assert.Contains(
            "StudioSsoHandoffRoutes.For(scope)",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "issued = route.UsesSeatCredential",
            body,
            StringComparison.Ordinal);

        // Both calls address the route rather than a literal, so the path and
        // the credential cannot disagree.
        Assert.Equal(2, Occurrences(body, "route.Path,"));
        Assert.DoesNotContain("\"/api/cloud-era/v1/sso/", body, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    [Fact]
    public void THEIdentityIsPublishedWhereBOTHHalvesAreVisible()
    {
        // 🔴 THE HALF THAT KEEPS GOING MISSING IN THIS CODEBASE IS THE CALLER.
        // The rules above are pure and provable; what makes them reach the disk
        // is one call, in the one method that sees the signed-in person AND the
        // machine's seat at the same time. Publishing from either side alone
        // would keep overwriting the other, which is the same shape as the seat
        // that stopped receiving whenever an employee signed in.
        string view = ReadAppSource("ShellView.cs");
        int method = view.IndexOf("private void UpdateAccountUi()", StringComparison.Ordinal);
        Assert.True(method > 0, "UpdateAccountUi was not found");
        string body = view[method..view.IndexOf("\n    }", method, StringComparison.Ordinal)];

        Assert.Contains("account.PublishSsoIdentity(", body, StringComparison.Ordinal);

        // The seat's LOCK is what decides whether an identity is asserted at
        // all, so the lock state has to be what is passed. Passing the seat's
        // mere existence would publish a locked machine as an active bot.
        Assert.Contains("unlockedSeatIdentity is not null", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AFAILEDPublishIsReportedOnceRatherThanSwallowed()
    {
        // A device that cannot write its identity is one where four products
        // will refuse for no visible reason. Said on the way down, and not on
        // every refresh - a message repeated on every menu click is one people
        // learn to scroll past.
        string view = ReadAppSource("ShellView.cs");
        int method = view.IndexOf("private void UpdateAccountUi()", StringComparison.Ordinal);
        string body = view[method..view.IndexOf("\n    }", method, StringComparison.Ordinal)];

        Assert.Contains("!ssoPublished && ssoIdentityPublished", body, StringComparison.Ordinal);
        Assert.Contains("ssoIdentityPublished = ssoPublished;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEPublisherWritesOnlyWhenTheRecordActuallyNeedsIt()
    {
        // The account UI refreshes on menu clicks and project switches. Writing
        // the credential every time is a needless chance to corrupt something
        // that was already right, and it is the reason the rewrite test exists
        // at all.
        string service = ReadAppSource("StudioAccountService.cs");
        int method = service.IndexOf(
            "public bool PublishSsoIdentity(",
            StringComparison.Ordinal);
        Assert.True(method > 0, "PublishSsoIdentity was not found");
        string body = service[method..service.IndexOf("\n    }", method, StringComparison.Ordinal)];

        Assert.Contains("StudioSsoIdentityStore.Read(credentialStore)", body, StringComparison.Ordinal);
        Assert.Contains("StudioSsoIdentityPublisher.NeedsRewrite(", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("NeedsRewrite(", StringComparison.Ordinal) <
                body.IndexOf("StudioSsoIdentityStore.Write(", StringComparison.Ordinal),
            "the rewrite check must come before the write, or it decides nothing");
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, System.Text.Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }

    private static StudioSsoIdentityInputs Inputs() => new(
        SignedInEmail: null,
        SeatBotId: null,
        SeatOrganizationId: null,
        SeatUnlocked: false,
        CanonicalFingerprint: Canonical,
        LegacyFingerprint: Legacy,
        HandoffToken: null,
        HandoffTokenExpiresAtUtc: null,
        // Person by default because most of these are; the cases that matter
        // set it deliberately, and a scope with no token beside it decides
        // nothing.
        HandoffTokenScope: StudioSsoHandoffScope.Person,
        NowUtc: Now);

    private static StudioSsoIdentityRecord Build(StudioSsoIdentityInputs inputs) =>
        StudioSsoIdentityPublisher.Build(inputs, previous: null);

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

        public T? Read<T>(string target) where T : class =>
            entries.TryGetValue(target, out string? json)
                ? JsonSerializer.Deserialize<T>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : null;

        public void Write<T>(string target, string userName, T value) =>
            entries[target] = JsonSerializer.Serialize(
                value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        public void Delete(string target) => entries.Remove(target);
    }

    /// <summary>
    /// Reads by throwing, writes by succeeding. The shape of a transient store
    /// failure, and the only shape in which the do-not-overwrite guard is
    /// observable at all.
    /// </summary>
    private sealed class ReadThrowsWriteSucceedsCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

        public int WriteCount { get; private set; }

        public void Seed<T>(string target, T value) =>
            entries[target] = JsonSerializer.Serialize(
                value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        public T? Read<T>(string target) where T : class =>
            throw new InvalidOperationException("credential store temporarily unavailable");

        public void Write<T>(string target, string userName, T value)
        {
            WriteCount++;
            Seed(target, value);
        }

        public void Delete(string target) => entries.Remove(target);
    }

    private sealed class ThrowingCredentialStore : ICredentialStore
    {
        public T? Read<T>(string target) where T : class =>
            throw new InvalidOperationException("credential store unavailable");

        public void Write<T>(string target, string userName, T value) =>
            throw new InvalidOperationException("credential store unavailable");

        public void Delete(string target) =>
            throw new InvalidOperationException("credential store unavailable");
    }
}
