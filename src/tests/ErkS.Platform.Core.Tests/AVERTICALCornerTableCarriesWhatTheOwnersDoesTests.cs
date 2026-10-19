using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The vertical working-drawing corner table, 70 × 90 mm.
///
/// 🔴 IT PRINTED THREE THINGS: the company, the sheet title and the sheet number.
/// No roles and no names, no scale, no mark, no ciphers, no date - a table that
/// LOOKED drawn and carried almost nothing a signed sheet needs. The horizontal
/// table had all of it; the vertical one had been left as a sketch.
///
/// The layout here is MEASURED, not invented: the vector lines of the owner's
/// own working drawing (Grand Mark AZ 2026, sheet БА-03, a vertical table) read
/// with PyMuPDF on 2026-10-19 - rows at 0/5/35/40/45/50/66/74/82/90 mm, the
/// logo cell 25 × 25, the role rows split label | signature | name at 25 and
/// 48.7 mm, the bottom two rows split at 25 and 50 mm.
/// </summary>
public sealed class AVERTICALCornerTableCarriesWhatTheOwnersDoesTests
{
    [Fact]
    public void TheThreeRolesAndTheirNamesArePrinted()
    {
        IReadOnlyList<WorkingTitleBlockCell> cells = Cells();

        Assert.Equal("Э.Мөнххолбоо", CellAt(cells, x: 59, y: 37.5).Text);
        Assert.Equal("М.Будбаясгалан", CellAt(cells, x: 59, y: 42.5).Text);
        Assert.Equal("М.Насанжаргал", CellAt(cells, x: 59, y: 47.5).Text);
        Assert.Equal("Архитектор", CellAt(cells, x: 10, y: 37.5).Text);
        Assert.Equal("Гүйцэтгэсэн", CellAt(cells, x: 10, y: 42.5).Text);
        Assert.Equal("Шалгасан", CellAt(cells, x: 10, y: 47.5).Text);
    }

    [Fact]
    public void TheStageRowNamesTheMark()
    {
        Assert.Contains(
            Cells(),
            cell => cell.Text == "АЖЛЫН ЗУРАГ /БА/" && cell.YMm < 5);
    }

    [Fact]
    public void ScaleMarkSheetCiphersAndDateAreAllThere()
    {
        string[] texts = Cells().Select(cell => cell.Text).ToArray();

        Assert.Contains("М1:100", texts);
        Assert.Contains("БА", texts);
        Assert.Contains("11", texts);
        // Unfilled ciphers keep their labels, so the cell reads as a form field
        // rather than a rendering fault - the rule the horizontal table follows.
        Assert.Contains("Е.Г.Шифр:", texts);
        Assert.Contains("Т.Г.Шифр:", texts);
        Assert.Contains("Огноо", texts);
    }

    [Fact]
    public void AnEnteredCipherIsPrinted()
    {
        var project = Project();
        project.GeneralDesignCipher = "АХО-24/2025";

        Assert.Contains(Cells(project), cell => cell.Text == "АХО-24/2025");
    }

    [Fact]
    public void EveryCellStaysInsideTheTable()
    {
        foreach (WorkingTitleBlockCell cell in Cells())
        {
            Assert.InRange(cell.XMm, 0, 70);
            Assert.InRange(cell.YMm, 0, 90);
            Assert.InRange(cell.XMm + cell.WidthMm, 0, 70.001);
            Assert.InRange(cell.YMm + cell.HeightMm, 0, 90.001);
        }
    }

    [Fact]
    public void TheGridIsTheMeasuredOne()
    {
        double[] rows = VerticalWorkingTitleBlockLayout.Lines
            .Where(line => Math.Abs(line.Y0Mm - line.Y1Mm) < 0.001 &&
                           line.X0Mm <= 0.001 && line.X1Mm >= 69.999)
            .Select(line => line.Y0Mm)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal([5d, 35d, 40d, 45d, 50d, 66d, 74d, 82d], rows);
    }

    private static IReadOnlyList<WorkingTitleBlockCell> Cells(AlbumProject? project = null) =>
        VerticalWorkingTitleBlockLayout.Cells(
            project ?? Project(),
            sheetTitle: "ЗООРИЙН ДАВХРЫН БАЙГУУЛАЛТ",
            sheetNumber: "11",
            mark: "БА",
            scale: "М1:100",
            roleAssignments: null,
            sourceRoles:
            [
                new SheetRoleSignature { RoleCode = SheetRoleCodes.Architect, FullName = "Э.Мөнххолбоо" },
                new SheetRoleSignature { RoleCode = SheetRoleCodes.PreparedBy, FullName = "М.Будбаясгалан" },
                new SheetRoleSignature { RoleCode = SheetRoleCodes.CheckedBy, FullName = "М.Насанжаргал" },
            ]);

    private static AlbumProject Project() => new()
    {
        Name = "ИРГЭН БАТНАСАНГИЙН ОРОН СУУЦНЫ БАРИЛГА",
        Company = new CompanyProfile
        {
            Name = "\"Алтанхөхийн Очир\" ХХК",
            ShortName = "Алтанхөхийн Очир",
            Address = "УБ хот, БЗ дүүрэг, 3-р хороо, 26-70 тоот",
            Phone = "88111923",
        },
    };

    private static WorkingTitleBlockCell CellAt(
        IReadOnlyList<WorkingTitleBlockCell> cells,
        double x,
        double y) =>
        cells.Single(cell =>
            !string.IsNullOrWhiteSpace(cell.Text) &&
            x >= cell.XMm && x <= cell.XMm + cell.WidthMm &&
            y >= cell.YMm && y <= cell.YMm + cell.HeightMm);
}
