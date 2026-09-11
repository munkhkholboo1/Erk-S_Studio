using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Pressing «suggest from the address» says what it did AND what it did not.
///
/// 🔴 THE CAP AND THE PROPOSAL MEET HERE. ХЯНАСАН holds exactly two; a project
/// with two already and an address offering a third is ordinary, and both wrong
/// answers are silent - adding it puts a row where the sheet cannot print it,
/// dropping it without a word leaves somebody believing the address had nothing.
///
/// This is the last link of №25: the directory is filled, the address resolves,
/// and until now nothing called <see cref="OfficialsProposal"/> at all - a rule
/// written and never wired, which is its own kind of defect.
/// </summary>
public sealed class THEAddressFillsWhatFitsTests
{
    private const string Bayangol = "01103";

    [Fact]
    public void ANEmptyRosterTakesEverythingTheAddressOffers()
    {
        IReadOnlyList<OfficialsProposalOutcome> outcomes =
            OfficialsProposalPlan.For(Offered(), [], []);

        Assert.Equal(3, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.True(outcome.WasAdded));
    }

    [Fact]
    public void AROWTheRosterHasIsREPORTEDNotSilentlySkipped()
    {
        // «Nothing happened» and «that one was already there» are different
        // things to understand, and the second is the ordinary case on a second
        // press of the same button.
        var concurred = new List<ProjectApprovalEntry>
        {
            new() { OrganizationName = "Онцгой байдлын хэлтэс" },
        };

        IReadOnlyList<OfficialsProposalOutcome> outcomes = OfficialsProposalPlan.For(
            OfficialsProposal.For(Directory(), concurred, []),
            concurred,
            []);

        OfficialsProposalOutcome emergency = Assert.Single(
            outcomes,
            outcome => outcome.Entry.Kind == OfficialBodyKind.EmergencyManagement);
        Assert.Equal(OfficialsProposalSkip.AlreadyPresent, emergency.Skip);
        Assert.False(emergency.WasAdded);
    }

    [Fact]
    public void AFULLReviewedTableREFUSESTheThirdRowAndSaysSo()
    {
        // 🔴 THE DRAWING HAS TWO PLACES. A third row would be stored where the
        // sheet cannot print it - kept, invisible, and believed.
        var reviewed = new List<ProjectApprovalEntry>
        {
            new() { OrganizationName = "Нэгдүгээр хянагч" },
            new() { OrganizationName = "Хоёрдугаар хянагч" },
        };

        IReadOnlyList<OfficialsProposalOutcome> outcomes = OfficialsProposalPlan.For(
            OfficialsProposal.For(Directory(), [], reviewed),
            [],
            reviewed);

        OfficialsProposalOutcome urban = Assert.Single(
            outcomes,
            outcome => outcome.Block == OfficialsBlock.ReviewedBy);
        Assert.Equal(OfficialsProposalSkip.NoRoomLeft, urban.Skip);

        // The other table is untouched by the full one.
        Assert.All(
            outcomes.Where(outcome => outcome.Block == OfficialsBlock.ConcurredBy),
            outcome => Assert.True(outcome.WasAdded));
    }

    [Fact]
    public void ROOMIsCountedASRowsAreAddedNotOnceAtTheStart()
    {
        // 🔴 TWO OFFERED TO A TABLE WITH ONE PLACE LEFT: ONE FITS. A single check
        // against the starting count would admit both and overfill the drawing.
        var directory = new OfficialsDirectorySnapshot(
            [
                Official(OfficialBodyKind.UrbanPlanning, "Хот байгуулалт нэг"),
                Official(OfficialBodyKind.UrbanPlanning, "Хот байгуулалт хоёр"),
            ],
            asOfUtc: null);

        var reviewed = new List<ProjectApprovalEntry>
        {
            new() { OrganizationName = "Аль хэдийн нэг" },
        };

        IReadOnlyList<OfficialsProposalOutcome> outcomes = OfficialsProposalPlan.For(
            OfficialsProposal.For(directory.Officials(Bayangol), [], reviewed),
            [],
            reviewed);

        Assert.Equal(1, outcomes.Count(outcome => outcome.WasAdded));
        Assert.Equal(
            1,
            outcomes.Count(outcome => outcome.Skip == OfficialsProposalSkip.NoRoomLeft));
    }

    [Fact]
    public void THEConcurringTableIsCRAMPEDRatherThanREFUSED()
    {
        // 🔴 THE TWO TABLES DIFFER BECAUSE THEIR DRAWINGS DIFFER. ЗӨВШИЛЦСӨН
        // divides a fixed height by whatever it holds, so a further party is ugly
        // and possible; refusing there would leave a project that really has
        // seven unable to produce a cover at all.
        var concurred = Enumerable.Range(0, ProjectApprovalRosterLimits.MaxConcurredBy)
            .Select(index => new ProjectApprovalEntry { OrganizationName = "Тал " + index })
            .ToList();

        IReadOnlyList<OfficialsProposalOutcome> outcomes = OfficialsProposalPlan.For(
            OfficialsProposal.For(Directory(), concurred, []),
            concurred,
            []);

        Assert.All(
            outcomes.Where(outcome => outcome.Block == OfficialsBlock.ConcurredBy),
            outcome => Assert.NotEqual(OfficialsProposalSkip.NoRoomLeft, outcome.Skip));

        // 🔴 THE RULE ITSELF, NOT ONLY ITS EFFECT - AND A MUTATION TAUGHT ME THE
        // DIFFERENCE. A first version computed room for ЗӨВШИЛЦСӨН and never
        // consulted it; flipping that dead value to «capped» changed nothing and
        // this test stayed green. The asymmetry is now a rule with one home,
        // asserted directly.
        Assert.True(OfficialsProposalPlan.RefusesBeyondMaximum(OfficialsBlock.ReviewedBy));
        Assert.False(OfficialsProposalPlan.RefusesBeyondMaximum(OfficialsBlock.ConcurredBy));
    }

    [Fact]
    public void THEProposalAndTheEDITORAgreeAboutWhichTableRefuses()
    {
        // 🔴 TWO SCREENS, ONE RULE. The roster editor refuses a third ХЯНАСАН row
        // where somebody types it; this plan refuses the same row where the
        // address offers it. If they ever disagreed, a row would be accepted on
        // one screen and refused on the other with nothing to explain it - and
        // the two live in different projects, so nothing else compares them.
        Assert.Equal(
            ProjectApprovalRosterLimits.MaxReviewedBy,
            ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo.Count);

        // Every block must have a decided answer, derived rather than listed.
        foreach (OfficialsBlock block in Enum.GetValues<OfficialsBlock>())
            _ = OfficialsProposalPlan.RefusesBeyondMaximum(block);
    }

    [Fact]
    public void THESentenceNamesWhatDidNOTHappen()
    {
        // 🔴 A COUNT OF ADDITIONS ALONE CANNOT TELL AN ADDRESS THAT OFFERED TWO
        // FROM ONE THAT OFFERED THREE. «Two added» beside a third that went
        // nowhere is the shape of every defect this chain has produced.
        var reviewed = new List<ProjectApprovalEntry>
        {
            new() { OrganizationName = "Нэг" },
            new() { OrganizationName = "Хоёр" },
        };
        var concurred = new List<ProjectApprovalEntry>
        {
            new() { OrganizationName = "Онцгой байдлын хэлтэс" },
        };

        string sentence = OfficialsProposalPlan.DescribeMn(
            OfficialsProposalPlan.For(
                OfficialsProposal.For(Directory(), concurred, reviewed),
                concurred,
                reviewed));

        Assert.Contains("нэмэгдлээ", sentence, StringComparison.Ordinal);
        Assert.Contains("аль хэдийн байна", sentence, StringComparison.Ordinal);
        Assert.Contains("багтсангүй", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void NOTHINGOfferedIsITSOwnSentence()
    {
        // The state the product ships in - nobody has filled the directory in -
        // and «nothing was added» would read as a failure of the button.
        Assert.Contains(
            "олдсонгүй",
            OfficialsProposalPlan.DescribeMn([]),
            StringComparison.Ordinal);
        Assert.Contains(
            "олдсонгүй",
            OfficialsProposalPlan.DescribeMn(null),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASENTENCEIsNeverEmptyWhateverHappened()
    {
        // 🔴 A BUTTON THAT ANSWERS WITH A BLANK IS A BUTTON THAT LOOKS BROKEN.
        // Every combination of outcomes must produce something to read.
        foreach (int concurredCount in new[] { 0, 1, ProjectApprovalRosterLimits.MaxConcurredBy })
        {
            foreach (int reviewedCount in new[] { 0, 1, ProjectApprovalRosterLimits.MaxReviewedBy })
            {
                var concurred = Rows(concurredCount, "Зөвшилцсөн ");
                var reviewed = Rows(reviewedCount, "Хянасан ");

                string sentence = OfficialsProposalPlan.DescribeMn(
                    OfficialsProposalPlan.For(
                        OfficialsProposal.For(Directory(), concurred, reviewed),
                        concurred,
                        reviewed));

                Assert.False(
                    string.IsNullOrWhiteSpace(sentence),
                    $"{concurredCount}/{reviewedCount} produced no sentence");
            }
        }
    }

    [Fact]
    public void EVERYProposedRowComesBackWithAReason()
    {
        // 🔴 INCLUDING THE ONES THAT ARRIVED. A row that simply vanishes from the
        // result is one nobody can ask about - and the count on screen would not
        // match what the address offered.
        var reviewed = new List<ProjectApprovalEntry>
        {
            new() { OrganizationName = "Нэг" },
            new() { OrganizationName = "Хоёр" },
        };

        IReadOnlyList<OfficialsProposalRow> proposed =
            OfficialsProposal.For(Directory(), [], reviewed);
        IReadOnlyList<OfficialsProposalOutcome> outcomes =
            OfficialsProposalPlan.For(proposed, [], reviewed);

        Assert.Equal(proposed.Count, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.True(Enum.IsDefined(outcome.Skip)));
    }

    private static List<ProjectApprovalEntry> Rows(int count, string prefix) =>
        Enumerable.Range(0, count)
            .Select(index => new ProjectApprovalEntry { OrganizationName = prefix + index })
            .ToList();

    /// <summary>What the address offers a project that has nothing yet.</summary>
    private static IReadOnlyList<OfficialsProposalRow> Offered() =>
        OfficialsProposal.For(Directory(), [], []);

    /// <summary>A fixture. These are not real officials - no source names them.</summary>
    private static IReadOnlyList<OfficialsDirectoryEntry> Directory() =>
    [
        Official(OfficialBodyKind.EmergencyManagement, "Онцгой байдлын хэлтэс"),
        Official(OfficialBodyKind.PublicHealth, "Эрүүл мэндийн төв"),
        Official(OfficialBodyKind.UrbanPlanning, "Хот байгуулалтын алба"),
    ];

    private static OfficialsDirectoryEntry Official(
        OfficialBodyKind kind,
        string organizationName) => new()
        {
            UnitCode = Bayangol,
            Kind = kind,
            OrganizationName = organizationName,
            PositionTitle = "Албан тушаал",
            PersonName = "Тест",
        };
}
