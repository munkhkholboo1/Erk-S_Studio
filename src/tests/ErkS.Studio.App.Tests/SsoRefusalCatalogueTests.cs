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
        Assert.Equal(Render(), ReadPublished());
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

        // The steps run 1..8 with one shared position: SignedOut and BotLocked
        // are both decided at the state check.
        int[] steps = StudioSsoRefusalCatalogue.All
            .Where(item => item.CheckOrder is not null)
            .Select(item => item.CheckOrder!.Value)
            .Order()
            .ToArray();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 7, 8], steps);
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

        Assert.Contains(
            "StudioSsoRefusalCatalogue.MessageMn(\"sso_store_unavailable\")",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "StudioSsoRefusalCatalogue.MessageMn(\"sso_handoff_token_missing\")",
            view,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The published shape. Kept here rather than in a separate emitter so the
    /// file and the thing that checks it cannot describe different documents.
    /// </summary>
    private static string Render()
    {
        var codes = new JsonArray();
        foreach (StudioSsoRefusal refusal in StudioSsoRefusalCatalogue.All)
        {
            codes.Add(new JsonObject
            {
                ["code"] = refusal.Code,
                ["origin"] = refusal.Origin.ToString(),
                ["httpStatus"] = refusal.HttpStatus,
                ["checkOrder"] = refusal.CheckOrder,
                ["nextStep"] = refusal.NextStep.ToString(),
                ["messageMn"] = refusal.MessageMn,
            });
        }

        var file = new JsonObject
        {
            ["_comment"] = new JsonArray
            {
                "GENERATED by SsoRefusalCatalogueTests. Do not edit by hand - every " +
                "hand-copy of this list has been wrong, because a new code arrives " +
                "silently and every line of the old copy stays true.",
                "",
                "Compare your reader's constants against this file. Names, order and " +
                "sentences all live here; the nearest other implementation is not a " +
                "reference.",
                "",
                "origin: Reader = decided from the resting record before any request. " +
                "Server = decided when the token is presented. Five codes are both, " +
                "deliberately: one situation gets one sentence whichever side noticed it.",
                "",
                "checkOrder: the step a reader raises it at. Order is part of the " +
                "contract - each step makes the fields below it mean something.",
            },
            ["codes"] = codes,
        };
        // Normalised, because line endings are a checkout concern and not
        // part of the contract: the serializer writes CRLF on Windows and
        // git may hand the file back either way.
        return (file.ToJsonString(Options) + "\n")
            .Replace("\r\n", "\n");
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
