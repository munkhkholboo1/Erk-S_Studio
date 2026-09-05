using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// One routine draws two covers, and from 2026-09-06 they answer to different
/// authorities: the working-drawing cover must reproduce what Revit drew (PFR's
/// cover code is deleted, so Studio is the only remaining producer), while the
/// concept album's cover is Studio's own and the user did not ask for it to
/// move.
///
/// The two grids agree on the table's outer bounds and differ by 5-8 mm on four
/// interior columns - the reason the difference was invisible, and the reason a
/// single edit to "the cover geometry" would silently reach the wrong document.
/// </summary>
public sealed class CoverApprovalTableGridTests
{
    [Fact]
    public void TheWorkingDrawingGridIsTheCONTRACTSNumbers()
    {
        // Not ours to choose. Written out one by one rather than derived, so
        // that a change to any of them is a change to this test as well.
        CoverApprovalTableGrid grid = CoverApprovalTableGrid.WorkingDrawing;

        Assert.Equal(68.275, grid.TableLeft, 3);
        Assert.Equal(138.275, grid.ReviewRoleRight, 3);
        Assert.Equal(166.275, grid.ReviewNameRight, 3);
        Assert.Equal(196.275, grid.ReviewRight, 3);
        Assert.Equal(226.275, grid.CompanyLogoRight, 3);
        Assert.Equal(292.975, grid.CompanyRoleRight, 3);
        Assert.Equal(321.725, grid.CompanyNameRight, 3);
        Assert.Equal(351.725, grid.TableRight, 3);
        Assert.Equal(169.86, grid.TableTop, 3);
        Assert.Equal(161.86, grid.ColumnHeaderTop, 3);
        Assert.Equal(153.86, grid.ColumnHeaderBottom, 3);
        Assert.Equal(93.86, grid.RowBottom, 3);
    }

    [Fact]
    public void TheConceptGridKeepsTheGeometryTheUserAlreadyReceives()
    {
        CoverApprovalTableGrid grid = CoverApprovalTableGrid.Concept;

        Assert.Equal(131.275, grid.ReviewRoleRight, 3);
        Assert.Equal(171.275, grid.ReviewNameRight, 3);
        Assert.Equal(284.975, grid.CompanyRoleRight, 3);
        Assert.Equal(326.725, grid.CompanyNameRight, 3);
    }

    [Fact]
    public void TheTwoGridsAgreeOnEveryOuterBound()
    {
        // This is why the difference could not be seen: the table's frame is
        // identical in both, so it looks correct either way.
        CoverApprovalTableGrid concept = CoverApprovalTableGrid.Concept;
        CoverApprovalTableGrid working = CoverApprovalTableGrid.WorkingDrawing;

        Assert.Equal(working.TableLeft, concept.TableLeft, 3);
        Assert.Equal(working.TableRight, concept.TableRight, 3);
        Assert.Equal(working.ReviewRight, concept.ReviewRight, 3);
        Assert.Equal(working.CompanyLogoRight, concept.CompanyLogoRight, 3);
        Assert.Equal(working.RowBottom, concept.RowBottom, 3);
        Assert.Equal(working.ColumnHeaderTop, concept.ColumnHeaderTop, 3);
        Assert.Equal(working.Width, concept.Width, 3);
    }

    [Fact]
    public void TheFourINTERIORColumnsMustNotBeCollapsedIntoOne()
    {
        // The guard the user's decision needs: applying the contract to both
        // skins would move a document nobody asked to change, and copying the
        // concept numbers onto the working cover would stop reproducing Revit.
        // Either mistake reads as tidying up duplicate constants.
        CoverApprovalTableGrid concept = CoverApprovalTableGrid.Concept;
        CoverApprovalTableGrid working = CoverApprovalTableGrid.WorkingDrawing;

        Assert.NotEqual(working.ReviewRoleRight, concept.ReviewRoleRight);
        Assert.NotEqual(working.ReviewNameRight, concept.ReviewNameRight);
        Assert.NotEqual(working.CompanyRoleRight, concept.CompanyRoleRight);
        Assert.NotEqual(working.CompanyNameRight, concept.CompanyNameRight);
    }

    [Fact]
    public void BothGridsSplitTheSameSpanWithoutLosingOrInventingMillimetres()
    {
        // Two partitions of one 155.45 mm right block. If either stopped adding
        // up, a column would overhang the table's own edge.
        foreach (CoverApprovalTableGrid grid in
                 new[] { CoverApprovalTableGrid.Concept, CoverApprovalTableGrid.WorkingDrawing })
        {
            double logo = grid.CompanyLogoRight - grid.ReviewRight;
            double role = grid.CompanyRoleRight - grid.CompanyLogoRight;
            double name = grid.CompanyNameRight - grid.CompanyRoleRight;
            double signature = grid.TableRight - grid.CompanyNameRight;

            Assert.Equal(30.0, logo, 3);
            Assert.Equal(155.45, logo + role + name + signature, 3);
            Assert.True(role > 0 && name > 0 && signature > 0);
        }
    }

    [Fact]
    public void TheFOOTERFollowsTheSkinTooAndTheTwoAreFarApart()
    {
        // 39 mm apart, and both were drawn by one line reading one constant.
        // The footer is not part of the table and travels with it anyway: a
        // second selector for "the rest of the cover" is how two halves of one
        // page end up drawing two different covers.
        Assert.Equal(65.0, CoverApprovalTableGrid.WorkingDrawing.CityCenterY, 3);
        Assert.Equal(46.0, CoverApprovalTableGrid.WorkingDrawing.YearCenterY, 3);
        Assert.Equal(26.125, CoverApprovalTableGrid.Concept.CityCenterY, 3);
        Assert.Equal(15.625, CoverApprovalTableGrid.Concept.YearCenterY, 3);
    }

    [Fact]
    public void TheWorkingTableReachesEightMillimetresHigher()
    {
        // The contract's table top is 169.86; the concept skin stops at 161.86
        // and draws its headers above that line anyway. Same band, two names -
        // which is exactly the kind of agreement that hides a difference.
        Assert.Equal(169.86, CoverApprovalTableGrid.WorkingDrawing.TableTop, 3);
        Assert.Equal(161.86, CoverApprovalTableGrid.Concept.TableTop, 3);
    }

    [Fact]
    public void TheWriterReadsTheTableTopAndFooterFromTheGrid()
    {
        // Each of these was a literal in the drawing code, so the working cover
        // kept the concept table's height and footer whatever the grid said.
        string writer = ReadWriterSource();

        Assert.Contains("double y1 = grid.TableTop;", writer, StringComparison.Ordinal);
        Assert.Contains("grid.CityCenterY", writer, StringComparison.Ordinal);
        Assert.Contains("grid.YearCenterY", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("CoverCenteredRect(210.0, 26.125", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWriterACTUALLYPicksTheGridFromTheSkin()
    {
        // The grids are worth having only where they are asked. Until this
        // commit the writer held its own file-level constants aliasing the
        // CONCEPT numbers, so the working-drawing cover was drawn with the
        // concept column split no matter what any grid said - and a unit test
        // of the grids alone stays green through exactly that.
        string writer = ReadWriterSource();

        Assert.Contains(
            "CoverApprovalTableGrid.For(drawWorkingDrawingEtalon)",
            writer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheWriterKeepsNoPrivateCopyOfTheCoverColumns()
    {
        // The aliases that made the two covers share one geometry. A new one
        // would restore the defect silently: the table would still look right.
        string writer = ReadWriterSource();

        Assert.DoesNotContain("private const double CoverTableLeftMm", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("private const double CoverCompanyRoleRightMm", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("private const double CoverReviewRoleRightMm", writer, StringComparison.Ordinal);
    }

    private static string ReadWriterSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        Assert.Fail("PdfSharpAlbumWriter.cs was not found; this test reads it from source");
        return "";
    }

    [Fact]
    public void TheSkinChoosesTheGrid_AndNothingElseDoes()
    {
        Assert.Same(CoverApprovalTableGrid.WorkingDrawing, CoverApprovalTableGrid.For(workingDrawing: true));
        Assert.Same(CoverApprovalTableGrid.Concept, CoverApprovalTableGrid.For(workingDrawing: false));
    }
}
