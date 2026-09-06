using System.Text;
using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Reading the published catalogue, tested against the rows SRV actually
/// published rather than against rows shaped to pass.
///
/// The reader was written before the route was live, so the ENVELOPE around the
/// rows is an assumption while the rows themselves are not. That asymmetry is
/// what these tests are mostly about: the rows are held to the contract exactly,
/// and the wrapper is required to fail LOUDLY and by name when it is not
/// recognised - because the quiet version of that failure is an empty list, and
/// an empty list is also what "this sum has no bags" and "nothing downloaded"
/// look like.
/// </summary>
public sealed class AdministrativeUnitDocumentTests
{
    [Fact]
    public void TheContractsOwnRowsREADBACKUnchanged()
    {
        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(
            Envelope(ContractRows(), asOfUtc: "2026-09-06T08:00:00Z"));

        Assert.Equal("", read.ProblemMn);
        Assert.True(read.IsUsable);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero), read.AsOfUtc);

        AdministrativeUnit ulaanbaatar = read.Units.Single(unit => unit.UnitCode == "511");
        Assert.Equal("Улаанбаатар", ulaanbaatar.NameMn);
        Assert.Equal("Дүүрэг", ulaanbaatar.ChildPickerLabelMn);
        Assert.Equal("", ulaanbaatar.ParentUnitCode);

        // The city inside an aimag, whose wards are «баг». Read, never worked
        // out - the reason the label travels in the data at all.
        Assert.Equal("Баг", read.Units.Single(unit => unit.UnitCode == "26101").ChildPickerLabelMn);
    }

    [Fact]
    public void ABareArrayIsREFUSEDByName()
    {
        // Refused rather than accepted-with-no-version: a list with no asOfUtc
        // makes a choice from a three-month-old copy and one from live data the
        // same sentence. The refusal has to NAME asOfUtc, or whoever reads it
        // starts looking at the rows.
        string bare = "[" + string.Join(",", ContractRowsJson()) + "]";

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(bare);

        Assert.False(read.IsUsable);
        Assert.Contains("asOfUtc", read.ProblemMn, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnvelopeWithNoVersionIsREFUSED()
    {
        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(
            "{\"units\":[" + string.Join(",", ContractRowsJson()) + "]}");

        Assert.False(read.IsUsable);
        Assert.Contains("asOfUtc", read.ProblemMn, StringComparison.Ordinal);
    }

    [Fact]
    public void TheROWSAreFoundWhateverTheWrapperCallsThem()
    {
        // The envelope was never published, so searching for the name `units`
        // would be a guess - and a guess that misses returns an empty catalogue,
        // which looks exactly like a server that is down. The rows are found by
        // SHAPE instead, and the name that matched is reported so the assumption
        // can be checked against the real route rather than believed.
        string json =
            "{\"generatedBy\":\"srv\",\"asOfUtc\":\"2026-09-06T08:00:00Z\"," +
            "\"administrativeDivisions\":[" + string.Join(",", ContractRowsJson()) + "]}";

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(json);

        Assert.True(read.IsUsable);
        Assert.Equal("administrativeDivisions", read.UnitsPropertyName);
    }

    [Fact]
    public void TOTALSAreSkippedAndCOUNTED()
    {
        // «Улсын дүн» and the regional sums ride along in the same list. Filtered
        // by code length, because a total in the picker is a building standing in
        // the whole country - and counted, because a count that suddenly doubles
        // is the sign that the source changed shape.
        string json = Envelope(
            "{\"unitCode\":\"0\",\"level\":\"Total\",\"parentUnitCode\":null,\"nameMn\":\"Улсын дүн\"}," +
            "{\"unitCode\":\"51\",\"level\":\"Total\",\"parentUnitCode\":null,\"nameMn\":\"Төвийн бүс\"}," +
            ContractRows(),
            asOfUtc: "2026-09-06T08:00:00Z");

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(json);

        Assert.True(read.IsUsable);
        Assert.Equal(2, read.SkippedRollUpRows);
        Assert.DoesNotContain(read.Units, unit => unit.NameMn.Contains("дүн", StringComparison.Ordinal));
    }

    [Fact]
    public void ONEMalformedRowRejectsTHEWHOLECatalogue()
    {
        // The alternative is a catalogue with places missing from it and nothing
        // downstream able to say which - the shape of defect this codebase has
        // now met several times: the partial result that reports success.
        string json = Envelope(
            ContractRows() + ",{\"unitCode\":\"5110155\",\"level\":\"Khoroo\"," +
            "\"parentUnitCode\":\"51101\"}",
            asOfUtc: "2026-09-06T08:00:00Z");

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(json);

        Assert.False(read.IsUsable);
        Assert.Contains("nameMn", read.ProblemMn, StringComparison.Ordinal);
        Assert.Empty(read.Units);
    }

    [Fact]
    public void ARowThatDISAGREESWithItsOwnCodeIsRejected()
    {
        // The catalogue publishes parentUnitCode AND the prefix rule derives one.
        // Two independent statements exist precisely so that a disagreement can
        // be noticed; picking either silently is how a restore starts failing for
        // a reason nobody can see, since Restore uses the prefix and the picker
        // uses the published parent.
        string json = Envelope(
            "{\"unitCode\":\"5110151\",\"level\":\"Khoroo\",\"parentUnitCode\":\"51102\"," +
            "\"nameMn\":\"1-р хороо\"}",
            asOfUtc: "2026-09-06T08:00:00Z");

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(json);

        Assert.False(read.IsUsable);
        Assert.Contains("51101", read.ProblemMn, StringComparison.Ordinal);
        Assert.Contains("51102", read.ProblemMn, StringComparison.Ordinal);
    }

    [Fact]
    public void ADUPLICATEDCodeIsRejected()
    {
        // The code is the key every downstream reader matches on. Two rows sharing
        // one puts the same place in a picker twice and lets a restore land on
        // either - and the user's own spreadsheet already carried exactly this
        // defect, a duplicated Сонгино хайрхан with a code NSO never issued.
        string json = Envelope(ContractRows() + "," + ContractRowsJson()[0], asOfUtc: "2026-09-06T08:00:00Z");

        AdministrativeUnitDocumentRead read = AdministrativeUnitDocument.Read(json);

        Assert.False(read.IsUsable);
        Assert.Contains("511", read.ProblemMn, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRefusalSAYSSomethingDIFFERENT()
    {
        // THE DISCRIMINATION CHECK. Each of these is a different thing to go and
        // do - publish the route, fix the wrapper, look at the data - and a
        // reader who gets one sentence for all of them acts on a guess. Asserting
        // they are pairwise distinct is what stops a later tidy-up from merging
        // them into one polite apology.
        string[] problems =
        [
            AdministrativeUnitDocument.Read("").ProblemMn,
            AdministrativeUnitDocument.Read("not json at all").ProblemMn,
            AdministrativeUnitDocument.Read("[]").ProblemMn,
            AdministrativeUnitDocument.Read("{\"units\":[" + ContractRowsJson()[0] + "]}").ProblemMn,
            AdministrativeUnitDocument.Read(Envelope(
                "{\"unitCode\":\"0\",\"level\":\"Total\",\"parentUnitCode\":null,\"nameMn\":\"Улсын дүн\"}",
                asOfUtc: "2026-09-06T08:00:00Z")).ProblemMn,
        ];

        Assert.All(problems, problem => Assert.NotEqual("", problem));
        Assert.Equal(problems.Length, problems.Distinct(StringComparer.Ordinal).Count());
    }

    private static string Envelope(string rows, string asOfUtc) =>
        "{\"asOfUtc\":\"" + asOfUtc + "\",\"units\":[" + rows + "]}";

    /// <summary>
    /// The contract's own rows, serialised back out. READ from the published file
    /// rather than retyped: a copied fixture states the same facts a second time
    /// and goes on asserting them after the contract changes.
    /// </summary>
    private static string ContractRows() => string.Join(",", ContractRowsJson());

    private static IReadOnlyList<string> ContractRowsJson()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? contract = null;
        while (directory is not null && contract is null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "_shared",
                "mongolia-admin-divisions-contract-2026-09-06.json");
            if (File.Exists(candidate))
                contract = candidate;
            directory = directory.Parent;
        }

        Assert.True(contract is not null, "the administrative divisions contract was not found");

        List<string> rows = [];
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(contract!, Encoding.UTF8));
        foreach (JsonProperty branch in document.RootElement.GetProperty("realRows").EnumerateObject())
        {
            if (branch.Value.ValueKind != JsonValueKind.Array)
                continue;
            foreach (JsonElement row in branch.Value.EnumerateArray())
                rows.Add(row.GetRawText());
        }

        Assert.NotEmpty(rows);
        return rows;
    }
}
