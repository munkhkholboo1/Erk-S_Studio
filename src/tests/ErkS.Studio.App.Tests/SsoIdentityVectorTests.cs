using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The published record, executed here.
///
/// 🔴 THE VECTORS ARE THE HANDOVER, NOT A DESCRIPTION OF IT. Four products are
/// about to stop signing in for themselves and start reading what Studio writes,
/// and none of them will have a sign-in left to fall back on. Handing them prose
/// would mean four readers each guessing at a shape; handing them REAL output -
/// the exact bytes this code emits, signature included - means a reader can be
/// built and proved before Studio is ever installed on the machine.
///
/// The same file runs on both sides, which is what keeps them from drifting: a
/// side that changes its mind goes red in its OWN tests rather than refusing to
/// start on somebody's workstation.
///
/// This test also catches the quieter failure: the published file falling behind
/// the code. Every case here is regenerated from the current implementation and
/// compared, so a change to what Studio writes cannot ship without the contract
/// changing with it.
/// </summary>
public sealed class SsoIdentityVectorTests
{
    private sealed record VectorFile(int FormatVersion, string CredentialTarget, List<Vector> Cases);

    private sealed record Vector(
        string Name,
        string? Note,
        VectorInput Input,
        string CanonicalForm,
        StudioSsoIdentityRecord Record);

    private sealed record VectorInput(
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

    public static TheoryData<string> VectorNames()
    {
        var names = new TheoryData<string>();
        foreach (Vector vector in Load().Cases)
            names.Add(vector.Name);
        return names;
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void THEPublisherEmitsExactlyThePublishedRecord(string name)
    {
        Vector vector = Load().Cases.Single(item => item.Name == name);
        StudioSsoIdentityRecord built = StudioSsoIdentityPublisher.Build(
            new StudioSsoIdentityInputs(
                vector.Input.SignedInEmail,
                vector.Input.SeatBotId,
                vector.Input.SeatOrganizationId,
                vector.Input.SeatUnlocked,
                vector.Input.CanonicalFingerprint,
                vector.Input.LegacyFingerprint,
                vector.Input.HandoffToken,
                vector.Input.HandoffTokenExpiresAtUtc,
                vector.Input.HandoffTokenScope,
                vector.Input.NowUtc),
            previous: null);

        Assert.Equal(Serialize(vector.Record), Serialize(built));

        // 🔴 THE STRING THAT WAS SIGNED, PUBLISHED BYTE FOR BYTE. PFA built a
        // reader against the field list alone and signed the JSON's own
        // timestamp text - which System.Text.Json writes without the
        // trailing fraction the canonical form keeps. Every record came out
        // as sso_device_mismatch: a formatting difference wearing the name
        // of a forged record. A field list is not a contract; the exact
        // bytes are, so they travel in the vector now.
        Assert.Equal(vector.CanonicalForm, built.CanonicalForm());

        // The signature is compared as part of the record above, and verified
        // here as well. The two are not the same check: the first says Studio
        // still emits the published bytes, the second says a READER following
        // the published rule accepts them.
        Assert.True(
            StudioSsoIdentitySignature.Verify(built),
            "the published record does not verify against its own rule: " + name);
    }

    [Fact]
    public void EVERYVectorIsActuallyRun()
    {
        // 🔴 THE CONTROL FOR THE THEORY ABOVE. A vector file that failed to load
        // would produce zero cases and a green run - the silent-checker shape
        // this codebase keeps producing. Eight is what the file carries today; a
        // changed count is a deliberate edit and should have to be seen.
        Assert.Equal(8, Load().Cases.Count);
        Assert.All(Load().Cases, vector => Assert.False(string.IsNullOrWhiteSpace(vector.Name)));
    }

    [Fact]
    public void THEPublishedTargetIsTheOneStudioWritesTo()
    {
        // The credential target travels in the vector file because a reader
        // needs it before it can read anything at all. If the two disagree, a
        // perfectly correct reader looks in an empty drawer.
        Assert.Equal(StudioSsoIdentityStore.CredentialTarget, Load().CredentialTarget);
        Assert.Equal(StudioSsoIdentityRecord.CurrentFormatVersion, Load().FormatVersion);
    }

    [Fact]
    public void THEVectorsCoverEveryStateAReaderHasToAnswerFor()
    {
        // A reader has a different message for each of these, and a vector file
        // that skipped one would let a reader ship with that branch unproved.
        HashSet<string> states = Load().Cases
            .Select(vector => vector.Record.State)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(StudioSsoIdentityState.Active, states);
        Assert.Contains(StudioSsoIdentityState.BotLocked, states);
        Assert.Contains(StudioSsoIdentityState.SignedOut, states);
    }

    [Fact]
    public void THECanonicalTimestampIsNOTTheTimestampInTheJSON()
    {
        // The trap itself, asserted as an invariant rather than left to a
        // vector. A data file goes red only while it holds the right case; this
        // goes red the moment the two representations become the same - which
        // is exactly when a reader could start signing the JSON text and get
        // away with it on the cases we happen to publish.
        Vector fractional = Load().Cases.Single(
            item => item.Name == "person-fractional-seconds");

        string json = Serialize(fractional.Record);
        Assert.Contains("11:04:22.123+00:00", json, StringComparison.Ordinal);
        Assert.Contains("11:04:22.1230000+00:00", fractional.CanonicalForm, StringComparison.Ordinal);
        Assert.DoesNotContain("11:04:22.1230000+00:00", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AVectorCarriesFractionalSecondsAtALL()
    {
        // 🔴 THE FIRST FIVE VECTORS WERE ALL WHOLE SECONDS, AND THAT WAS LUCK
        // RUNNING OUT SLOWLY. On a whole second the two forms differ by
        // «.0000000», which catches a reader that signs the JSON text - but
        // nothing catches a reader that writes three fraction digits, because
        // there were no fraction digits to get wrong. A sample taken entirely
        // from one side of a distribution proves only what that side agrees on.
        Assert.Contains(
            Load().Cases,
            vector => vector.Record.IssuedAtUtc.Ticks % TimeSpan.TicksPerSecond != 0);
    }

    private static string Serialize(StudioSsoIdentityRecord record) =>
        JsonSerializer.Serialize(
            record,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private static VectorFile Load()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "contracts", "sso-device-identity-vectors.json");
        Assert.True(
            File.Exists(path),
            "the SSO identity vectors were not copied to the output: " + path);

        VectorFile? file = JsonSerializer.Deserialize<VectorFile>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(file);
        Assert.NotEmpty(file!.Cases);
        return file;
    }
}
