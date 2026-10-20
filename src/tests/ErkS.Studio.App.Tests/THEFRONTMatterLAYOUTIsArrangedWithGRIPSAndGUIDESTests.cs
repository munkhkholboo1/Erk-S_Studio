using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Owner, 2026-10-20: «бичих биш өөрийг нь идэвхжүүлээд хөөрхөн цэгэн болон
/// тохирсон гоё guide уудаар удирддаг байна». The geometry of the layout
/// editor: grips move and resize a block, edges snap to guides, columns are
/// added and spaced on the page.
/// </summary>
public sealed class THEFRONTMatterLAYOUTIsArrangedWithGRIPSAndGUIDESTests
{
    private static readonly PageRectMm Corner = new() { X = 407, Y = 376, Width = 170, Height = 34 };
    private static readonly PageRectMm Inner = new() { X = 20, Y = 10, Width = 557, Height = 400 };

    private static FrontMatterBlockLayout Note() => new()
    {
        Kind = SheetFrontMatterKinds.ExplanatoryNote,
        X = 300,
        Y = 100,
        Width = 120,
        Height = 200,
        Columns = 2,
        ColumnGapMm = 5,
    };

    private static (List<double> Xs, List<double> Ys) Guides() =>
        FrontMatterLayoutEditing.Guides(582, 420, Corner, Inner, []);

    [Fact]
    public void DraggingNearTheCornerTableSnapsOntoIt()
    {
        (List<double> xs, List<double> ys) = Guides();

        // 108 mm right and a little: the left edge lands 1 mm short of the
        // corner table's left edge, and snaps to it.
        FrontMatterDragResult result = FrontMatterLayoutEditing.Drag(Note(), FrontMatterGrip.Move, 106, 74.5, xs, ys);

        Assert.Equal(Corner.X, result.Block.X, 6);
        Assert.Equal(Corner.X, result.GuideX);
        Assert.Equal(Corner.Y, result.Block.Y + result.Block.Height, 6);
        Assert.Equal(Corner.Y, result.GuideY);
    }

    [Fact]
    public void AFreeDragFarFromEveryGuideSnapsToNothing()
    {
        FrontMatterDragResult result = FrontMatterLayoutEditing.Drag(Note(), FrontMatterGrip.Move, 13, 17, [], []);

        Assert.Equal(313, result.Block.X, 6);
        Assert.Equal(117, result.Block.Y, 6);
        Assert.Null(result.GuideX);
        Assert.Null(result.GuideY);
    }

    [Fact]
    public void AGripResizesOnlyItsEdgesAndNeverBelowTheMinimum()
    {
        FrontMatterDragResult wider = FrontMatterLayoutEditing.Drag(Note(), FrontMatterGrip.East, 30, 99, [], []);
        Assert.Equal(300, wider.Block.X, 6);
        Assert.Equal(150, wider.Block.Width, 6);
        Assert.Equal(200, wider.Block.Height, 6);

        FrontMatterDragResult collapsed = FrontMatterLayoutEditing.Drag(Note(), FrontMatterGrip.NorthWest, 500, 500, [], []);
        Assert.Equal(FrontMatterBlockLayout.MinSizeMm, collapsed.Block.Width, 6);
        Assert.Equal(FrontMatterBlockLayout.MinSizeMm, collapsed.Block.Height, 6);
        Assert.Equal(420, collapsed.Block.X + collapsed.Block.Width, 6);
    }

    [Fact]
    public void TheGapGripSpacesTheColumnsAndColumnsCanBeAddedOrRemoved()
    {
        FrontMatterDragResult spaced = FrontMatterLayoutEditing.Drag(Note(), FrontMatterGrip.ColumnGap, 2.5, 0, [], []);
        Assert.Equal(10, spaced.Block.ColumnGapMm, 6);

        Assert.Equal(3, FrontMatterLayoutEditing.WithColumns(Note(), 3).Columns);
        Assert.Equal(1, FrontMatterLayoutEditing.WithColumns(Note(), 0).Columns);
        Assert.Equal(FrontMatterBlockLayout.MaxColumns, FrontMatterLayoutEditing.WithColumns(Note(), 12).Columns);
        // Four columns of a 40 mm block would be narrower than the minimum.
        FrontMatterBlockLayout narrow = Note();
        narrow.Width = 40;
        Assert.Equal(2, FrontMatterLayoutEditing.WithColumns(narrow, 4).Columns);
    }

    [Fact]
    public void TheEditorIsOnTheAlbumRibbonAndSavesOnTheAlbum()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string workspaces = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.Workspaces.cs"));

        Assert.Contains("documentGroup.Children.Add(frontMatterLayout);", workspaces, StringComparison.Ordinal);
        int editor = workspaces.IndexOf("new FrontMatterLayoutEditorWindow(", StringComparison.Ordinal);
        int apply = workspaces.IndexOf("matter.Layout = dialog.Result.ToList();", editor, StringComparison.Ordinal);
        int save = workspaces.IndexOf("ApplyWorkingDrawingFrontMatter(matter);", apply, StringComparison.Ordinal);
        Assert.True(editor > 0 && apply > editor && save > apply);
        Assert.Contains("matter.SourceMoves = dialog.MovesResult.ToList();", workspaces, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEditorShowsTheFrontMatterAsItWillPrint()
    {
        // Owner, 2026-10-20: «хэмжээг тохируулахад текст ямар байдалтай болох
        // нь мэдэгдэхгүй байх нь эрсдэлтэй». The editor is given the album's own
        // writer to draw the arranged blocks, and says what did not fit.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string app = Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App");
        string workspaces = File.ReadAllText(Path.Combine(app, "ShellView.Workspaces.cs"));
        string window = File.ReadAllText(Path.Combine(app, "FrontMatterLayoutEditorWindow.cs"));

        Assert.Contains("arranged => RenderFrontMatterPreviewAsync(page, matter, arranged))", workspaces, StringComparison.Ordinal);
        Assert.Contains("PdfSharpAlbumWriter.WriteFrontMatterPreview(request, page, matter, arranged, path)", workspaces, StringComparison.Ordinal);
        Assert.Contains("StudioBitmapClearWhite.Apply(bitmap)", workspaces, StringComparison.Ordinal);
        Assert.Contains("surface.Children.Add(previewImage);", window, StringComparison.Ordinal);
        Assert.Contains("ShowPreviewWarnings();", window, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePapersWhiteIsMadeClearAndTheInkIsKept()
    {
        byte[] pixels =
        [
            255, 255, 255, 255, // white paper
            250, 249, 248, 255, // near-white paper
            0, 0, 0, 255,       // ink
            200, 30, 30, 255,   // coloured ink
        ];
        System.Windows.Media.Imaging.BitmapSource source = System.Windows.Media.Imaging.BitmapSource.Create(
            4, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16);

        System.Windows.Media.Imaging.BitmapSource clear = StudioBitmapClearWhite.Apply(source);

        byte[] result = new byte[16];
        clear.CopyPixels(result, 16, 0);
        Assert.Equal(0, result[3]);
        Assert.Equal(0, result[7]);
        Assert.Equal(255, result[11]);
        Assert.Equal(255, result[15]);
    }
}
