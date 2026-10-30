namespace ErkS.Platform.Core;

/// <summary>
/// Album pages that will print «Хуулбар оруулаагүй» in place of a document scan: the design
/// company's certificate or licence, or the approved planning task. Named before the album is
/// published (Мастер, 2026-10-31: Erin R64-R66 went out with both company pages empty and
/// nobody was told).
/// </summary>
/// <remarks>
/// Reported, never refused, like <see cref="HeavySheetSurvey"/>: an album may have to go out
/// before a scan exists, and the page says so itself. A kind is named once however many pages
/// it would take.
/// </remarks>
public static class MissingDocumentCopySurvey
{
    public const string Prefix = "Хуулбаргүй хуудас";

    public static IReadOnlyList<string> Review(AlbumBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<ConceptGeneratedPagePlan> plans;
        try
        {
            plans = BuildingArchitectureConceptGeneratedPagePlanner.Create(request.Project);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return [];
        }

        return plans
            .Where(plan => plan.DocumentKind != ConceptGeneratedDocumentKind.None && plan.DocumentPages.Count == 0)
            .Select(plan => plan.DocumentKind)
            .Distinct()
            .Select(kind => $"{Prefix}: {Name(kind)} — альбумд «Хуулбар оруулаагүй» гэж гарна. {Remedy(kind)}")
            .ToList();
    }

    private static string Name(ConceptGeneratedDocumentKind kind) => kind switch
    {
        ConceptGeneratedDocumentKind.CompanyRegistrationCertificate => "байгууллагын улсын бүртгэлийн гэрчилгээ",
        ConceptGeneratedDocumentKind.CompanyDesignLicense => "байгууллагын тусгай зөвшөөрөл",
        _ => "батлагдсан архитектур төлөвлөлтийн даалгавар",
    };

    private static string Remedy(ConceptGeneratedDocumentKind kind) => kind switch
    {
        ConceptGeneratedDocumentKind.ApprovedPlanningTask =>
            "Төслийн баримтаас АТД-ийн хуулбарыг оруулна уу.",
        _ => "Компанийн сангаас байгууллагын хуулбарыг оруулбал Cloud-оор бүх гишүүнд очно.",
    };
}
