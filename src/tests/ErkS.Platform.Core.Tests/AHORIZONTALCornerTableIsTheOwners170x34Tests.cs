using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The horizontal working-drawing corner table is 170 × 34 mm.
///
/// 🔴 STUDIO DREW 180 × 36, A SIZE NO SHEET OF THE OWNER'S HAS. It came from
/// the 2026-09-06 space contract, whose numbers were read from PFR's code -
/// and that code matched no family PFR actually places. PFR measured its own
/// TitleBlockHorizontal.rfa at 170 × 34 on 2026-10-19 and the owner confirmed
/// it («170х34»). Measured here too, from the owner's own sheets with PyMuPDF:
/// Eco Garden AZ (EG BA 05-10-24.pdf, pages 2, 3, 6) and the Grand Mark AZ 2026
/// cover both put the table at x 239 → 409, a width of 170, against the inner
/// frame's bottom-right corner.
///
/// The grid is Eco Garden's: columns 25 | 45 | 70 | 95 | 120 | 145, rows 9
/// (project), 15 (sheet title), then three role rows on the left and two
/// metadata rows on the right. Eco Garden's table is 33 high; the family is
/// 34, and the extra millimetre goes to the bottom rows.
/// </summary>
public sealed class AHORIZONTALCornerTableIsTheOwners170x34Tests
{
    [Fact]
    public void TheReservedSpaceIs170By34()
    {
        Assert.Equal(170d, WorkingDrawingPageLayout.HorizontalTitleBlockWidthMm);
        Assert.Equal(34d, WorkingDrawingPageLayout.HorizontalTitleBlockHeightMm);
        Assert.Equal(170d, HorizontalWorkingTitleBlockLayout.WidthMm);
        Assert.Equal(34d, HorizontalWorkingTitleBlockLayout.HeightMm);
    }

    [Fact]
    public void TheColumnsAreTheMeasuredOnes()
    {
        double[] columns = HorizontalWorkingTitleBlockLayout.Lines
            .Where(line => Math.Abs(line.X0Mm - line.X1Mm) < 0.001)
            .Select(line => line.X0Mm)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal([25d, 45d, 70d, 95d, 120d, 145d], columns);
    }

    [Fact]
    public void TheThreeRolesAndTheirNamesArePrinted()
    {
        IReadOnlyList<WorkingTitleBlockCell> cells = Cells();

        Assert.Equal("Архитектор", CellAt(cells, x: 35, y: 18).Text);
        Assert.Equal("Гүйцэтгэсэн", CellAt(cells, x: 35, y: 24).Text);
        Assert.Equal("Шалгасан", CellAt(cells, x: 35, y: 30).Text);
        Assert.Equal("Э.Мөнххолбоо", CellAt(cells, x: 57, y: 18).Text);
        Assert.Equal("М.Будбаясгалан", CellAt(cells, x: 57, y: 24).Text);
        Assert.Equal("М.Насанжаргал", CellAt(cells, x: 57, y: 30).Text);
    }

    [Fact]
    public void TheMetadataSitsInTheMeasuredCells()
    {
        IReadOnlyList<WorkingTitleBlockCell> cells = Cells();

        Assert.StartsWith("ЕГ шифр", CellAt(cells, x: 107, y: 19).Text);
        Assert.Contains("1:100", CellAt(cells, x: 132, y: 19).Text);
        Assert.StartsWith("Огноо", CellAt(cells, x: 157, y: 19).Text);
        Assert.StartsWith("ТГ шифр", CellAt(cells, x: 107, y: 29).Text);
        Assert.Contains("БА", CellAt(cells, x: 132, y: 29).Text);
        Assert.Contains("11", CellAt(cells, x: 157, y: 29).Text);
        Assert.Equal("ЗООРИЙН ДАВХРЫН БАЙГУУЛАЛТ", CellAt(cells, x: 90, y: 12).Text);
    }

    [Fact]
    public void EveryCellStaysInsideTheTable()
    {
        foreach (WorkingTitleBlockCell cell in Cells())
        {
            Assert.InRange(cell.XMm, 0, 170);
            Assert.InRange(cell.YMm, 0, 34);
            Assert.InRange(cell.XMm + cell.WidthMm, 0, 170.001);
            Assert.InRange(cell.YMm + cell.HeightMm, 0, 34.001);
        }
    }

    private static IReadOnlyList<WorkingTitleBlockCell> Cells() =>
        HorizontalWorkingTitleBlockLayout.Cells(
            new AlbumProject
            {
                Name = "ИРГЭН БАТНАСАНГИЙН ОРОН СУУЦНЫ БАРИЛГА",
                Company = new CompanyProfile { Name = "\"Алтанхөхийн Очир\" ХХК" },
            },
            sheetTitle: "ЗООРИЙН ДАВХРЫН БАЙГУУЛАЛТ",
            sheetNumber: "11",
            mark: "БА",
            scale: "1:100",
            roleAssignments: null,
            sourceRoles:
            [
                new SheetRoleSignature { RoleCode = SheetRoleCodes.Architect, FullName = "Э.Мөнххолбоо" },
                new SheetRoleSignature { RoleCode = SheetRoleCodes.PreparedBy, FullName = "М.Будбаясгалан" },
                new SheetRoleSignature { RoleCode = SheetRoleCodes.CheckedBy, FullName = "М.Насанжаргал" },
            ]);

    private static WorkingTitleBlockCell CellAt(
        IReadOnlyList<WorkingTitleBlockCell> cells,
        double x,
        double y) =>
        cells.Single(cell =>
            !string.IsNullOrWhiteSpace(cell.Text) &&
            x >= cell.XMm && x <= cell.XMm + cell.WidthMm &&
            y >= cell.YMm && y <= cell.YMm + cell.HeightMm);
}
