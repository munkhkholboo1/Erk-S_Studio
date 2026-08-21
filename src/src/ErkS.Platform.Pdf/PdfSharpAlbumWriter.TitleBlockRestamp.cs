using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

public sealed record AlbumTitleBlockRestampResult(
    int PageCount,
    IReadOnlyList<int> RestampedPages);

public sealed partial class PdfSharpAlbumWriter
{
    private const int CanonicalTitleBlockRevision = 4;
    private const string CanonicalTitleBlockKeywordPrefix = "ErkSCanonicalTitleBlock=";

    /// <summary>
    /// Repaints only Studio-owned canonical metadata cells. Contributor page
    /// content, signatures, scale, sheet number and year remain untouched.
    /// </summary>
    public static AlbumTitleBlockRestampResult RestampCanonicalTitleBlocks(
        string inputPdfPath,
        AlbumProject project,
        IReadOnlyList<AlbumComponentPdfSlot> components,
        string outputPdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPdfPath);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPdfPath);
        if (!File.Exists(inputPdfPath))
            throw new FileNotFoundException("Canonical album PDF was not found.", inputPdfPath);
        if (Path.GetFullPath(inputPdfPath).Equals(
                Path.GetFullPath(outputPdfPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Title-block restamping requires a distinct output path.",
                nameof(outputPdfPath));
        }

        WindowsFontResolver.Register();
        string? outputFolder = Path.GetDirectoryName(outputPdfPath);
        if (!string.IsNullOrWhiteSpace(outputFolder))
            Directory.CreateDirectory(outputFolder);

        // The corner table repainted below belongs to the concept album alone.
        // A working-drawing or general-plan page already carries the horizontal
        // title block its own build drew, and this table was painted over it at
        // concept coordinates - leaving two title blocks, offset from one
        // another, on every such sheet.
        HashSet<int> selectedPages = ConceptAlbumOwnsTheCornerTable(project)
            ? components
                .Where(component => ShouldRestampCanonicalTitleBlock(component.Code))
                .SelectMany(component => component.PageNumbers ?? [])
                .Where(pageNumber => pageNumber > 0)
                .ToHashSet()
            : [];
        var restampedPages = new List<int>();

        using PdfDocument document = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Modify);
        foreach (int pageNumber in selectedPages.Order())
        {
            if (pageNumber > document.PageCount)
                continue;

            PdfPage page = document.Pages[pageNumber - 1];
            double widthMm = page.Width.Point / PointsPerMillimeter;
            double heightMm = page.Height.Point / PointsPerMillimeter;
            if (widthMm < BuildingArchitectureConceptPageLayout.TitleBlockWidthMm +
                    BuildingArchitectureConceptPageLayout.NormalMarginMm * 2 ||
                heightMm < BuildingArchitectureConceptPageLayout.TitleBlockHeightMm +
                    BuildingArchitectureConceptPageLayout.NormalMarginMm * 2)
            {
                continue;
            }

            string bindEdge = heightMm > widthMm ? "TOP" : "LEFT";
            BuildingArchitectureConceptPageRegions regions =
                BuildingArchitectureConceptPageLayout.Calculate(
                    widthMm,
                    heightMm,
                    bindEdge);
            BuildingArchitectureConceptCornerGrid grid =
                BuildingArchitectureConceptPageLayout.ResolveCornerGrid(regions.TitleBlockArea);
            using XGraphics gfx = XGraphics.FromPdfPage(
                page,
                XGraphicsPdfPageOptions.Append);
            var borderPen = new XPen(XColors.Black, Mm(0.35));
            var finePen = new XPen(XColors.Black, Mm(0.10));
            DrawCanonicalConceptCornerMetadata(
                gfx,
                project,
                grid,
                borderPen,
                finePen,
                clearCanonicalCells: true);
            restampedPages.Add(pageNumber);
        }

        string signature = ComputeCanonicalTitleBlockSignature(project);
        document.Info.Keywords = WithCanonicalTitleBlockSignature(
            document.Info.Keywords,
            signature);
        int pageCount = document.PageCount;
        document.Save(outputPdfPath);
        return new AlbumTitleBlockRestampResult(pageCount, restampedPages);
    }

    public static bool HasCanonicalTitleBlockSignature(
        string pdfPath,
        string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        if (!File.Exists(pdfPath))
            return false;

        try
        {
            using PdfDocument document = PdfReader.Open(
                pdfPath,
                PdfDocumentOpenMode.Import);
            string marker = CanonicalTitleBlockKeywordPrefix +
                signature.Trim().ToLowerInvariant();
            return SplitKeywords(document.Info.Keywords)
                .Contains(marker, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException)
        {
            return false;
        }
    }

    public static string ComputeCanonicalTitleBlockSignature(AlbumProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        CompanyProfile company = project.Company;
        (string Role, string Name) representative = ResolveCompanyRepresentative(project);
        string companyName = CompanyDisplayName(company, project.DesignOrganizationName);
        string clientName = ProjectClientTypes.ResolveCoverPersonName(
            project.InitiationBasis.ClientType,
            project.InitiationBasis.ClientName,
            project.InitiationBasis.ClientRepresentativeName,
            project.ClientName);
        string logoPath = ResolveAlbumAssetPath(project.ProjectFolder, company.LogoPath);
        string logoSha256 = ComputeOptionalAssetSha256(logoPath);
        string payload = JsonSerializer.Serialize(new
        {
            Revision = CanonicalTitleBlockRevision,
            ProjectName = ProjectDisplayName(project),
            CompanyName = companyName,
            CompanyRole = string.IsNullOrWhiteSpace(companyName)
                ? representative.Role
                : $"\"{companyName}\" {representative.Role}".Trim(),
            RepresentativeName = representative.Name?.Trim() ?? "",
            Architect = ResolveArchitect(project),
            PageRoles = ResolvePageRoleSignature(project),
            ClientName = clientName?.Trim() ?? "",
            CompanyShortName = company.ShortName?.Trim() ?? "",
            LogoSha256 = logoSha256,
            company.LogoScale,
            company.LogoOffsetX,
            company.LogoOffsetY,
            Year = DateTime.Now.Year,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static IReadOnlyList<string> ResolvePageRoleSignature(AlbumProject project)
    {
        IEnumerable<(string Owner, IAlbumPageRoleOwner Roles)> owners =
            project.Album.Pages.Select(page => ($"page:{page.Id:N}", (IAlbumPageRoleOwner)page))
                .Concat(project.Album.Composition.Select(component =>
                    ($"component:{component.Id}", (IAlbumPageRoleOwner)component)));
        return owners
            .SelectMany(owner => owner.Roles.RoleAssignments.Select(assignment =>
                string.Join(
                    "|",
                    owner.Owner,
                    AlbumPageRoleCodes.Normalize(assignment.RoleCode),
                    assignment.ParticipantId?.Trim() ?? "",
                    AlbumPageRoleAssignmentResolver.ResolveDocumentName(
                        owner.Roles.RoleAssignments,
                        assignment.RoleCode,
                        project.Participants) ?? "")))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Whether this album's pages carry the concept corner table. Every other
    /// album - the general plan and the working drawings - draws the horizontal
    /// title block instead, at coordinates this restamp cannot resolve from the
    /// page size alone, so their canonical cells are left as their build drew
    /// them rather than overpainted with a second table.
    /// </summary>
    private static bool ConceptAlbumOwnsTheCornerTable(AlbumProject project) =>
        BuildingArchitectureConceptAlbumTemplate.TemplateId.Equals(
            (project.Album.TemplateId ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRestampCanonicalTitleBlock(string? componentCode)
    {
        string code = componentCode?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code))
            return false;
        if (code.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!code.StartsWith("generated:", StringComparison.OrdinalIgnoreCase))
            return false;
        return !code.StartsWith("generated:cover", StringComparison.OrdinalIgnoreCase) &&
            !code.StartsWith("generated:table-of-contents", StringComparison.OrdinalIgnoreCase) &&
            !code.StartsWith(
                ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeOptionalAssetSha256(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "";
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string WithCanonicalTitleBlockSignature(
        string? keywords,
        string signature)
    {
        string marker = CanonicalTitleBlockKeywordPrefix +
            signature.Trim().ToLowerInvariant();
        return string.Join(
            "; ",
            SplitKeywords(keywords)
                .Where(value => !value.StartsWith(
                    CanonicalTitleBlockKeywordPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .Append(marker));
    }

    private static IEnumerable<string> SplitKeywords(string? keywords) =>
        (keywords ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value));
}
