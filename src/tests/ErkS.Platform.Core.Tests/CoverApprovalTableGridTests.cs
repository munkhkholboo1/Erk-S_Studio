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
    public void TheSkinChoosesTheGrid_AndNothingElseDoes()
    {
        Assert.Same(CoverApprovalTableGrid.WorkingDrawing, CoverApprovalTableGrid.For(workingDrawing: true));
        Assert.Same(CoverApprovalTableGrid.Concept, CoverApprovalTableGrid.For(workingDrawing: false));
    }
}
