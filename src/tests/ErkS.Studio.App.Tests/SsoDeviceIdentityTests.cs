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
        StudioSsoIdentityRecord active = Build(Inputs() with
        {
            SignedInEmail = Person,
            HandoffToken = "handoff-abc",
        });

        Assert.Null(locked.HandoffToken);
        Assert.Null(signedOut.HandoffToken);
        Assert.Equal("handoff-abc", active.HandoffToken);
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
    public void ARecordCARRIEDToAnotherMachineFailsItsOwnSignature()
    {
        // The one thing this tripwire genuinely stops. Its key is derived from
        // the device fingerprint, so the same bytes read on another machine do
        // not verify - which is why the record may live in a store a person can
        // export.
        StudioSsoIdentityRecord record = Build(Inputs() with { SignedInEmail = Person });
        record.Device.CanonicalFingerprint = "SHA256:0000FFFF";

        Assert.False(StudioSsoIdentitySignature.Verify(record));
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

        Assert.Null(StudioSsoIdentityStore.Read(store));
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
        StudioSsoIdentityRecord? read = StudioSsoIdentityStore.Read(store);

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

        Assert.Null(StudioSsoIdentityStore.Read(store));
        Assert.False(StudioSsoIdentityStore.Write(
            store,
            Build(Inputs() with { SignedInEmail = Person })));
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
