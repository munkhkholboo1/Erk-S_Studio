using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Owner, 2026-10-20: «тайлбар бичгийг булангийн хүснэгтийн дээр байрлуул.
/// давхцлууд ажиглагдаад байна … зөөж байрлуулахаас гадна баганын тоо болон
/// тэдгээрийн хоорондох зай баганын урт өргөнийг хялбархан тохируулж болдог
/// болго».
/// </summary>
public sealed class THEFRONTMatterBLOCKSStandWhereTheOWNERPutsThemTests
{
    // The Batnasan 01 (582 x 420): the frames PFR reserves.
    private static Dictionary<string, PageRectSpec> Frames() => new(StringComparer.Ordinal)
    {
        [SheetFrontMatterKinds.DrawingList] = new() { X = 30, Y = 22, Width = 107, Height = 378 },
        [SheetFrontMatterKinds.ExplanatoryNote] = new() { X = 150, Y = 27, Width = 123, Height = 368 },
        [SheetFrontMatterKinds.AlbumComposition] = new() { X = 476, Y = 270, Width = 96, Height = 60 },
        [SheetFrontMatterKinds.TechnicalIndicators] = new() { X = 476, Y = 330, Width = 96, Height = 46 },
    };

    private static readonly PageRectMm Corner = new() { X = 407, Y = 376, Width = 170, Height = 34 };

    private static FrontMatterBlockLayout Block(IReadOnlyList<FrontMatterBlockLayout> blocks, string kind) =>
        blocks.Single(block => block.Kind == kind);

    [Fact]
    public void ByDefaultTheNoteStandsOnTheCornerTableInTwoColumns()
    {
        IReadOnlyList<FrontMatterBlockLayout> blocks = WorkingDrawingFrontMatterPlacement.Resolve(Frames(), Corner, 10, null);

        FrontMatterBlockLayout note = Block(blocks, SheetFrontMatterKinds.ExplanatoryNote);
        Assert.Equal(Corner.X, note.X, 6);
        Assert.Equal(Corner.Width, note.Width, 6);
        Assert.Equal(Corner.Y - WorkingDrawingFrontMatterPlacement.StackGapMm, note.Y + note.Height, 6);
        Assert.Equal(2, note.Columns);

        FrontMatterBlockLayout composition = Block(blocks, SheetFrontMatterKinds.AlbumComposition);
        FrontMatterBlockLayout indicators = Block(blocks, SheetFrontMatterKinds.TechnicalIndicators);
        Assert.True(composition.Y < indicators.Y && indicators.Y + indicators.Height < note.Y);
        Assert.Equal(Corner.X, indicators.X, 6);

        FrontMatterBlockLayout list = Block(blocks, SheetFrontMatterKinds.DrawingList);
        Assert.Equal(30, list.X, 6);
        Assert.Equal(378, list.Height, 6);
    }

    [Fact]
    public void WhatTheOwnerPlacedWins()
    {
        var saved = new List<FrontMatterBlockLayout>
        {
            new() { Kind = SheetFrontMatterKinds.ExplanatoryNote, X = 150, Y = 30, Width = 240, Height = 200, Columns = 3, ColumnGapMm = 6 },
        };

        FrontMatterBlockLayout note = Block(
            WorkingDrawingFrontMatterPlacement.Resolve(Frames(), Corner, 10, saved),
            SheetFrontMatterKinds.ExplanatoryNote);

        Assert.Equal(150, note.X, 6);
        Assert.Equal(3, note.Columns);
        Assert.Equal(6, note.ColumnGapMm, 6);
        Assert.Equal((240 - 12) / 3.0, note.ColumnWidthMm, 6);
    }

    [Fact]
    public void ASheetWithNoRoomAboveTheCornerTableKeepsTheSourcesFrames()
    {
        // An A3 sheet: the corner table near the top of a short page.
        var corner = new PageRectMm { X = 245, Y = 120, Width = 170, Height = 34 };

        FrontMatterBlockLayout note = Block(
            WorkingDrawingFrontMatterPlacement.Resolve(Frames(), corner, 10, null),
            SheetFrontMatterKinds.ExplanatoryNote);

        Assert.Equal(150, note.X, 6);
        Assert.Equal(1, note.Columns);
    }

    [Fact]
    public void TheLayoutTravelsWithTheFrontMatterAndIsKeptInBounds()
    {
        var matter = new WorkingDrawingFrontMatter
        {
            Layout =
            [
                new() { Kind = SheetFrontMatterKinds.ExplanatoryNote, X = -5, Y = 10, Width = 2, Height = 50, Columns = 9 },
                new() { Kind = "unknown", X = 1, Y = 1, Width = 20, Height = 20 },
            ],
        };

        matter.Normalize();
        WorkingDrawingFrontMatter copy = matter.Clone();

        FrontMatterBlockLayout note = Assert.Single(copy.Layout);
        Assert.Equal(0, note.X, 6);
        Assert.Equal(FrontMatterBlockLayout.MinSizeMm, note.Width, 6);
        Assert.Equal(FrontMatterBlockLayout.MaxColumns, note.Columns);
    }
}
