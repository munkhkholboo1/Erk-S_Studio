using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The «ЗӨВШӨӨРӨЛЦСӨН» table stands immediately left of Studio's corner table,
/// bottom edges aligned (owner, 2026-10-20: «зөвшөөрөлцсөн хүснэгт хуудас бүр
/// дээр байрлал нь алдаатай байна» - the Revit frames sat 3 mm low and 1 mm
/// into the corner table, and above it on some sheets).
/// </summary>
public sealed class THECONSULTATIONTableStandsBesideTheCORNERTableTests
{
    [Fact]
    public void ItTouchesTheCornerTablesLeftEdgeAndSharesItsBottom()
    {
        // The B1-wide Batnasan sheet: corner table 170 x 34 at (517, 376).
        var corner = new PageRectMm { X = 517, Y = 376, Width = 170, Height = 34 };

        PageRectMm table = ConsultationTableLayout.Place(corner, 70, 34);

        Assert.Equal(447, table.X, 6);
        Assert.Equal(corner.X, table.X + table.Width, 6);
        Assert.Equal(corner.Y + corner.Height, table.Y + table.Height, 6);
        Assert.Equal(70, table.Width, 6);
        Assert.Equal(34, table.Height, 6);
    }

    [Fact]
    public void AShorterFrameStillSitsOnTheSameBottom()
    {
        var corner = new PageRectMm { X = 232, Y = 376, Width = 170, Height = 34 };

        PageRectMm table = ConsultationTableLayout.Place(corner, 70, 30);

        Assert.Equal(410, table.Y + table.Height, 6);
        Assert.Equal(380, table.Y, 6);
    }

    [Fact]
    public void TheSheetChromePlacesItFromTheCornerTable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ErkS.Platform.Pdf")))
            directory = directory.Parent;
        string writer = File.ReadAllText(Path.Combine(directory!.FullName, "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs"));

        Assert.Contains("DrawConsultationTable(gfx, project, buildPage, regions.TitleBlockArea,", writer, StringComparison.Ordinal);
        int cover = writer.IndexOf("gfx.DrawRectangle(paperBrush, new XRect(Mm(reserved.X), Mm(reserved.Y), Mm(reserved.Width), Mm(reserved.Height)));", StringComparison.Ordinal);
        int place = writer.IndexOf("ConsultationTableLayout.Place(corner, reserved.Width, reserved.Height)", StringComparison.Ordinal);
        Assert.True(place > 0 && cover > place, "the source's frame is covered, then the table drawn beside the corner table");
    }
}
