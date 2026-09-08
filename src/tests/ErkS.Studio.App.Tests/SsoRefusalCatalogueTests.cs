using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The refusal vocabulary, published as a file the other products compare
/// against instead of retyping.
///
/// 🔴 THIS TEST EXISTS BECAUSE PROSE ALREADY FAILED, ON THE SAME DAY. The names
/// went out in a markdown table and within hours three products had three
/// dictionaries: PFA wrote its own before the canon existed, CGM copied PFA, and
/// only PFR matched. A contract somebody has to retype is a contract that
/// drifts. So the catalogue is generated into a file, and this test is what
/// keeps the file and the code from parting company.
/// </summary>
public sealed class SsoRefusalCatalogueTests
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

    [Fact]
    public void THEPublishedFileIsWhatTheCatalogueSaysToday()
    {
        // Regenerated and compared, so a code added or reworded in the catalogue
        // cannot ship without the file the readers hold changing with it.
        Assert.Equal(SsoContractRenderer.Codes(), ReadPublished());
    }

    [Fact]
    public void EVERYSharedCodeMatchesTheSERVERSOwnWords()
    {
        // 🔴 THE HALF THAT WOULD OTHERWISE DRIFT SILENTLY. Five of these codes
        // belong to both sides and their sentences are the SERVER's. Copying
        // them into this catalogue without a check would recreate, one level up,
        // exactly the divergence this file exists to stop - and it would show up
        // as a person being told two different things about one situation.
        JsonNode server = JsonNode.Parse(
            File.ReadAllText(ContractPath("sso-plugin-resolve-vectors.json")))!;
        JsonArray refusals = server["refusals"]!.AsArray();

        var shared = new List<string>();
        foreach (JsonNode? entry in refusals)
        {
            string code = entry!["code"]!.GetValue<string>();
            StudioSsoRefusal? mine = StudioSsoRefusalCatalogue.All
                .SingleOrDefault(item => item.Code == code);
            if (mine is null)
            {
                Assert.Fail(
                    "the server publishes a refusal this catalogue has never heard of: " +
                    code + " - add it rather than letting each reader invent a name");
            }

            shared.Add(code);
            Assert.Equal(entry["message"]!.GetValue<string>(), mine!.MessageMn);
            Assert.Equal(entry["status"]!.GetValue<int>(), mine.HttpStatus);
            Assert.True(
                mine.Origin.HasFlag(StudioSsoRefusalOrigin.Server),
                code + " is raised by the server and must say so");
        }

        // The control: without it, a server file that failed to load would leave
        // the loop with nothing to compare and this test would pass in silence.
        Assert.Equal(8, shared.Count);
    }

    [Fact]
    public void READERCodesAreOrderedAndServerOnlyCodesAreNot()
    {
        // The check order is part of the contract - each step makes the fields
        // below it mean something - so a code a reader can raise has to say
        // where it belongs, and one only the server raises must not pretend to.
        foreach (StudioSsoRefusal refusal in StudioSsoRefusalCatalogue.All)
        {
            if (refusal.Origin.HasFlag(StudioSsoRefusalOrigin.Reader))
            {
                Assert.True(
                    refusal.CheckOrder is > 0,
                    refusal.Code + " is decided by a reader and must carry its step");
            }
            else
            {
                Assert.Null(refusal.CheckOrder);
            }
        }

        // The steps run 1..8 with two shared positions: SignedOut and BotLocked
        // are both decided at the state check, and a missing token is named
        // differently on a seat than on a person.
        int[] steps = StudioSsoRefusalCatalogue.All
            .Where(item => item.CheckOrder is not null)
            .Select(item => item.CheckOrder!.Value)
            .Order()
            .ToArray();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 7, 8, 8], steps);
    }

    [Fact]
    public void THESignatureCheckCANNOTBeADeviceCheck()
    {
        // 🔴 PFA'S TECHNIQUE, TAKEN: lock the REASON for the name, not only the
        // name. sso_device_mismatch was renamed today because the check cannot
        // establish what its name claimed - and nothing stopped somebody from
        // widening it tomorrow and leaving the new name just as wrong.
        //
        // Asserted structurally rather than from the source text: the verifier
        // is handed the record and nothing else. No fingerprint, no machine
        // identity, no store. A check with no device input CANNOT be a device
        // check, and the day one is added this goes red and the name is
        // reconsidered on purpose.
        System.Reflection.MethodInfo verify =
            typeof(StudioSsoIdentitySignature).GetMethod("Verify")!;
        System.Reflection.ParameterInfo[] parameters = verify.GetParameters();

        Assert.Single(parameters);
        Assert.Equal(typeof(StudioSsoIdentityRecord), parameters[0].ParameterType);

        // And the read that raises it sees only the store: no device argument
        // reaches the decision either.
        System.Reflection.MethodInfo read =
            typeof(StudioSsoIdentityStore).GetMethod("Read")!;
        Assert.Single(read.GetParameters());
        Assert.Equal(typeof(ICredentialStore), read.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void THEEXPIRYCapIsPublishedAsAMEASUREDFact()
    {
        // 🔴 THE HALF PFA CANNOT CHECK FOR THEMSELVES. They read the record's
        // expiry and trust it is never longer than the token inside it; the
        // token is opaque to them, so they cannot verify that. If Studio dropped
        // the cap they would break with nothing red anywhere.
        //
        // The published value is the OUTCOME of running the publisher, so
        // removing the cap changes the file and this comparison goes red - the
        // side that can prove the invariant owns the proof.
        JsonNode published = JsonNode.Parse(ReadPublished())!;
        Assert.True(
            published["invariants"]!["recordExpiryIsCappedByTokenExpiry"]!.GetValue<bool>(),
            "the cap is what PFA depends on; if this is false the readers must be told");

        // Measured again here, independently of the renderer, so the file and
        // the behaviour cannot agree with each other while both being wrong.
        var now = new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero);
        StudioSsoIdentityRecord record = StudioSsoIdentityPublisher.Build(
            new StudioSsoIdentityInputs(
                "someone@erk-s.mn", null, null, false, "fp", "fp-legacy",
                "token", now.AddDays(3), StudioSsoHandoffScope.Person, now),
            previous: null);
        Assert.Equal(now.AddDays(3), record.ExpiresAtUtc);
    }

    [Fact]
    public void THERENAMEDCodeIsGoneAndTheSERVERSKept()
    {
        // Two different proofs, two names. The reader's signature check keeps
        // sso_signature_invalid; the server's device claim keeps
        // sso_device_mismatch. Sharing one name made a reader's formatting bug
        // look like the server's device verdict, and told people their record
        // had been altered when the fault was ours.
        StudioSsoRefusal signature = StudioSsoRefusalCatalogue.All
            .Single(item => item.Code == "sso_signature_invalid");
        StudioSsoRefusal device = StudioSsoRefusalCatalogue.All
            .Single(item => item.Code == "sso_device_mismatch");

        Assert.Equal(StudioSsoRefusalOrigin.Reader, signature.Origin);
        Assert.Equal(StudioSsoRefusalOrigin.Server, device.Origin);
        Assert.Null(device.CheckOrder);
        Assert.Equal(5, signature.CheckOrder);
    }

    [Fact]
    public void EVERYCodeCarriesASentenceAndAWayForward()
    {
        // A bare code is an obstacle. Four of these are situations a person can
        // fix themselves and the rest are not; saying which is which is the
        // difference between a message and a wall.
        Assert.All(StudioSsoRefusalCatalogue.All, refusal =>
        {
            Assert.StartsWith("sso_", refusal.Code, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(refusal.MessageMn), refusal.Code);
            Assert.NotEqual(StudioSsoRefusalOrigin.None, refusal.Origin);
        });

        Assert.Equal(
            StudioSsoRefusalCatalogue.All.Select(item => item.Code).Distinct().Count(),
            StudioSsoRefusalCatalogue.All.Count);
    }

    [Fact]
    public void ANUNKNOWNCodeGetsNoSentenceRatherThanAMadeUpOne()
    {
        // A stand-in sentence for a code this build has never heard of would
        // read as though the situation were understood. Empty is the honest
        // answer and the caller can say so.
        Assert.Equal("", StudioSsoRefusalCatalogue.MessageMn("sso_something_new"));
        Assert.NotEqual("", StudioSsoRefusalCatalogue.MessageMn("sso_store_unavailable"));
    }

    [Fact]
    public void STUDIOSPeopleReadTheSameSentencesAsThePlugins()
    {
        // The catalogue would be a declaration with no caller if Studio only
        // published it. Two of these situations are ones Studio itself detects,
        // and it says the same words the plugin about to refuse will say - two
        // wordings for one situation is how somebody concludes they are looking
        // at two problems.
        string view = ReadAppSource("ShellView.cs");

        // Asserted as «the catalogue is the source» rather than by pinning one
        // call shape: the token message became a choice between two codes when
        // seats got their own, and an assertion pinned to the old spelling would
        // have gone red for a reason that had nothing to do with what it checks.
        Assert.Contains("StudioSsoRefusalCatalogue.MessageMn(", view, StringComparison.Ordinal);
        foreach (string code in new[]
        {
            "sso_store_unavailable",
            "sso_handoff_token_missing",
            "sso_seat_token_absent",
        })
        {
            Assert.Contains("\"" + code + "\"", view, StringComparison.Ordinal);
        }
    }

    private static string ReadPublished()
    {
        string path = ContractPath("sso-device-identity-codes.json");
        Assert.True(
            File.Exists(path),
            "the refusal catalogue was not copied to the output: " + path);
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    private static string ContractPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "contracts", fileName);

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
}
