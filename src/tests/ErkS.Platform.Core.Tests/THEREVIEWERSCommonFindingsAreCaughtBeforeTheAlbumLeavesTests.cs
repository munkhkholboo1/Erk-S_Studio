using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The findings the state review (магадлал) repeats most often on the
/// building-architecture set, turned into checks that run on every working
/// drawing album (owner, 2026-10-20: «агентуудад үүнийг илгээгээд дүрэм
/// болго»; _shared/magadlal-ba-review-rules-2026-10-20.md, items 1, 4, 6).
/// Reported, never refused - the album is still built.
/// </summary>
public sealed class THEREVIEWERSCommonFindingsAreCaughtBeforeTheAlbumLeavesTests
{
    [Fact]
    public void AMissingGeneralPlanSheetIsNamed()
    {
        // Batnasan, 2026-10-20: four ЕТ sheets, no cartogram.
        AlbumBuildRequest request = Request(
            ["ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ", "ЗАЙ ХЭМЖЭЭ", "ЗАМ ТАЛБАЙН ХУЧИЛТ", "НОГООН БАЙГУУЛАМЖ"],
            CompleteNote());

        IReadOnlyList<string> findings = AlbumReviewChecklist.Review(request);

        string finding = Assert.Single(findings, item => item.Contains("ЕТ", StringComparison.Ordinal));
        Assert.Contains("Картограмм", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompleteSetSaysNothing()
    {
        AlbumBuildRequest request = Request(
            ["ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ", "ЗАЙ ХЭМЖЭЭ", "КАРТОГРАММ", "ЗАМ ТАЛБАЙН ХУЧИЛТ", "НОГООН БАЙГУУЛАМЖ"],
            CompleteNote());

        Assert.Empty(AlbumReviewChecklist.Review(request));
    }

    [Fact]
    public void AnAlbumWithoutAGeneralPlanIsNotAskedForOne()
    {
        AlbumBuildRequest request = Request([], CompleteNote());

        Assert.DoesNotContain(AlbumReviewChecklist.Review(request), item => item.Contains("ЕТ-ийн", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNoteIsCheckedForWhatReviewersAskEveryTime()
    {
        var bare = new WorkingDrawingFrontMatter
        {
            ExplanatoryNote = "ЕРӨНХИЙ ЗҮЙЛ\n\nИРГЭН БАТНАСАНГИЙН ОРОН СУУЦНЫ БАРИЛГА-ийн ажлын зураг.",
        };

        IReadOnlyList<string> findings = AlbumReviewChecklist.Review(Request([], bare));

        Assert.Contains(findings, item => item.Contains("нормын жагсаалт", StringComparison.Ordinal));
        Assert.Contains(findings, item => item.Contains("цаг уурын", StringComparison.Ordinal));
        Assert.Contains(findings, item => item.Contains("шил", StringComparison.Ordinal));
        Assert.Contains(findings, item => item.Contains("MNS", StringComparison.Ordinal));
        Assert.Contains(findings, item => item.Contains("өнгөний код", StringComparison.Ordinal));
        Assert.Contains(findings, item => item.Contains("К1", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, item => item.Contains("төслийн нэр", StringComparison.Ordinal));
    }

    [Fact]
    public void ANoteThatNamesAnotherProjectIsCaught()
    {
        WorkingDrawingFrontMatter matter = CompleteNote();
        matter.ExplanatoryNote = matter.ExplanatoryNote.Replace("ИРГЭН БАТНАСАНГИЙН ОРОН СУУЦНЫ БАРИЛГА", "ӨӨР ТӨСӨЛ");

        Assert.Contains(
            AlbumReviewChecklist.Review(Request([], matter)),
            item => item.Contains("төслийн нэр", StringComparison.Ordinal));
    }

    [Fact]
    public void AReservedConsultationFrameWithNoEngineersIsCaught()
    {
        AlbumBuildRequest request = Request([], CompleteNote(), consultationFrame: true);
        request.Project.ApprovalWorkflow.WorkingDrawingSectionConsent.Clear();

        Assert.Contains(
            AlbumReviewChecklist.Review(request),
            item => item.Contains("ЗӨВШӨӨРӨЛЦСӨН", StringComparison.Ordinal));
    }

    [Fact]
    public void AConceptAlbumIsLeftToItsOwnTemplate()
    {
        AlbumBuildRequest request = Request(["ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ"], new WorkingDrawingFrontMatter());
        request.Project.Album.TemplateId = BuildingArchitectureConceptAlbumTemplate.TemplateId;

        Assert.Empty(AlbumReviewChecklist.Review(request));
    }

    private static WorkingDrawingFrontMatter CompleteNote() => new()
    {
        ExplanatoryNote =
            "ЕРӨНХИЙ ЗҮЙЛ\n\nИРГЭН БАТНАСАНГИЙН ОРОН СУУЦНЫ БАРИЛГА-ийн ажлын зургийг БНбД 31-01-10, БНбД 30-01-04-ийн дагуу " +
            "боловсруулав. Гадна агаарын тооцооны температур −32.7 °C, цасны ачаалал 50 кг/м², салхины даралт 33 кгх/м².\n\n" +
            "Цонх 3 давхар шилтэй (MNS 6060:2010). Фасадын өнгөний код CMYK 0/0/0/10.",
        Indicators =
        {
            new() { Number = "06", Name = "К1 = S аж / S аш", Value = "0.62" },
            new() { Number = "07", Name = "К2 = S аш / S аж", Value = "1.61" },
        },
    };

    private static AlbumBuildRequest Request(
        IReadOnlyList<string> generalPlanTitles,
        WorkingDrawingFrontMatter matter,
        bool consultationFrame = false)
    {
        var project = new AlbumProject { Name = "ИРГЭН БАТНАСАНГИЙН ОРОН СУУЦНЫ БАРИЛГА" };
        project.Album = BuildingWorkingDrawingAlbumTemplate.CreateDefinition("");
        project.Album.FrontMatter = matter;
        project.ApprovalWorkflow.WorkingDrawingSectionConsent.Add(new() { SectionMark = "ББ", PersonName = "А.Буянжаргал" });

        var pages = generalPlanTitles
            .Select((title, index) => Page($"0{index + 2}", title, "ЕТ", consultation: false))
            .ToList();
        pages.Add(Page("11", "ЗООРИЙН ДАВХРЫН БАЙГУУЛАЛТ", "БА", consultationFrame));
        return new AlbumBuildRequest
        {
            Project = project,
            Sections = [new AlbumBuildSection { Title = "", Pages = pages }],
        };
    }

    private static AlbumBuildPage Page(string number, string title, string mark, bool consultation)
    {
        var entry = new SheetPackageEntry
        {
            SheetId = number,
            Number = number,
            Name = title,
            Discipline = mark,
            WidthMm = 420,
            HeightMm = 297,
        };
        if (consultation)
        {
            entry.ConsultationTableArea = new PageRectSpec { X = 165, Y = 253, Width = 75, Height = 34 };
            entry.ConsultationTableRows = 6;
        }
        return new AlbumBuildPage
        {
            Sheet = new SheetRecord
            {
                Key = "src|" + number,
                SourceId = "src",
                SourceIdentity = "src",
                Entry = entry,
                Source = new SheetPackageSource(),
                PackageId = Guid.Empty,
                ManifestPath = "",
                PdfPath = "",
                SourceSheetIndex = 0,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                IsVerified = true,
            },
            Definition = new AlbumPageDefinition(),
            Format = PageFormatCatalog.DefaultWorkingDrawing,
        };
    }
}
