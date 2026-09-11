using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The officials file keeps what it can and SAYS what it could not.
///
/// 🔴 THE HAZARD IS SILENCE, NOT FAILURE. A file that will not parse is loud.
/// A file that parses and loses row seventeen is not: every lookup simply
/// answers «nobody serves this district», which is a sentence the product says
/// all day for perfectly ordinary reasons. Somebody who typed thirty-six rows
/// and works with thirty-five has no way to notice - the same defect that
/// survived a whole suite of lookup tests in `a62b174`.
///
/// So the refusals travel with the result AND with the directory: the parse
/// happens once, and the screen is looked at all day.
/// </summary>
public sealed class AREFUSEDRowIsReportedNotSwallowedTests
{
    private const string Bayangol = "01103";

    [Fact]
    public void AGOODFileIsReadWholeAndLosesNothing()
    {
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
            Row(Bayangol, "EmergencyManagement", "Онцгой байдлын хэлтэс", "Дарга", "Нэг"),
            Row(Bayangol, "PublicHealth", "Эрүүл мэндийн төв", "Дарга", "Хоёр")));

        Assert.True(read.IsUsable);
        Assert.Equal(2, read.Entries.Count);
        Assert.Empty(read.Rejected);
        Assert.Equal("", read.LossMn);
        Assert.Equal("", read.ProblemMn);
    }

    [Fact]
    public void ONEBadRowDoesNotLockThePersonOutOfTheRest()
    {
        // 🔴 DELIBERATELY UNLIKE THE UNIT CATALOGUE, WHICH REJECTS THE WHOLE
        // DOCUMENT FOR ONE BAD ROW. That is right for a published list of places,
        // where a gap is invisible downstream. This list is maintained by hand,
        // and a typo on one row must not take the other thirty-five away.
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
            Row(Bayangol, "EmergencyManagement", "Онцгой байдлын хэлтэс", "Дарга", "Нэг"),
            Row(Bayangol, "ЭрүүлАхуй", "Эрүүл мэндийн төв", "Дарга", "Хоёр"),
            Row(Bayangol, "UrbanPlanning", "Хот байгуулалтын алба", "Мэргэжилтэн", "Гурав")));

        Assert.True(read.IsUsable);
        Assert.Equal(2, read.Entries.Count);

        OfficialsDirectoryRejection rejected = Assert.Single(read.Rejected);
        Assert.Equal(2, rejected.RowNumber);
        Assert.Contains("ЭрүүлАхуй", rejected.ReasonMn, StringComparison.Ordinal);
    }

    [Fact]
    public void THELossIsASentenceNamingTheRowAndTheReason()
    {
        // The person looking for their missing official needs the row number to
        // find it and the reason to fix it. A count alone sends them hunting.
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
            Row(Bayangol, "EmergencyManagement", "Байгаа", "Дарга", "Нэг"),
            Row(Bayangol, "PublicHealth", "", "Дарга", "Хоёр")));

        Assert.Contains("2-р мөр", read.LossMn, StringComparison.Ordinal);
        Assert.Contains("organizationName", read.LossMn, StringComparison.Ordinal);
    }

    [Fact]
    public void THELossSURVIVESIntoTheDirectoryItself()
    {
        // 🔴 THE PARSE HAPPENS ONCE; THE SCREEN IS LOOKED AT ALL DAY. A snapshot
        // that dropped the refusals would put the loss back where nobody sees it.
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
            Row(Bayangol, "EmergencyManagement", "Байгаа", "Дарга", "Нэг"),
            Row("", "PublicHealth", "Кодгүй", "Дарга", "Хоёр")));

        OfficialsDirectorySnapshot directory = OfficialsDirectorySnapshot.From(read);

        Assert.Equal(1, directory.Count);
        Assert.NotEqual("", directory.LossMn);
        Assert.Contains("2-р мөр", directory.LossMn, StringComparison.Ordinal);

        // And a clean read leaves it silent, or the notice would be permanent
        // furniture that nobody reads.
        OfficialsDirectorySnapshot clean = OfficialsDirectorySnapshot.From(
            OfficialsDirectoryDocument.Read(File(
                Row(Bayangol, "EmergencyManagement", "Байгаа", "Дарга", "Нэг"))));
        Assert.Equal("", clean.LossMn);
    }

    [Fact]
    public void AWARDCodeIsREFUSEDRatherThanStoredWhereNothingAsks()
    {
        // 🔴 THE M7 DEFECT, AT THE DOOR THIS TIME. A seven-digit code would be
        // stored under a key no lookup uses: present in the file, absent from
        // every answer, and nothing anywhere saying so.
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
            Row("0110315", "PublicHealth", "Хорооны эмнэлэг", "Эрхлэгч", "Нэг")));

        Assert.Empty(read.Entries);
        OfficialsDirectoryRejection rejected = Assert.Single(read.Rejected);
        Assert.Contains("5 оронтой", rejected.ReasonMn, StringComparison.Ordinal);

        // And the province level is refused for the same reason, from the other side.
        Assert.Single(
            OfficialsDirectoryDocument.Read(File(
                Row("011", "PublicHealth", "Нийслэл", "Дарга", "Нэг"))).Rejected);

        Assert.True(OfficialsDirectoryDocument.IsLookupLevel("01103"));
        Assert.False(OfficialsDirectoryDocument.IsLookupLevel("0110315"));
        Assert.False(OfficialsDirectoryDocument.IsLookupLevel("011"));
        Assert.False(OfficialsDirectoryDocument.IsLookupLevel("0110a"));
    }

    [Fact]
    public void SEVERALOfficialsMaySharePlaceAndOffice()
    {
        // 🔴 THE UNIT CATALOGUE REJECTS DUPLICATE CODES; THIS MUST NOT. Several
        // officials serve one district, and the owner named TWO from a single
        // office: «нөгөө талд хот байгуулалтын газрын 2 албан тушаалтан».
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
            Row(Bayangol, "UrbanPlanning", "Хот байгуулалтын алба", "Дарга", "Нэг"),
            Row(Bayangol, "UrbanPlanning", "Хот байгуулалтын алба", "Мэргэжилтэн", "Хоёр")));

        Assert.Empty(read.Rejected);
        Assert.Equal(2, OfficialsDirectorySnapshot.From(read).Officials(Bayangol).Count);
    }

    [Fact]
    public void ANOfficeWithNoOccupantIsKept()
    {
        // Known office, unknown person: the paper form prints a line to sign.
        // Refusing the row would lose what IS known to protect a blank.
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
            Row(Bayangol, "PublicHealth", "Эрүүл мэндийн төв", "Дарга", "")));

        Assert.Empty(read.Rejected);
        Assert.Equal("", Assert.Single(read.Entries).PersonName);
    }

    [Theory]
    [InlineData("", "Албан тушаалтны файл хоосон байна.")]
    [InlineData("   ", "Албан тушаалтны файл хоосон байна.")]
    public void ANEmptyFileFailsWholeAndSaysSo(string json, string expected)
    {
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(json);

        Assert.False(read.IsUsable);
        Assert.Equal(expected, read.ProblemMn);
    }

    [Fact]
    public void AFILEWithoutTheNamedArrayFailsLOUDLY()
    {
        // 🔴 NAMED, NOT SNIFFED. The unit catalogue finds its wrapper by shape
        // because its envelope could not be measured when it was written; this
        // format is defined here, so a missing wrapper is a fault and not an
        // occasion to guess.
        foreach (string json in new[]
        {
            "{}",
            "[]",
            "{ \"rows\": [] }",
            "{ \"officials\": {} }",
            "not json at all",
        })
        {
            OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(json);
            Assert.False(read.IsUsable, json + " was accepted");
            Assert.NotEqual("", read.ProblemMn);
        }
    }

    [Fact]
    public void AWHOLEDocumentFailureIsNOTARowRejection()
    {
        // They are acted on differently: one means nothing was read, the other
        // means this row was not. Collapsing them would make «the file is broken»
        // and «row 17 is broken» read the same on screen.
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read("{ \"rows\": [] }");

        Assert.NotEqual("", read.ProblemMn);
        Assert.Empty(read.Rejected);
        Assert.Equal("", read.LossMn);
    }

    [Fact]
    public void ANEmptyListIsUSABLEAndIsNotAFailure()
    {
        // The state the product ships in: a file that exists and nobody has
        // filled in. Not an error - there is simply nobody yet.
        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read("{ \"officials\": [] }");

        Assert.True(read.IsUsable);
        Assert.Empty(read.Entries);
        Assert.Empty(read.Rejected);
    }

    [Fact]
    public void THEVersionIsCARRIEDAndNeverInvented()
    {
        OfficialsDirectoryRead stamped = OfficialsDirectoryDocument.Read(
            "{ \"asOfUtc\": \"2026-09-12T04:05:06+00:00\", \"officials\": [] }");
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-12T04:05:06+00:00", System.Globalization.CultureInfo.InvariantCulture),
            stamped.AsOfUtc);

        // 🔴 NULL WHEN THE FILE DOES NOT SAY. A read that stamped «now» would
        // make a three-month-old copy and a live one both claim today - the same
        // rule the unit catalogue holds.
        Assert.Null(OfficialsDirectoryDocument.Read("{ \"officials\": [] }").AsOfUtc);
        Assert.Null(OfficialsDirectoryDocument.Read(
            "{ \"asOfUtc\": \"өчигдөр\", \"officials\": [] }").AsOfUtc);
    }

    [Fact]
    public void EVERYKindNameTheProductCanHOLDIsAlsoReadable()
    {
        // 🔴 DERIVED FROM THE ENUM. A kind added in code but unreadable from a
        // file is a body the owner can be told about and cannot record - and the
        // rejection would name the value, not the gap, sending them to fix a file
        // that was already right.
        foreach (OfficialBodyKind kind in Enum.GetValues<OfficialBodyKind>())
        {
            OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(File(
                Row(Bayangol, kind.ToString(), "Байгууллага", "Албан тушаал", "Хүн")));

            Assert.Empty(read.Rejected);
            Assert.Equal(kind, Assert.Single(read.Entries).Kind);
        }
    }

    private static string File(params string[] rows) =>
        "{ \"officials\": [" + string.Join(",", rows) + "] }";

    private static string Row(
        string unitCode,
        string kind,
        string organizationName,
        string positionTitle,
        string personName) =>
        $$"""
        {
            "unitCode": "{{unitCode}}",
            "kind": "{{kind}}",
            "organizationName": "{{organizationName}}",
            "positionTitle": "{{positionTitle}}",
            "personName": "{{personName}}"
        }
        """;
}
