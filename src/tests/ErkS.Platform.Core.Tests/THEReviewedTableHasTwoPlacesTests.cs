using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// ХЯНАСАН has two places, and they come from the measured drawing.
///
/// 🔴 THE TABLE WAS DRAWN AND STORED NOWHERE UNTIL 2026-09-12. The owner named
/// its occupants plainly - «нөгөө талд хот байгуулалтын газрын 2 албан тушаалтан
/// тэгээд л болоо» - and the measured A3 agrees: twoTablePairs.top
/// .rightRowHeightsMm is [20.0, 20.0]. Without a list of its own, «find the
/// officials from the address» was half a feature: found, then thrown away.
///
/// 🔴 AND THE TWO ROWS WERE RIGHT BY ACCIDENT. The row heights fell out of an
/// even split of a 40 mm body, which divides by two to exactly the drawing's
/// numbers. Nothing recorded that agreement, so the day the body height moved,
/// the table the owner measured would have drifted off the drawing in silence.
/// </summary>
public sealed class THEReviewedTableHasTwoPlacesTests
{
    [Fact]
    public void TWORowsAreTheMeasurementAndNotADivision()
    {
        // The drawing's own numbers, and they must not depend on 40 being
        // divisible by 2.
        Assert.Equal(
            [20.0, 20.0],
            ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo);
        Assert.Equal(
            ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo,
            ConceptCoverSheetGrid.UpperRowHeights(2));

        // 🔴 AND THE VALUES CANNOT PROVE IT, WHICH IS THE WHOLE POINT OF THIS
        // FILE. A mutation that disabled the measured lookup entirely left the
        // assertions above green: the even split of a 40 mm body IS [20, 20], so
        // the right answer arrives down either path and the check shares the very
        // coincidence it was written to catch.
        //
        // The two paths differ in one observable way - the measured branch hands
        // back the shared table, the division builds a new list - so that is what
        // separates them. It is an implementation detail, and here the
        // implementation detail IS the claim: where did this number come from?
        Assert.Same(
            ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo,
            ConceptCoverSheetGrid.UpperRowHeights(2));
        Assert.Same(
            ConceptCoverSheetGrid.MeasuredUpperRowHeights,
            ConceptCoverSheetGrid.UpperRowHeights(3));

        // The positive control for that instrument: a count with no drawing
        // behind it must NOT come back as either measured table.
        IReadOnlyList<double> divided = ConceptCoverSheetGrid.UpperRowHeights(4);
        Assert.NotSame(ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo, divided);
        Assert.NotSame(ConceptCoverSheetGrid.MeasuredUpperRowHeights, divided);
        Assert.Equal(4, divided.Count);
    }

    [Fact]
    public void THEMeasuredCountsAgreeWithTheSharedContract()
    {
        // 🔴 READ FROM THE CONTRACT, NOT RETYPED. A number copied into a test is
        // a number that stops tracking the drawing the first time the drawing
        // changes - and this file exists because an unrecorded agreement drifted.
        //
        // From the VENDORED copy, never from `_shared`: that folder belongs to
        // another repository, and a test that walks up to it passes on the one
        // machine with both checked out and fails everywhere else. The gate
        // caught this file doing exactly that. SharedContractCopies compares the
        // copy against the original wherever both exist, so the copy cannot rot.
        string contract = SharedContractCopies.Read(SharedContractCopies.ConceptCoverA3);

        Assert.Contains(
            "\"rightRowHeightsMm\": [",
            contract.Replace("\r\n", "\n"),
            StringComparison.Ordinal);

        IReadOnlyList<double> right = NumbersAfter(contract, "\"rightRowHeightsMm\"");
        IReadOnlyList<double> left = NumbersAfter(contract, "\"leftRowHeightsMm\"");

        Assert.Equal(right, ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo);
        Assert.Equal(left, ConceptCoverSheetGrid.MeasuredUpperRowHeights);
    }

    [Fact]
    public void THEThreeRowCaseIsUntouched()
    {
        // The left table keeps the measurement it already had. A change to the
        // right-hand side that quietly moved the left one would be the kind of
        // repair that costs more than it fixes.
        Assert.Equal(
            [16.0, 12.0, 12.0],
            ConceptCoverSheetGrid.UpperRowHeights(3));
    }

    [Fact]
    public void THEWriterDrawsTheReviewedTableFromItsOWNRoster()
    {
        // 🔴 ITS OWN LIST, AND NOBODY ELSE'S. The table was drawn empty for as
        // long as it had nowhere to store rows, and that was the right answer:
        // ЗӨВШӨӨРӨЛЦСӨН is the nearest list and means something else, so a form
        // printed with the wrong parties is worse than one printed blank.
        string writer = ReadPdfSource("PdfSharpAlbumWriter.ConceptCover2026.cs");

        Assert.Contains(
            "project.ApprovalWorkflow.ConceptDesign.ReviewedBy",
            writer,
            StringComparison.Ordinal);
        Assert.Contains(
            "project.ApprovalWorkflow.ConceptDesign.ConcurredBy",
            writer,
            StringComparison.Ordinal);

        // The empty literal that used to stand in for the missing roster.
        Assert.DoesNotContain("\"ХЯНАСАН.\",\n            []", writer.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void ANEmptyReviewedTableStillPrintsTWOLinesToSignOn()
    {
        // 🔴 THE MINIMUM IS THE DRAWING'S, NOT THE ROSTER'S. Both tables used to
        // take Math.Max(1, rows.Count), so an empty ХЯНАСАН printed ONE line
        // where the measured sheet has TWO - invisible for as long as the table
        // had no roster and nobody counted its empty lines.
        string writer = ReadPdfSource("PdfSharpAlbumWriter.ConceptCover2026.cs");

        Assert.Contains(
            "drawnRowMinimum: ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo.Count",
            writer,
            StringComparison.Ordinal);
        Assert.Contains(
            "Math.Max(Math.Max(1, drawnRowMinimum), rows.Count)",
            writer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THELeftTablesEmptyRowCountIsLEFTAlone()
    {
        // ⚠ AN OPEN QUESTION, DELIBERATELY NOT ANSWERED IN PASSING. The left
        // table prints ONE line when nobody has filled it while the drawing shows
        // three - the same mismatch, on the side whose row count the owner
        // deferred (№24). Settling it here would decide a deferred question with
        // no drawing behind it, so it is recorded instead.
        string writer = ReadPdfSource("PdfSharpAlbumWriter.ConceptCover2026.cs");

        Assert.Contains("drawnRowMinimum: 1", writer, StringComparison.Ordinal);
    }

    /// <summary>The numbers of the first JSON array following <paramref name="key"/>.</summary>
    private static IReadOnlyList<double> NumbersAfter(string json, string key)
    {
        int at = json.IndexOf(key, StringComparison.Ordinal);
        Assert.True(at > 0, key + " was not found in the contract");

        int open = json.IndexOf('[', at);
        int close = json.IndexOf(']', open);
        Assert.True(open > 0 && close > open, key + " is not followed by an array");

        return json[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }

    private static string ReadPdfSource(string fileName) =>
        ReadFrom(Path.Combine("src", "src", "ErkS.Platform.Pdf", fileName));

    private static string ReadFrom(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(relativePath + " was not found; this test reads it");
        return "";
    }
}
