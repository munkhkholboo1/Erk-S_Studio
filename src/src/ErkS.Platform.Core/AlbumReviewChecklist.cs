using System.Text.RegularExpressions;

namespace ErkS.Platform.Core;

/// <summary>
/// The state review's (магадлал) most repeated findings on the
/// building-architecture set, checked on every working-drawing album build
/// (owner, 2026-10-20; _shared/magadlal-ba-review-rules-2026-10-20.md).
/// </summary>
/// <remarks>
/// Reported, never refused, like <see cref="DrawingScaleSurvey"/>: whoever
/// issues the album knows things this does not, and a build that stops is a
/// build that gets worked around. Each finding names the review item it comes
/// from so it can be traced back to the reviewers' wording.
/// </remarks>
public static partial class AlbumReviewChecklist
{
    public const string Prefix = "Магадлал";

    /// <summary>The general-plan sheets every set must carry (item 1), by title.</summary>
    private static readonly (string Label, string[] Keys)[] GeneralPlanSet =
    [
        ("Ерөнхий төлөвлөгөө", ["ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ"]),
        ("Зай хэмжээ", ["ЗАЙ ХЭМЖЭЭ", "ЗАЙ, ХЭМЖЭЭ"]),
        ("Картограмм / босоо төлөвлөлт", ["КАРТОГРАММ", "БОСОО ТӨЛӨВЛӨЛТ", "ӨНДӨРЖИЛТ"]),
        ("Зам талбайн хучилт", ["ХУЧИЛТ"]),
        ("Ногоон байгууламж", ["НОГООН БАЙГУУЛАМЖ", "ДЕНДРО"]),
    ];

    public static IReadOnlyList<string> Review(AlbumBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var findings = new List<string>();
        AlbumDefinition album = request.Project.Album;
        if (!string.Equals(album.TemplateId, BuildingWorkingDrawingAlbumTemplate.TemplateId, StringComparison.OrdinalIgnoreCase))
            return findings;

        List<AlbumBuildPage> pages = request.Sections.SelectMany(section => section.Pages).ToList();
        ReviewGeneralPlanSet(pages, findings);
        ReviewExplanatoryNote(request.Project, album.FrontMatter ?? new WorkingDrawingFrontMatter(), findings);
        ReviewConsultation(request.Project, pages, findings);
        return findings;
    }

    private static void ReviewGeneralPlanSet(IReadOnlyList<AlbumBuildPage> pages, List<string> findings)
    {
        List<string> titles = pages
            .Where(page => ProjectDesignSourceClassification.IsGeneralPlanSheet(page.Sheet.Entry))
            .Select(page => (page.Title ?? "").ToUpperInvariant())
            .ToList();
        if (titles.Count == 0)
            return;

        string[] missing = GeneralPlanSet
            .Where(item => !titles.Any(title => item.Keys.Any(key => title.Contains(key, StringComparison.Ordinal))))
            .Select(item => item.Label)
            .ToArray();
        if (missing.Length > 0)
        {
            findings.Add(
                $"{Prefix} 1: ЕТ-ийн иж бүрдэлд дутуу хуудас — {string.Join(", ", missing)}.");
        }
    }

    private static void ReviewExplanatoryNote(AlbumProject project, WorkingDrawingFrontMatter matter, List<string> findings)
    {
        string note = matter.ExplanatoryNote ?? "";
        if (string.IsNullOrWhiteSpace(note))
        {
            findings.Add($"{Prefix} 4: тайлбар бичиг алга («ЕХ мэдээлэл»).");
            return;
        }

        void Require(bool present, string what)
        {
            if (!present)
                findings.Add($"{Prefix} 4: тайлбар бичигт {what} алга.");
        }

        Require(NormPattern().IsMatch(note), "мөрдсөн нормын жагсаалт (БНбД …)");
        Require(
            note.Contains("температур", StringComparison.OrdinalIgnoreCase) &&
            note.Contains("цас", StringComparison.OrdinalIgnoreCase) &&
            note.Contains("салх", StringComparison.OrdinalIgnoreCase),
            "цаг уурын өгөгдөл (тооцооны температур, цас, салхи)");
        Require(note.Contains("шил", StringComparison.OrdinalIgnoreCase), "цонхны шилний тодорхойлолт");
        Require(note.Contains("MNS", StringComparison.OrdinalIgnoreCase) || note.Contains("ҮСТ", StringComparison.Ordinal), "MNS стандартын дугаар");
        Require(ColourCodePattern().IsMatch(note), "фасадын өнгөний код (CMYK/RAL)");

        string name = (project.Name ?? "").Trim();
        if (name.Length > 0 && !note.Contains(name, StringComparison.OrdinalIgnoreCase))
            findings.Add($"{Prefix} 4: тайлбар бичигт зураг төслийн нэр («{name}») алга — даалгаврын нэртэй тулгана уу.");

        string[] coefficients = ["К1", "К2"];
        foreach (string coefficient in coefficients)
        {
            bool filled = matter.Indicators.Any(row =>
                (row.Name ?? "").Replace("K", "К").Contains(coefficient, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(row.Value));
            if (!filled)
                findings.Add($"{Prefix} 4: ТЭЗҮ-д {coefficient} утга алга.");
        }
    }

    private static void ReviewConsultation(AlbumProject project, IReadOnlyList<AlbumBuildPage> pages, List<string> findings)
    {
        bool reserved = pages.Any(page => page.Sheet.Entry.ConsultationTableArea is not null);
        if (reserved && (project.ApprovalWorkflow?.WorkingDrawingSectionConsent?.Count ?? 0) == 0)
        {
            findings.Add(
                $"{Prefix} 6: «ЗӨВШӨӨРӨЛЦСӨН» блокийн хүрээ байгаа ч хэсгийн инженерүүд бичигдээгүй («Оролцогчид»).");
        }
    }

    [GeneratedRegex(@"БНбД|БД\s*\d|ДЗ\s*\d|MNS|ГОСТ|СНиП|СП\s*\d", RegexOptions.CultureInvariant)]
    private static partial Regex NormPattern();

    [GeneratedRegex(@"CMYK|RAL\s*\d|өнгөний код", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColourCodePattern();
}
