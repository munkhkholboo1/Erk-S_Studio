using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Decision №25: the project's address offers the officials who sign the cover.
///
/// 🔴 THE OWNER ASKED FOR THIS TWICE IN ONE DAY. «төслийн хаягаар албан
/// тушаалтанг олж автоматаар бичдэг логикоо хийнэ шүү» - and earlier the same
/// morning, «хаягийг сонгоход эрүүл ахуй, онцгой байдлын албан тушаалтнууд
/// автоматаар сонгогддог байна гэж ярилцсан яриа юу болсон бэ? одоо болтол
/// бодит үр дүн байхгүй байна». A conversation had happened and left no record,
/// so nobody had started.
///
/// 🔴 AND THE HARD HALF IS THE SOURCE, NOT THE LOGIC. Measured 2026-09-11: no
/// open source publishes these officials today. So what is built here is the
/// part that does not depend on one - the key, the lookup, the proposal - each
/// tested against fixtures, exactly as the administrative-unit catalogue was
/// finished before its first request.
/// </summary>
public sealed class THEAddressOffersTheOfficialsTests
{
    private const string Ulaanbaatar = "011";
    private const string Bayangol = "01103";
    private const string Khoroo = "0110315";

    [Fact]
    public void THEKeyIsTheFiveDigitLevel()
    {
        // Sum or district: that is where these offices are. A whole address
        // answers with its district, not its ward.
        Assert.Equal(Bayangol, OfficialsLookupKey.For(Whole()));
    }

    [Fact]
    public void APROVINCEAloneAnswersNOTHINGRatherThanEverything()
    {
        // 🔴 THE WRONG ANSWER THAT LOOKS RIGHT. Walking up from whatever is
        // filled in would hand a project that named only its province the
        // capital's officials - a plausible list, printed on a sheet, for a
        // district nobody chose.
        var provinceOnly = new ProjectSiteLocation
        {
            ProvinceCode = Ulaanbaatar,
            ProvinceName = "Улаанбаатар",
        };

        Assert.Equal("", OfficialsLookupKey.For(provinceOnly));
    }

    [Fact]
    public void ABROKENAddressAnswersNOTHING()
    {
        // A ward that does not sit under the district beside it. The chain check
        // exists because this state is reachable; officials resolved from one
        // half of it would be printed next to the other half.
        var broken = new ProjectSiteLocation
        {
            ProvinceCode = Ulaanbaatar,
            ProvinceName = "Улаанбаатар",
            DistrictCode = Bayangol,
            DistrictName = "Баянгол",
            WardCode = "0110415",
            WardName = "15-р хороо",
        };

        Assert.False(broken.ChainHoldsTogether, "the fixture stopped being a broken address");
        Assert.Equal("", OfficialsLookupKey.For(broken));
    }

    [Fact]
    public void NOAddressAtAllAnswersNOTHING()
    {
        Assert.Equal("", OfficialsLookupKey.For(null));
        Assert.Equal("", OfficialsLookupKey.For(new ProjectSiteLocation()));
    }

    [Fact]
    public void ANEmptyDirectoryFindsNOBODYAndSaysWHY()
    {
        // 🔴 THE STATE THE PRODUCT SHIPS IN. No source exists yet, so the honest
        // directory is empty AND carries its reason. An empty list with no reason
        // is how «nobody serves this district» and «nothing has been loaded» stop
        // being distinguishable.
        OfficialsDirectorySnapshot directory =
            OfficialsDirectorySnapshot.Empty("Албан тушаалтны жагсаалт хараахан бөглөгдөөгүй байна.");

        Assert.Empty(directory.Officials(Bayangol));
        Assert.NotEqual("", directory.UnavailableReasonMn);
        Assert.Null(directory.AsOfUtc);
    }

    [Fact]
    public void AFILLEDDirectoryAnswersTheUnitItWasAskedFor()
    {
        IOfficialsDirectory directory = Directory();

        IReadOnlyList<OfficialsDirectoryEntry> found = directory.Officials(Bayangol);
        Assert.Equal(3, found.Count);

        // A district nobody filled in is empty - the ordinary state, not a fault.
        Assert.Empty(directory.Officials("01104"));
        Assert.Equal("", directory.UnavailableReasonMn);
    }

    [Fact]
    public void AWARDCodeIsNotADistrictCode()
    {
        // The directory is keyed at one level and does not quietly accept
        // another. Asking with a ward must not find the district's officials.
        Assert.Empty(Directory().Officials(Khoroo));
    }

    [Fact]
    public void THEProposalMarksWHICHTableEachRowBelongsIn()
    {
        IReadOnlyList<OfficialsProposalRow> rows =
            OfficialsProposal.For(Directory().Officials(Bayangol), []);

        Assert.Equal(3, rows.Count);
        Assert.Equal(
            2,
            rows.Count(row => row.Block == OfficialsBlock.ConcurredBy));
        Assert.Equal(
            1,
            rows.Count(row => row.Block == OfficialsBlock.ReviewedBy));
    }

    [Fact]
    public void EMERGENCYAndHEALTHConcurWhileURBANPLANNINGReviews()
    {
        // The design's own words, pinned per kind so a body cannot drift into
        // the wrong table: «ЗӨВШИЛЦСӨН - онцгой байдал · эрүүл ахуй»,
        // «ХЯНАСАН - хот байгуулалтын газар».
        Assert.Equal(
            OfficialsBlock.ConcurredBy,
            OfficialsProposal.BlockFor(OfficialBodyKind.EmergencyManagement));
        Assert.Equal(
            OfficialsBlock.ConcurredBy,
            OfficialsProposal.BlockFor(OfficialBodyKind.PublicHealth));
        Assert.Equal(
            OfficialsBlock.ReviewedBy,
            OfficialsProposal.BlockFor(OfficialBodyKind.UrbanPlanning));
    }

    [Fact]
    public void EVERYKindOfBodyHasADecidedTable()
    {
        // 🔴 DERIVED FROM THE ENUM, NEVER HAND-LISTED. A kind added without a
        // placement would otherwise be discovered by the sheet, which is the
        // last place that should find out.
        foreach (OfficialBodyKind kind in Enum.GetValues<OfficialBodyKind>())
        {
            OfficialsBlock block = OfficialsProposal.BlockFor(kind);
            Assert.True(
                Enum.IsDefined(block),
                $"«{kind}» resolved to a block that is not one of the sheet's tables");
        }
    }

    [Fact]
    public void AROWTheRosterALREADYCarriesIsMarkedSO()
    {
        // Matched by ORGANISATION. The person in the chair changes; the office
        // does not, so matching on a name would offer the same office again
        // under every new occupant.
        var existing = new List<ProjectApprovalEntry>
        {
            new()
            {
                OrganizationName = "Баянгол дүүргийн Онцгой байдлын хэлтэс",
                PositionTitle = "Дарга",
                PersonName = "Өөр хүн",
            },
        };

        IReadOnlyList<OfficialsProposalRow> rows =
            OfficialsProposal.For(Directory().Officials(Bayangol), existing);

        OfficialsProposalRow emergency = Assert.Single(
            rows, row => row.Entry.Kind == OfficialBodyKind.EmergencyManagement);
        Assert.True(emergency.AlreadyPresent);

        // And the others are still offered.
        Assert.All(
            rows.Where(row => row.Entry.Kind != OfficialBodyKind.EmergencyManagement),
            row => Assert.False(row.AlreadyPresent));
    }

    [Fact]
    public void THEProposalWRITESNothingAndCopiesWhatItOffers()
    {
        // 🔴 «СОНГОДОГ БАЙНА» - THE AUTOMATIC SITS UNDER A MANUAL CHOICE. The
        // proposal must not be able to reach the roster or the directory on its
        // own, so it hands out COPIES: a screen that edits a proposed row before
        // accepting it must not alter the directory behind it.
        var roster = new List<ProjectApprovalEntry>();
        IOfficialsDirectory directory = Directory();

        IReadOnlyList<OfficialsProposalRow> rows =
            OfficialsProposal.For(directory.Officials(Bayangol), roster);

        Assert.Empty(roster);

        rows[0].Entry.PersonName = "Гараар засав";
        Assert.NotEqual(
            "Гараар засав",
            directory.Officials(Bayangol)[0].PersonName);
    }

    [Fact]
    public void EVERYProposedRowCanNowBeKept()
    {
        // 🔴 THIS TEST USED TO ASSERT THE OPPOSITE, AND THAT IS WHY IT IS HERE.
        // ХЯНАСАН was drawn on the cover and stored NOWHERE, so two of the four
        // rows the design names could be resolved and not saved. The gap was
        // written down as a failing condition rather than a comment, and when
        // ConceptDesign.ReviewedBy arrived on 2026-09-12 this went red - which is
        // the change finishing where the gap was recorded, instead of being
        // discovered by somebody's empty sheet.
        IReadOnlyList<OfficialsProposalRow> rows =
            OfficialsProposal.For(Directory().Officials(Bayangol), []);

        Assert.All(rows, row => Assert.True(OfficialsProposal.CanBeStored(row)));
        Assert.Contains(rows, row => row.Block == OfficialsBlock.ReviewedBy);
    }

    [Fact]
    public void EACHTableIsAskedAboutITSOWNRows()
    {
        // 🔴 ONE COMBINED SET WOULD SILENCE THE WRONG OFFER. The two tables ask
        // different questions about the same bodies, so an organisation typed
        // into ЗӨВШИЛЦСӨН must not report the ХЯНАСАН row as already handled.
        var concurred = new List<ProjectApprovalEntry>
        {
            new() { OrganizationName = "Баянгол дүүргийн Хот байгуулалтын алба" },
        };

        IReadOnlyList<OfficialsProposalRow> rows =
            OfficialsProposal.For(Directory().Officials(Bayangol), concurred, reviewedBy: []);

        Assert.False(
            Assert.Single(rows, row => row.Block == OfficialsBlock.ReviewedBy).AlreadyPresent,
            "a row on the other table was taken for this one");

        // And the mirror: the same name on the RIGHT table does mark it.
        IReadOnlyList<OfficialsProposalRow> mirrored =
            OfficialsProposal.For(Directory().Officials(Bayangol), [], reviewedBy: concurred);

        Assert.True(
            Assert.Single(mirrored, row => row.Block == OfficialsBlock.ReviewedBy).AlreadyPresent);
        Assert.All(
            mirrored.Where(row => row.Block == OfficialsBlock.ConcurredBy),
            row => Assert.False(row.AlreadyPresent));
    }

    [Fact]
    public void APROJECTWithNoReviewersReadsAsEmptyAndIsNotMigrated()
    {
        // Additive only: a roster that has never heard of this list answers with
        // an empty one, and nothing is moved into it from a neighbour.
        var roster = new ConceptDesignApprovalRoster();
        roster.Normalize();

        Assert.Empty(roster.ReviewedBy);
        Assert.Empty(roster.ConcurredBy);

        // A row on a neighbouring list stays on that list.
        roster.EndorsedBy.Add(new ProjectApprovalEntry { OrganizationName = "Хөрш" });
        roster.Normalize();

        Assert.Empty(roster.ReviewedBy);
        Assert.Single(roster.EndorsedBy);
    }

    [Fact]
    public void AREVIEWERSurvivesACloneLikeEveryOtherRow()
    {
        // A list that is stored but not copied is lost the first time a project
        // is duplicated - silently, because nothing else about it changes.
        var roster = new ConceptDesignApprovalRoster();
        roster.ReviewedBy.Add(new ProjectApprovalEntry
        {
            OrganizationName = "Хот байгуулалтын алба",
            PositionTitle = "Мэргэжилтэн",
            PersonName = "Тест",
        });

        ConceptDesignApprovalRoster copy = roster.Clone();
        ProjectApprovalEntry copied = Assert.Single(copy.ReviewedBy);

        Assert.Equal("Хот байгуулалтын алба", copied.OrganizationName);

        // A copy, not the same object: editing one project must not edit another.
        copied.PersonName = "Өөрчлөв";
        Assert.Equal("Тест", roster.ReviewedBy[0].PersonName);
    }

    [Fact]
    public void ATHIRDReviewerIsNOTThrownAwayOnSave()
    {
        // 🔴 TWO IS WHAT THE SHEET DRAWS, NOT WHAT THE STORE ENFORCES. Silently
        // dropping a third row on normalise would destroy somebody's typing at
        // save time, which is worse than a row that does not fit: the cap belongs
        // where rows are entered, in sight of the person entering them.
        var roster = new ConceptDesignApprovalRoster();
        for (var index = 0; index < 3; index++)
            roster.ReviewedBy.Add(new ProjectApprovalEntry { OrganizationName = "Гурав " + index });

        roster.Normalize();

        Assert.Equal(3, roster.ReviewedBy.Count);
    }

    [Fact]
    public void NOTHINGFoundOffersNOTHING()
    {
        Assert.Empty(OfficialsProposal.For([], []));
        Assert.Empty(OfficialsProposal.For(null, []));
    }

    [Fact]
    public void ANOfficialBecomesAnOrdinaryRosterRow()
    {
        // The three columns the roster editor already shows: organisation,
        // position, name. Nothing new has to be stored for a row to be kept.
        OfficialsDirectoryEntry entry = Directory().Officials(Bayangol)[0];
        ProjectApprovalEntry row = entry.ToApprovalEntry();

        Assert.Equal(entry.OrganizationName, row.OrganizationName);
        Assert.Equal(entry.PositionTitle, row.PositionTitle);
        Assert.Equal(entry.PersonName, row.PersonName);
        Assert.NotEqual("", row.Id);
    }

    [Fact]
    public void ANOfficeWithNoPersonIsStillAnOffice()
    {
        // Known office, unknown occupant: a real state. A paper form prints the
        // office with a line to sign, and refusing the row would lose what IS
        // known to protect a blank.
        var directory = new OfficialsDirectorySnapshot(
            [
                new OfficialsDirectoryEntry
                {
                    UnitCode = Bayangol,
                    Kind = OfficialBodyKind.PublicHealth,
                    OrganizationName = "Баянгол дүүргийн Эрүүл мэндийн төв",
                    PositionTitle = "Дарга",
                    PersonName = "",
                },
            ],
            asOfUtc: null);

        OfficialsDirectoryEntry found = Assert.Single(directory.Officials(Bayangol));
        Assert.Equal("", found.PersonName);
        Assert.NotEqual("", found.OrganizationName);
    }

    [Fact]
    public void AROWWithNoUnitCodeIsREFUSEDRatherThanStoredUnreachable()
    {
        // 🔴 THIS TEST USED TO ASSERT THE SYMPTOM AND A MUTATION WALKED THROUGH
        // IT. It asked only «can the row be found», and the answer is no whether
        // the row was refused OR stored under an empty key - so removing the
        // guard entirely left the suite green. What matters is that the row is
        // NOT KEPT: an operator who loads thirty-six rows and silently works
        // with thirty-five has no way to notice.
        var directory = new OfficialsDirectorySnapshot(
            [
                new OfficialsDirectoryEntry
                {
                    UnitCode = "   ",
                    Kind = OfficialBodyKind.PublicHealth,
                    OrganizationName = "Хаана ч хамаарахгүй",
                },
            ],
            asOfUtc: null);

        Assert.Equal(0, directory.Count);
        Assert.Empty(directory.Officials("   "));
        Assert.Empty(directory.Officials(""));
        Assert.Empty(directory.Officials(Bayangol));
    }

    [Fact]
    public void THECountIsOfRowsACTUALLYKept()
    {
        // The positive control for the assertion above. A count that is zero
        // because nothing is ever counted would pass that test while proving
        // nothing at all.
        var mixed = new OfficialsDirectorySnapshot(
            [
                new OfficialsDirectoryEntry
                {
                    UnitCode = Bayangol,
                    Kind = OfficialBodyKind.EmergencyManagement,
                    OrganizationName = "Тоологдох ёстой",
                },
                new OfficialsDirectoryEntry
                {
                    UnitCode = "",
                    Kind = OfficialBodyKind.PublicHealth,
                    OrganizationName = "Тоологдох ёсгүй",
                },
            ],
            asOfUtc: null);

        Assert.Equal(1, mixed.Count);
        Assert.Single(mixed.Officials(Bayangol));
        Assert.Equal(3, ((OfficialsDirectorySnapshot)Directory()).Count);
        Assert.Equal(0, OfficialsDirectorySnapshot.Empty("шалтгаан").Count);
    }

    private static ProjectSiteLocation Whole() => new()
    {
        ProvinceCode = Ulaanbaatar,
        ProvinceName = "Улаанбаатар",
        DistrictCode = Bayangol,
        DistrictName = "Баянгол",
        WardCode = Khoroo,
        WardName = "15-р хороо",
    };

    /// <summary>
    /// A fixture, and ONLY a fixture. These are not real officials: the source
    /// that would name them does not exist yet, and inventing one here so the
    /// product had something to show would put made-up people on a signed sheet.
    /// </summary>
    private static IOfficialsDirectory Directory() => new OfficialsDirectorySnapshot(
        [
            new OfficialsDirectoryEntry
            {
                UnitCode = Bayangol,
                Kind = OfficialBodyKind.EmergencyManagement,
                OrganizationName = "Баянгол дүүргийн Онцгой байдлын хэлтэс",
                PositionTitle = "Дарга",
                PersonName = "Тест Нэг",
            },
            new OfficialsDirectoryEntry
            {
                UnitCode = Bayangol,
                Kind = OfficialBodyKind.PublicHealth,
                OrganizationName = "Баянгол дүүргийн Эрүүл мэндийн төв",
                PositionTitle = "Дарга",
                PersonName = "Тест Хоёр",
            },
            new OfficialsDirectoryEntry
            {
                UnitCode = Bayangol,
                Kind = OfficialBodyKind.UrbanPlanning,
                OrganizationName = "Баянгол дүүргийн Хот байгуулалтын алба",
                PositionTitle = "Мэргэжилтэн",
                PersonName = "Тест Гурав",
            },
        ],
        asOfUtc: null);
}
