using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The working drawings' front matter - drawing list, explanatory note, the
/// set's composition and the technical indicators.
///
/// 🔴 THE OWNER'S DECISION, 2026-10-20: the 01 «ЕРӨНХИЙ ХЭСЭГ» stays Revit's
/// sheet - its site plan, its elevation, its number - and Revit leaves EMPTY
/// FRAMES there that Studio writes into. Studio does not add a page of its own
/// for them and does not push Revit's numbers along by one. The reference is
/// the owner's hand-made Batnasan sheet 01 (Arch_BN_SHEET_V1_Corrected_Working,
/// page 1): list left, note beside it, composition and indicators bottom right.
///
/// The Grand Mark values the old sheet carried (573, 5616, 1195.3, 219.78,
/// 0.18, 5.44) are another project's and must never be printed as Batnasan's.
/// </summary>
public sealed class THEGENERALPartFRONTMatterIsWrittenIntoREVITsFramesTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-platform-tests",
        Guid.NewGuid().ToString("N"));

    public THEGENERALPartFRONTMatterIsWrittenIntoREVITsFramesTests()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(workDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    // ---- the manifest ---------------------------------------------------

    [Fact]
    public void TheFourFramesTravelAndVerify()
    {
        string manifestPath = WritePackage("frames");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        SheetPackageEntry entry = result.Manifest!.Sheets.Single(sheet => sheet.Number == "01");
        Assert.Equal(SheetFrontMatterKinds.All, entry.FrontMatterAreas!.Select(frame => frame.Kind));
        Assert.Equal(102, entry.FrontMatterAreas![0].Area!.Width);
    }

    [Fact]
    public void AFrameStudioCannotNameIsRefused()
    {
        string manifestPath = WritePackage("unknown");
        RewriteFrontSheet(manifestPath, entry => entry["frontMatterAreas"]![1]!["kind"] = "summary");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("unknown front-matter", StringComparison.Ordinal));
    }

    [Fact]
    public void AFrameDeclaredTwiceIsRefused()
    {
        string manifestPath = WritePackage("twice");
        RewriteFrontSheet(manifestPath, entry => entry["frontMatterAreas"]![1]!["kind"] = "drawing-list");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("declared twice", StringComparison.Ordinal));
    }

    [Fact]
    public void AFrameOffThePageIsRefused()
    {
        string manifestPath = WritePackage("off-page");
        RewriteFrontSheet(manifestPath, entry => entry["frontMatterAreas"]![3]!["area"]!["x"] = 560);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("outside the page", StringComparison.Ordinal));
    }

    [Fact]
    public void AFrameWithoutAnAreaIsRefused()
    {
        string manifestPath = WritePackage("no-area");
        RewriteFrontSheet(manifestPath, entry => entry["frontMatterAreas"]![2]!.AsObject().Remove("area"));

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("positive finite rectangle", StringComparison.Ordinal));
    }

    [Fact]
    public void FramesAreRefusedBelowSchema9()
    {
        string manifestPath = WritePackage("old-schema");
        RewriteManifest(manifestPath, manifest => manifest["schemaVersion"] = 8);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("front-matter", StringComparison.Ordinal) &&
                                                issue.Contains("9", StringComparison.Ordinal));
    }

    [Fact]
    public void ASheetWithoutFramesWritesNothingNew()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new SheetPackageEntry(), SheetPackageJson.Options);

        Assert.DoesNotContain("frontMatter", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the album place ------------------------------------------------

    [Fact]
    public void TheSheetWithTheListFrameTakesThe01Place()
    {
        AlbumDefinition album = BuildingWorkingDrawingAlbumTemplate.CreateDefinition("");
        var page = new AlbumPageDefinition { SheetKey = "revit|01" };

        bool claimed = WorkingDrawingFrontMatterSlot.Assign(album, page, FrontSheetEntry());

        Assert.True(claimed);
        Assert.True(page.FrontMatterOnSheet);
        Assert.Equal(WorkingDrawingFrontMatterSlot.SlotId, page.TemplateSlotId);
    }

    [Fact]
    public void ASheetWithoutTheListFrameDoesNotAndLosesAStaleClaim()
    {
        AlbumDefinition album = BuildingWorkingDrawingAlbumTemplate.CreateDefinition("");
        var page = new AlbumPageDefinition
        {
            SheetKey = "revit|01",
            TemplateSlotId = WorkingDrawingFrontMatterSlot.SlotId,
            FrontMatterOnSheet = true,
        };

        bool claimed = WorkingDrawingFrontMatterSlot.Assign(album, page, new SheetPackageEntry { Number = "01" });

        Assert.False(claimed);
        Assert.False(page.FrontMatterOnSheet);
    }

    [Fact]
    public void StudioDrawsNoListPageOfItsOwnWhenRevitCarriesTheFrames()
    {
        AlbumProject project = WorkingProject(out _, frontOnSheet: true);

        IReadOnlyList<ConceptGeneratedPagePlan> plans = BuildingArchitectureConceptGeneratedPagePlanner.Create(project);

        ConceptGeneratedPagePlan cover = Assert.Single(plans);
        Assert.Equal(AlbumGeneratedPageKind.Cover, cover.Component.GeneratedPageKind);
    }

    [Fact]
    public void WithoutTheFramesStudioStillDrawsItsListPage()
    {
        // The positive control: the old behaviour is untouched for a source
        // that reserves nothing.
        AlbumProject project = WorkingProject(out _, frontOnSheet: false);

        IReadOnlyList<ConceptGeneratedPagePlan> plans = BuildingArchitectureConceptGeneratedPagePlanner.Create(project);

        Assert.Equal(2, plans.Count);
    }

    [Fact]
    public void RevitsNumbersAreKept01ThenFrom02()
    {
        AlbumProject project = WorkingProject(out SheetLibrary library, frontOnSheet: true);
        int generated = BuildingArchitectureConceptGeneratedPagePlanner.Create(project).Count;

        IReadOnlyList<ConceptAlbumSourcePage> sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            project.Album,
            project.Album.Pages,
            library,
            [],
            generated);

        Assert.Equal(["01", "02", "03"], sequence.Select(item => item.AutomaticNumber));
        Assert.Equal("01", sequence[0].Sheet!.Entry.Number);
    }

    // ---- the explanatory-note draft ------------------------------------

    [Fact]
    public void TheDraftBecomesHeadingsAndParagraphs()
    {
        WorkingDrawingFrontMatter matter = WorkingDrawingFrontMatterDraft.Parse(Draft);

        Assert.Contains("ЕРӨНХИЙ ЗҮЙЛ", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.Contains("Иргэн Б.Батнасангийн захиалгаар", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.Contains("Газар хөдлөлтийн бүс – 7 балл", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.Contains("Шал: Орон сууцны", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.Contains("– Газар доорх давхар", matter.ExplanatoryNote, StringComparison.Ordinal);
        // The draft's own preamble, markdown and the tables after the rule are not the note.
        Assert.DoesNotContain("PFR агентын ноорог", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.DoesNotContain("**", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.DoesNotContain("|", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.DoesNotContain("`", matter.ExplanatoryNote, StringComparison.Ordinal);
        Assert.DoesNotContain("ТЕХНИК ЭДИЙН ЗАСГИЙН", matter.ExplanatoryNote, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlanksStayVisibleForTheOwnerToFill()
    {
        WorkingDrawingFrontMatter matter = WorkingDrawingFrontMatterDraft.Parse(Draft);

        Assert.Contains("[⚠ АТД-ын дугаар, огноо]", matter.ExplanatoryNote, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIndicatorsTakeTheModelColumnNEVERTheOldSheet()
    {
        WorkingDrawingFrontMatter matter = WorkingDrawingFrontMatterDraft.Parse(Draft);

        Assert.Equal(7, matter.Indicators.Count);
        Assert.Equal(("01", "Давхрын тоо", "", "16"), Row(matter.Indicators[0]));
        Assert.Equal(("02", "Барилгажилтын талбай", "м²", ""), Row(matter.Indicators[1]));
        Assert.Equal("≈9296", matter.Indicators[3].Value);
        string[] grandMark = ["573", "5616", "1195.3", "219.78", "0.18", "5.44"];
        Assert.DoesNotContain(matter.Indicators, row => grandMark.Contains(row.Value));
    }

    [Fact]
    public void TheCompositionRowsAreRead()
    {
        WorkingDrawingFrontMatter matter = WorkingDrawingFrontMatterDraft.Parse(Draft);

        Assert.Equal(3, matter.Composition.Count);
        Assert.Equal(("БА", "Барилга архитектур", ""), Comp(matter.Composition[0]));
        Assert.Equal(("ББ", "Барилга бүтээц", "70"), Comp(matter.Composition[1]));
        Assert.Equal(("", "Гал унтраах усны систем", "8"), Comp(matter.Composition[2]));
    }

    [Fact]
    public void NormalizeTrimsAndDropsEmptyRowsAndCloneIsDeep()
    {
        var matter = new WorkingDrawingFrontMatter
        {
            ExplanatoryNote = "  Тайлбар \r\n",
            Composition = { new() { Mark = " ББ ", Name = " Барилга бүтээц ", SheetCount = " 70 " }, new() },
            Indicators = { new() { Number = " 01 ", Name = " Давхрын тоо ", Value = " 16 " }, new() },
        };

        matter.Normalize();
        WorkingDrawingFrontMatter copy = matter.Clone();

        Assert.Equal("Тайлбар", matter.ExplanatoryNote);
        Assert.Equal(("ББ", "Барилга бүтээц", "70"), Comp(Assert.Single(matter.Composition)));
        Assert.Equal(("01", "Давхрын тоо", "", "16"), Row(Assert.Single(matter.Indicators)));
        Assert.NotSame(matter.Composition[0], copy.Composition[0]);
        Assert.NotSame(matter.Indicators[0], copy.Indicators[0]);
    }

    // ---- the tables -----------------------------------------------------

    [Fact]
    public void TheListCarriesEveryPageNumberTitleAndMark()
    {
        WorkingDrawingFrontMatterTable table = WorkingDrawingFrontMatterLayout.DrawingList(
            102, 280, ListRows(64));

        string[] texts = table.Cells.Select(cell => cell.Text).ToArray();
        Assert.Contains(WorkingDrawingFrontMatterLayout.DrawingListTitle, texts);
        Assert.Contains("00", texts);
        Assert.Contains("63", texts);
        Assert.Contains("ХУУДАС 63", texts);
        Assert.Equal(0, table.Overflow);
        AssertInside(table, 102, 280);
    }

    [Fact]
    public void AListLongerThanTheFrameIsReportedNotSilentlyCut()
    {
        WorkingDrawingFrontMatterTable table = WorkingDrawingFrontMatterLayout.DrawingList(
            102, 60, ListRows(64));

        Assert.True(table.Overflow > 0);
        AssertInside(table, 102, 60);
    }

    [Fact]
    public void ANameTooLongForItsColumnWrapsAndItsRowGrows()
    {
        // Owner, 2026-10-20: «хуудасны нэр багтахгүй бол мөр нэмэгдэж байгаа
        // зэрэг нь мэдэгддэг байх». A long name takes three lines; its row is
        // three slots tall and the cell wraps, the short names stay one line.
        List<WorkingDrawingFrontMatterLayout.ListRow> rows = ListRows(10).ToList();
        rows[3] = rows[3] with { Title = "ГАЗАР ДООРХ ГАРАЖИЙН ДАВХРЫН ПЛАН, ЗОГСООЛЫН БАЙР" };
        WorkingDrawingFrontMatterLayout.LineCounter measure = (text, _, _) => text.Length > 20 ? 3 : 1;

        WorkingDrawingFrontMatterTable plain = WorkingDrawingFrontMatterLayout.DrawingList(102, 280, rows);
        WorkingDrawingFrontMatterTable wrapped = WorkingDrawingFrontMatterLayout.DrawingList(
            102, 280, rows, measure: measure);

        WorkingTitleBlockCell longCell = wrapped.Cells.Single(cell => cell.Text == rows[3].Title);
        WorkingTitleBlockCell shortCell = wrapped.Cells.Single(cell => cell.Text == rows[4].Title);
        Assert.True(longCell.Wrap);
        Assert.False(shortCell.Wrap);
        Assert.Equal(shortCell.HeightMm * 3, longCell.HeightMm, 3);
        Assert.False(plain.Cells.Single(cell => cell.Text == rows[3].Title).Wrap);
        AssertInside(wrapped, 102, 280);
    }

    [Fact]
    public void TheFirstColumnFillsDownAndTheRestGoOnIntoTheNext()
    {
        // Owner, 2026-10-20: «байгаа хуудаснууд нь доошоо дүүрээд үлдсэн нь
        // арын багананд орно». 64 sheets in two 280 mm columns: the first
        // column reaches the frame's bottom, the second takes what is left,
        // no blank rows, nothing lost.
        IReadOnlyList<WorkingDrawingFrontMatterTable> tables =
            WorkingDrawingFrontMatterLayout.DrawingListColumns(100, 280, ListRows(64), 2);

        Assert.Equal(2, tables.Count);
        int first = tables[0].Cells.Count(cell => cell.Text.StartsWith("ХУУДАС", StringComparison.Ordinal));
        int second = tables[1].Cells.Count(cell => cell.Text.StartsWith("ХУУДАС", StringComparison.Ordinal));
        Assert.Equal(64, first + second);
        Assert.True(first > second);
        double firstBottom = tables[0].Lines.Max(line => Math.Max(line.Y0Mm, line.Y1Mm));
        Assert.True(firstBottom > 280 - WorkingDrawingFrontMatterLayout.MaxRowMm);
        Assert.Contains("ХУУДАС 0", tables[0].Cells.Select(cell => cell.Text));
        Assert.Contains("ХУУДАС 63", tables[1].Cells.Select(cell => cell.Text));
        Assert.All(tables, table => Assert.Equal(0, table.Overflow));
        Assert.All(tables, table => AssertInside(table, 100, 280));
    }

    [Fact]
    public void AListTooLongForAllColumnsShrinksItsRowsThenReportsTheRest()
    {
        IReadOnlyList<WorkingDrawingFrontMatterTable> fits =
            WorkingDrawingFrontMatterLayout.DrawingListColumns(100, 120, ListRows(64), 2);
        IReadOnlyList<WorkingDrawingFrontMatterTable> cut =
            WorkingDrawingFrontMatterLayout.DrawingListColumns(100, 60, ListRows(64), 2);

        Assert.Equal(0, fits[^1].Overflow);
        Assert.True(cut[^1].Overflow > 0);
        Assert.Equal(0, cut[0].Overflow);
    }

    [Fact]
    public void WrappedNamesLeaveFewerRowsAndTheRestIsReported()
    {
        // A frame that held every one-line name no longer holds them all when
        // the names wrap - the shortfall is counted, not cut silently.
        WorkingDrawingFrontMatterTable plain = WorkingDrawingFrontMatterLayout.DrawingList(102, 60, ListRows(14));
        WorkingDrawingFrontMatterTable wrapped = WorkingDrawingFrontMatterLayout.DrawingList(
            102, 60, ListRows(14), measure: (_, _, _) => 2);

        Assert.Equal(0, plain.Overflow);
        Assert.True(wrapped.Overflow > 0);
        AssertInside(wrapped, 102, 60);
    }

    [Fact]
    public void ThePreviewDrawsTheBlocksAloneAndSaysWhatDidNotFit()
    {
        // The layout editor shows the front matter as it will print; a block
        // made too small says so while it is being sized.
        AlbumProject project = WorkingProject(out SheetLibrary library, frontOnSheet: true);
        project.Album.FrontMatter = WorkingDrawingFrontMatterDraft.Parse(Draft);
        Assert.True(AlbumBuilder.TryCreateRequest(project, library, out AlbumBuildRequest request));
        AlbumBuildPage page = request.Sections
            .SelectMany(section => section.Pages)
            .First(item => item.Definition.FrontMatterOnSheet);
        string roomy = Path.Combine(workDirectory, "preview-roomy.pdf");
        string tight = Path.Combine(workDirectory, "preview-tight.pdf");

        IReadOnlyList<string> fits = PdfSharpAlbumWriter.WriteFrontMatterPreview(
            request, page, project.Album.FrontMatter,
            [new FrontMatterBlockLayout { Kind = SheetFrontMatterKinds.DrawingList, X = 20, Y = 10, Width = 102, Height = 250 }],
            roomy);
        IReadOnlyList<string> cut = PdfSharpAlbumWriter.WriteFrontMatterPreview(
            request, page, project.Album.FrontMatter,
            [new FrontMatterBlockLayout { Kind = SheetFrontMatterKinds.DrawingList, X = 20, Y = 10, Width = 102, Height = 12 }],
            tight);

        Assert.True(new FileInfo(roomy).Length > 0);
        Assert.DoesNotContain(fits, warning => warning.Contains("багтсангүй", StringComparison.Ordinal));
        Assert.Contains(cut, warning => warning.Contains("багтсангүй", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAlbumsOwnMarkIsCountedFromTheAlbumNotFromATypedCount()
    {
        // BN review, 2026-10-20, C4: «БА 59» stayed after the album grew to 61.
        // A set the album does not carry keeps the owner's typed count.
        WorkingDrawingFrontMatterTable table = WorkingDrawingFrontMatterLayout.Composition(
            90,
            60,
            [
                new() { Mark = "БА", Name = "Барилга архитектур", SheetCount = "59" },
                new() { Mark = "ББ", Name = "Барилга бүтээц", SheetCount = "70" },
            ],
            mark => mark == "БА" ? 47 : null,
            mark => mark == "БА" ? 61 : null);

        string[] texts = table.Cells.Select(cell => cell.Text).ToArray();
        Assert.Contains("61", texts);
        Assert.DoesNotContain("59", texts);
        Assert.Contains("70", texts);
    }

    [Fact]
    public void ASheetWithoutNamesSignsWithTheAlbumsDatesFromItsExportAndNeverLeavesTheScaleBlank()
    {
        // BN review, 2026-10-20, C1-C3: the general plan's DWG sheets came
        // without names, every «Огноо» was empty, three Revit sheets had no scale.
        AlbumProject project = WorkingProject(out SheetLibrary library, frontOnSheet: true);
        Assert.True(AlbumBuilder.TryCreateRequest(project, library, out AlbumBuildRequest request));
        List<AlbumBuildPage> pages = request.Sections.SelectMany(section => section.Pages).ToList();
        AlbumBuildPage signed = pages[0];
        AlbumBuildPage unsigned = pages[^1];
        signed.Sheet.Entry.Roles =
        [
            new SheetRoleSignature { RoleCode = SheetRoleCodes.Architect, FullName = "Э.Мөнххолбоо" },
            new SheetRoleSignature { RoleCode = SheetRoleCodes.PreparedBy, FullName = "М.Будбаясгалан" },
        ];
        unsigned.Sheet.Entry.Roles = [new SheetRoleSignature { RoleCode = SheetRoleCodes.PreparedBy, FullName = "Б.Бат" }];

        AlbumBuilder.ShareAlbumRoles(request);
        IReadOnlyList<SheetRoleSignature> roles = PdfSharpAlbumWriter.SheetRolesWithAlbumFallback(unsigned);

        Assert.Contains(roles, role => role.RoleCode == SheetRoleCodes.Architect && role.FullName == "Э.Мөнххолбоо");
        Assert.Contains(roles, role => role.RoleCode == SheetRoleCodes.PreparedBy && role.FullName == "Б.Бат");
        Assert.DoesNotContain(roles, role => role.FullName == "М.Будбаясгалан");

        IReadOnlyList<WorkingTitleBlockCell> cells = HorizontalWorkingTitleBlockLayout.Cells(
            project, "ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ", "1", "ЕТ", "", null, roles,
            new DateTimeOffset(2026, 10, 19, 4, 0, 0, TimeSpan.Zero));
        string[] texts = cells.Select(cell => cell.Text).ToArray();
        Assert.Contains("Э.Мөнххолбоо", texts);
        Assert.Contains("Масштаб: —", texts);
        Assert.Contains(texts, text => text.Contains("2026.10.19", StringComparison.Ordinal));

        project.SheetDateUtc = new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026.09.01", PdfSharpAlbumWriter.SheetDateText(project, new DateTimeOffset(2026, 10, 19, 4, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void EverySourceSheetIsListedWithTheNumberItPrintsUnder()
    {
        // BN review, 2026-10-20, B: sources cite sheets by Studio's number,
        // read from here (_shared/sheet-reference-contract-2026-10-20.md).
        AlbumProject project = WorkingProject(out SheetLibrary library, frontOnSheet: true);
        Assert.True(AlbumBuilder.TryCreateRequest(project, library, out AlbumBuildRequest request));

        IReadOnlyList<SheetPrintNumber> numbers = WorkingDrawingSheetNumbering.PrintNumbers(request);

        int pages = request.Sections.Sum(section => section.Pages.Count);
        Assert.Equal(pages, numbers.Count);
        Assert.All(numbers, number => Assert.False(string.IsNullOrWhiteSpace(number.Label)));
        Assert.All(numbers.Where(number => number.Mark.Length > 0),
            number => Assert.StartsWith(number.Mark + "-", number.Label, StringComparison.Ordinal));
    }

    [Fact]
    public void ACompositionRowWithoutACountCountsTheAlbumsOwnPages()
    {
        WorkingDrawingFrontMatterTable table = WorkingDrawingFrontMatterLayout.Composition(
            90,
            60,
            [
                new() { Mark = "БА", Name = "Барилга архитектур" },
                new() { Mark = "ББ", Name = "Барилга бүтээц", SheetCount = "70" },
            ],
            mark => mark == "БА" ? 56 : null);

        string[] texts = table.Cells.Select(cell => cell.Text).ToArray();
        Assert.Contains("56", texts);
        Assert.Contains("70", texts);
        Assert.Contains(WorkingDrawingFrontMatterLayout.CompositionTitle, texts);
        AssertInside(table, 90, 60);
    }

    [Fact]
    public void TheIndicatorsKeepTheirColumnsAndABlankStaysBlank()
    {
        WorkingDrawingFrontMatterTable table = WorkingDrawingFrontMatterLayout.Indicators(
            90,
            40,
            [
                new() { Number = "01", Name = "Давхрын тоо", Value = "16" },
                new() { Number = "02", Name = "Барилгажилтын талбай", Unit = "м²" },
            ]);

        string[] texts = table.Cells.Select(cell => cell.Text).ToArray();
        Assert.Contains("Давхрын тоо", texts);
        Assert.Contains("м²", texts);
        Assert.Contains("Хэмжээ", texts);
        Assert.Equal(1, WorkingDrawingFrontMatterLayout.BlankIndicatorCount(
            [new() { Name = "a", Value = "1" }, new() { Name = "b" }]));
        AssertInside(table, 90, 40);
    }

    // ---- the album ------------------------------------------------------

    [Fact]
    public void APartOfThe01sDrawingMovedByTheOwnerIsDrawnAgainAtItsNewPlace()
    {
        // Owner, 2026-10-20: «цаад зургаа яаж зөөх вэ». The source PDF is not
        // touched; the part is drawn again, vector, moved.
        AlbumProject project = WorkingProject(out SheetLibrary library, frontOnSheet: true);
        project.Album.FrontMatter = WorkingDrawingFrontMatterDraft.Parse(Draft);
        project.Album.FrontMatter.SourceMoves =
        [
            new SourceRegionMove { X = 200, Y = 40, Width = 120, Height = 150, OffsetX = -60, OffsetY = 10 },
        ];
        string outputPath = Path.Combine(workDirectory, "bn-album-moved.pdf");

        AlbumBuildResult result = new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, library, outputPath);

        Assert.Equal(4, result.PageCount);
        Assert.True(new FileInfo(outputPath).Length > 0);
        WorkingDrawingFrontMatter copy = project.Album.FrontMatter.Clone();
        Assert.Equal(-60, Assert.Single(copy.SourceMoves).OffsetX);
    }

    [Fact]
    public void TheBuiltAlbumIsCoverThenRevits01AndSaysWhatIsMissing()
    {
        AlbumProject project = WorkingProject(out SheetLibrary library, frontOnSheet: true);
        project.Album.FrontMatter = WorkingDrawingFrontMatterDraft.Parse(Draft);
        string outputPath = Path.Combine(workDirectory, "bn-album.pdf");

        AlbumBuildResult result = new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, library, outputPath);

        Assert.Equal(4, result.PageCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("утга хоосон ⚠", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("бөглөөгүй 1 хэсэг ⚠", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("багтсангүй", StringComparison.Ordinal));
        string? probe = Environment.GetEnvironmentVariable("ERKS_FRONT_MATTER_PROBE_DIR");
        if (!string.IsNullOrWhiteSpace(probe))
            File.Copy(outputPath, Path.Combine(probe, "bn-album.pdf"), overwrite: true);
    }

    [Fact]
    public void AMovedPartDoesNotCarryAReservedFramesOutlineAlong()
    {
        // Owner, 2026-10-20: «ийм илүү дутуу зурааснуудыг байхгүй болгоорой» -
        // the outlines of Revit's frames came along with a moved part of the
        // drawing. The moved part covers the frames at their moved places.
        string writer = File.ReadAllText(Path.Combine(SourceRoot(), "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.FrontMatter.cs"));
        int image = writer.IndexOf("gfx.DrawImage(form, CalculatePdfSharpFormDrawRectangle(desired", StringComparison.Ordinal);
        int cover = writer.IndexOf("ReservedFrameCover(frame, move.OffsetX, move.OffsetY)", image, StringComparison.Ordinal);
        int restore = writer.IndexOf("gfx.Restore(state);", image, StringComparison.Ordinal);
        Assert.True(image > 0 && cover > image && restore > cover);
        Assert.Contains("DrawSourceMoves(gfx, buildPage.Sheet!, matter.SourceMoves, paperBrush, reserved.Values.ToList());", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFrontMatterNeverReachesTheSheetsOwnLines()
    {
        // Owner, 2026-10-20: «хуудасны зурааснууд яагаад арилаад байна вэ» -
        // a block's paper, the reserved frames' cover took the drawing frame
        // and the corner table's top line with them.
        string writer = File.ReadAllText(Path.Combine(SourceRoot(), "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.FrontMatter.cs"));
        int clip = writer.IndexOf("gfx.IntersectClip(FrontMatterClip(WorkingDrawingPageLayout.Resolve(buildPage.Format)));", StringComparison.Ordinal);
        int cover = writer.IndexOf("gfx.DrawRectangle(paperBrush, ReservedFrameCover(frame.Area, 0, 0));", StringComparison.Ordinal);
        int blocks = writer.IndexOf("DrawFrontMatterBlocks(gfx, FrontMatterBlocks(buildPage, reserved, matter)", StringComparison.Ordinal);
        Assert.True(clip > 0 && cover > clip && blocks > clip);
        Assert.Contains("FillMode = XFillMode.Alternate", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWriterDrawsTheFramesAfterThePageIsComposed()
    {
        // The frames are written on the page as composed - after the sheet and
        // its chrome, so nothing of Studio's covers them again.
        string writer = File.ReadAllText(Path.Combine(SourceRoot(), "src", "ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs"));
        int compose = writer.IndexOf("ComposeFormattedPages(document, request.Project, buildPage);", StringComparison.Ordinal);
        int frames = writer.IndexOf("DrawFrontMatter(document, firstPageIndex, request, generatedPages, buildPage, warnings);", StringComparison.Ordinal);

        Assert.True(compose > 0 && frames > compose);
    }

    [Fact]
    public void ReconciliationClaimsThe01BeforeItLooksForDuplicates()
    {
        // Revit may title its 01 like Studio's own list page; a sheet carrying
        // the frames is the page the owner kept, not a duplicate to drop.
        string source = File.ReadAllText(Path.Combine(
            SourceRoot(), "src", "ErkS.Platform.Core", "ProjectPackageReconciliationService.cs"));
        int guard = source.IndexOf("WorkingDrawingFrontMatterSlot.CarriesFrontMatter(entry)", StringComparison.Ordinal);
        int collision = source.IndexOf("StudioComposedPageCollision.Find(album, entry)", StringComparison.Ordinal);

        Assert.True(guard > 0 && collision > guard);
        Assert.Contains("!carriesFrontMatter &&", source, StringComparison.Ordinal);
        Assert.Contains("WorkingDrawingFrontMatterSlot.Assign(album, page, entry)", source, StringComparison.Ordinal);
    }

    // ---- helpers ---------------------------------------------------------

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ErkS.Platform.Pdf")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private const string Draft = """
        # ИРГЭН БАТНАСАНГИЙН ОРОН СУУЦНЫ БАРИЛГА — ТАЙЛБАР БИЧИГ (ноорог)

        > PFR агентын ноорог, 2026-10-19. STU-ийн «Тайлбар бичиг» хуудсанд оруулна.

        ## ЕРӨНХИЙ ЗҮЙЛ

        Иргэн Б.Батнасангийн захиалгаар Улаанбаатар хот, Баянзүрх дүүргийн 22-р хорооны
        нутаг дэвсгэрт баригдах барилгын ажлын зургийг боловсруулав.

        Ажлын зургийг `[⚠ АТД-ын дугаар, огноо]` архитектур төлөвлөлтийн даалгавар дагуу боловсруулсан болно.

        | Үзүүлэлт | Утга |
        |---|---|
        | Газар хөдлөлтийн бүс | 7 балл |

        ## АРХИТЕКТУР ТӨЛӨВЛӨЛТ

        - Газар доорх давхар (−4.00): авто гараж.

        **Шал:** Орон сууцны зочны өрөөнд паркет шал хийнэ.

        ---

        ## ТЕХНИК ЭДИЙН ЗАСГИЙН ҮЗҮҮЛЭЛТ

        | № | Үзүүлэлт | Нэгж | Хуучин хуудсан дээр | Revit загвараас тооцсон |
        |---|---|---|---|---|
        | 01 | Давхрын тоо | — | 16 | 16 (+ газар доорх 1, техникийн 1) |
        | 02 | Барилгажилтын талбай | м² | 573 | `[⚠]` |
        | 03 | Барилгын эзэлхүүн | м³ | 5616 | `[⚠]` |
        | 04 | Ашигтай талбай (S аш) | м² | 1195.3 | ≈9296 (1–16-р давхрын өрөөний талбай) |
        | 05 | Ажлын талбай (S аж) | м² | 219.78 | `[⚠]` |
        | 06 | К1 = S аж / S аш | — | 0.18 | `[⚠]` |
        | 07 | К2 = S аш / S аж | — | 5.44 | `[⚠]` |

        ## АЖЛЫН ЗУРГИЙН ИЖ БҮРДЛИЙН ЖАГСААЛТ

        | Марк | Хэсэг | Хуудасны тоо |
        |---|---|---|
        | БА | Барилга архитектур | |
        | ББ | Барилга бүтээц | 70 |
        | `[⚠]` | Гал унтраах усны систем | 8 |

        ## ЗӨВШИЛЦСӨН

        ББ — А.Буянжаргал
        """;

    private static (string, string, string, string) Row(WorkingDrawingIndicatorRow row) =>
        (row.Number, row.Name, row.Unit, row.Value);

    private static (string, string, string) Comp(WorkingDrawingCompositionRow row) =>
        (row.Mark, row.Name, row.SheetCount);

    private static IReadOnlyList<WorkingDrawingFrontMatterLayout.ListRow> ListRows(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new WorkingDrawingFrontMatterLayout.ListRow(
                index.ToString("D2"),
                $"ХУУДАС {index}",
                index < 2 ? "ЕХ" : "БА"))
            .ToList();

    private static void AssertInside(WorkingDrawingFrontMatterTable table, double width, double height)
    {
        foreach (WorkingTitleBlockCell cell in table.Cells)
        {
            Assert.InRange(cell.XMm, -0.001, width);
            Assert.InRange(cell.YMm, -0.001, height);
            Assert.InRange(cell.XMm + cell.WidthMm, 0, width + 0.001);
            Assert.InRange(cell.YMm + cell.HeightMm, 0, height + 0.001);
        }
        foreach (WorkingTitleBlockLine line in table.Lines)
        {
            Assert.InRange(line.X0Mm, -0.001, width + 0.001);
            Assert.InRange(line.X1Mm, -0.001, width + 0.001);
            Assert.InRange(line.Y0Mm, -0.001, height + 0.001);
            Assert.InRange(line.Y1Mm, -0.001, height + 0.001);
        }
    }

    private static SheetPackageEntry FrontSheetEntry() => new()
    {
        SheetId = "01",
        Number = "01",
        Name = "ЕРӨНХИЙ ХЭСЭГ",
        Discipline = "ЕХ",
        WidthMm = 582,
        HeightMm = 420,
        FrontMatterAreas =
        [
            new() { Kind = SheetFrontMatterKinds.DrawingList, Area = new() { X = 24, Y = 17, Width = 102, Height = 380 } },
            new() { Kind = SheetFrontMatterKinds.ExplanatoryNote, Area = new() { X = 145, Y = 25, Width = 125, Height = 370 } },
            new() { Kind = SheetFrontMatterKinds.AlbumComposition, Area = new() { X = 470, Y = 268, Width = 96, Height = 60 } },
            new() { Kind = SheetFrontMatterKinds.TechnicalIndicators, Area = new() { X = 470, Y = 328, Width = 96, Height = 42 } },
        ],
    };

    private AlbumProject WorkingProject(out SheetLibrary library, bool frontOnSheet)
    {
        string manifestPath = WritePackage("project-" + frontOnSheet);
        library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(manifestPath));

        var project = new AlbumProject { Name = "BN" };
        project.Album = BuildingWorkingDrawingAlbumTemplate.CreateDefinition("");
        foreach (SheetRecord record in library.Snapshot().OrderBy(record => record.SourceSheetIndex))
        {
            var page = new AlbumPageDefinition { SheetKey = record.Key };
            if (!(frontOnSheet && WorkingDrawingFrontMatterSlot.Assign(project.Album, page, record.Entry)))
                page.TemplateSlotId = BuildingWorkingDrawingAlbumTemplate.FindSourceSlot(project.Album)!.Id;
            project.Album.Pages.Add(page);
        }
        return project;
    }

    private string WritePackage(string folderName)
    {
        string directory = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(directory);
        const string fileName = "BN.pdf";
        using (var document = new PdfDocument())
        {
            foreach (string label in new[] { "01", "БА-11", "БА-12" })
            {
                PdfPage page = document.AddPage();
                page.Width = XUnit.FromMillimeter(582);
                page.Height = XUnit.FromMillimeter(420);
                using XGraphics gfx = XGraphics.FromPdfPage(page);
                gfx.DrawString(label, new XFont("Arial", 12), XBrushes.Black, new XPoint(40, 40));
            }
            document.Save(Path.Combine(directory, fileName));
        }

        SheetPackageEntry front = FrontSheetEntry();
        front.PdfFileName = fileName;
        front.PdfPageNumber = 1;
        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = "batnasan-revit",
                Application = SheetSourceApplication.Revit,
                DocumentPath = @"D:\Cloud Work\Batnasan Apartment\RVT\Arch\Arch_BN_SHEET V2.rvt",
            },
            Sheets =
            {
                front,
                Plain("ba-11", "БА-11", 2),
                Plain("ba-12", "БА-12", 3),
            },
        };
        return SheetPackageWriter.Write(manifest, directory, "batnasan");

        SheetPackageEntry Plain(string id, string number, int pdfPage) => new()
        {
            SheetId = id,
            Number = number,
            Name = number,
            Discipline = "БА",
            WidthMm = 582,
            HeightMm = 420,
            PdfFileName = fileName,
            PdfPageNumber = pdfPage,
        };
    }

    private static void RewriteFrontSheet(string manifestPath, Action<JsonObject> change) =>
        RewriteManifest(manifestPath, manifest => change(manifest["sheets"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(entry => (string?)entry["number"] == "01")));

    private static void RewriteManifest(string manifestPath, Action<JsonObject> change)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        change(manifest);
        File.WriteAllText(manifestPath, manifest.ToJsonString(SheetPackageJson.Options));
    }
}
