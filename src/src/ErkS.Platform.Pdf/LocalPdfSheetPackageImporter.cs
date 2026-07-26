using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

public sealed record LocalPdfSheetPackageImportResult(
    string ManifestPath,
    string PdfPath,
    int PageCount,
    bool Changed);

/// <summary>
/// Converts a user-selected multi-page PDF into one physical package PDF and
/// one schema-5 logical sheet entry per page.
/// </summary>
public sealed class LocalPdfSheetPackageImporter
{
    private const string PackageBaseName = "pdf-source";
    private const string PackagePdfName = "source.pdf";
    private const string SourceLengthKey = "pdf.sourceLength";
    private const string SourceWriteTicksKey = "pdf.sourceWriteUtcTicks";

    public LocalPdfSheetPackageImportResult Import(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != DesignSourceKind.Pdf)
        {
            throw new ArgumentException("Only PDF design sources can be imported.", nameof(source));
        }

        string nativePath = Path.GetFullPath(source.NativeDocumentPath ?? "");
        if (!File.Exists(nativePath) ||
            !Path.GetExtension(nativePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("PDF source file was not found.", nativePath);
        }

        string inbox = Path.GetFullPath(source.InboxFolder ?? "");
        Directory.CreateDirectory(inbox);
        string packagePdfPath = Path.Combine(inbox, PackagePdfName);
        string manifestPath = Path.Combine(
            inbox,
            PackageBaseName + SheetPackageManifest.ManifestSuffix);
        var sourceInfo = new FileInfo(nativePath);

        SheetPackageLoadResult? existing = TryLoadExisting(manifestPath);
        bool physicalFileUnchanged =
            File.Exists(packagePdfPath) &&
            SourceIdentityMatches(source, sourceInfo);
        if (physicalFileUnchanged &&
            existing is { IsLossless: true, Manifest: not null } &&
            ManifestMatches(project, source, existing.Manifest, packagePdfPath))
        {
            return new LocalPdfSheetPackageImportResult(
                manifestPath,
                packagePdfPath,
                existing.Manifest.Sheets.Count,
                Changed: false);
        }

        bool copyRequired = !PathsEqual(nativePath, packagePdfPath) &&
                            (!File.Exists(packagePdfPath) ||
                             !FilesHaveSameContent(nativePath, packagePdfPath));
        if (copyRequired)
        {
            CopyAtomically(nativePath, packagePdfPath);
        }

        SheetPackageManifest manifest = CreateManifest(project, source, packagePdfPath);
        string writtenManifestPath = SheetPackageWriter.Write(
            manifest,
            inbox,
            PackageBaseName);
        RememberSourceIdentity(source, sourceInfo);
        source.LastPackageAtUtc = manifest.ExportedAtUtc;
        source.Status = DesignSourceStatuses.Connected;

        return new LocalPdfSheetPackageImportResult(
            writtenManifestPath,
            packagePdfPath,
            manifest.Sheets.Count,
            Changed: true);
    }

    private static SheetPackageManifest CreateManifest(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string packagePdfPath)
    {
        string documentTitle = string.IsNullOrWhiteSpace(source.NativeDocumentTitle)
            ? Path.GetFileName(source.NativeDocumentPath)
            : source.NativeDocumentTitle.Trim();
        string pageTitle = Path.GetFileNameWithoutExtension(documentTitle);
        ProjectDesignSourcePurpose purpose =
            ProjectDesignSourceClassification.EffectivePurpose(source);
        ProjectBuildingGroup? building = ResolveBuildingGroup(project, source);

        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
            PackageScope = SheetPackageScope.FullSnapshot,
            ExportMode = "Sheets",
            ProjectId = project.ProjectId,
            StageId = source.StageId?.ToString("N") ?? project.Identity.StageCode,
            WorkPackageId = source.WorkPackageId?.ToString("N") ?? "",
            Source = new SheetPackageSource
            {
                SourceId = source.Id,
                Application = SheetSourceApplication.Pdf,
                ApplicationVersion = string.IsNullOrWhiteSpace(source.ApplicationVersion)
                    ? "PDF"
                    : source.ApplicationVersion.Trim(),
                DocumentPath = Path.GetFullPath(source.NativeDocumentPath),
                DocumentTitle = documentTitle,
                ProjectCode = project.Identity.Code,
            },
        };

        using PdfDocument document = PdfReader.Open(packagePdfPath, PdfDocumentOpenMode.Import);
        IReadOnlyList<string> pageNames = ResolvePageNames(document, pageTitle);
        for (int index = 0; index < document.PageCount; index++)
        {
            PdfPage page = document.Pages[index];
            double widthMm = page.Width.Millimeter;
            double heightMm = page.Height.Millimeter;
            int pageNumber = index + 1;
            manifest.Sheets.Add(new SheetPackageEntry
            {
                SheetId = $"pdf-page-{pageNumber:D4}",
                Number = pageNumber.ToString("D2"),
                Name = pageNames[index],
                Discipline = purpose switch
                {
                    ProjectDesignSourcePurpose.GeneralPlan => "GeneralPlan",
                    ProjectDesignSourcePurpose.Building => "Architecture",
                    _ => "PDF",
                },
                ContentKind = purpose == ProjectDesignSourcePurpose.GeneralPlan
                    ? "master-plan"
                    : "",
                BuildingId = building?.Id ?? "",
                BuildingName = building?.Name ?? "",
                WidthMm = widthMm,
                HeightMm = heightMm,
                PageFormatId = DetectPageFormat(widthMm, heightMm),
                Format = null,
                IsCleanDrawingSpace = false,
                ContentWidthMm = widthMm,
                ContentHeightMm = heightMm,
                PdfFileName = PackagePdfName,
                PdfPageNumber = pageNumber,
                PageCount = 1,
            });
        }

        if (manifest.Sheets.Count == 0)
        {
            throw new InvalidDataException("The selected PDF has no pages.");
        }
        return manifest;
    }

    private static bool ManifestMatches(
        ProjectWorkspace project,
        ProjectDesignSource source,
        SheetPackageManifest manifest,
        string packagePdfPath)
    {
        string documentTitle = string.IsNullOrWhiteSpace(source.NativeDocumentTitle)
            ? Path.GetFileName(source.NativeDocumentPath)
            : source.NativeDocumentTitle.Trim();
        string expectedStageId = source.StageId?.ToString("N") ?? project.Identity.StageCode;
        string expectedWorkPackageId = source.WorkPackageId?.ToString("N") ?? "";
        if (manifest.SchemaVersion != SheetPackageManifest.CurrentSchemaVersion ||
            manifest.PackageScope != SheetPackageScope.FullSnapshot ||
            manifest.Source.Application != SheetSourceApplication.Pdf ||
            !manifest.Source.SourceId.Equals(source.Id, StringComparison.OrdinalIgnoreCase) ||
            !manifest.Source.DocumentPath.Equals(
                Path.GetFullPath(source.NativeDocumentPath),
                StringComparison.OrdinalIgnoreCase) ||
            !manifest.Source.DocumentTitle.Equals(documentTitle, StringComparison.Ordinal) ||
            !manifest.Source.ProjectCode.Equals(
                project.Identity.Code,
                StringComparison.OrdinalIgnoreCase) ||
            !manifest.ProjectId.Equals(project.ProjectId, StringComparison.OrdinalIgnoreCase) ||
            !manifest.StageId.Equals(expectedStageId, StringComparison.OrdinalIgnoreCase) ||
            !manifest.WorkPackageId.Equals(expectedWorkPackageId, StringComparison.OrdinalIgnoreCase) ||
            manifest.Sheets.Count == 0 ||
            manifest.Sheets.Any(entry =>
                !entry.PdfFileName.Equals(PackagePdfName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        ProjectDesignSourcePurpose purpose =
            ProjectDesignSourceClassification.EffectivePurpose(source);
        ProjectBuildingGroup? building = ResolveBuildingGroup(project, source);
        string pageTitle = Path.GetFileNameWithoutExtension(documentTitle);
        using PdfDocument document = PdfReader.Open(packagePdfPath, PdfDocumentOpenMode.Import);
        if (document.PageCount != manifest.Sheets.Count)
        {
            return false;
        }

        IReadOnlyList<string> pageNames = ResolvePageNames(document, pageTitle);
        for (int index = 0; index < manifest.Sheets.Count; index++)
        {
            SheetPackageEntry entry = manifest.Sheets[index];
            int pageNumber = index + 1;
            string expectedName = pageNames[index];
            string expectedDiscipline = purpose switch
            {
                ProjectDesignSourcePurpose.GeneralPlan => "GeneralPlan",
                ProjectDesignSourcePurpose.Building => "Architecture",
                _ => "PDF",
            };
            if (entry.PdfPageNumber != pageNumber ||
                entry.PageCount != 1 ||
                !entry.SheetId.Equals($"pdf-page-{pageNumber:D4}", StringComparison.OrdinalIgnoreCase) ||
                !entry.Number.Equals(pageNumber.ToString("D2"), StringComparison.Ordinal) ||
                !entry.Name.Equals(expectedName, StringComparison.Ordinal) ||
                !entry.Discipline.Equals(expectedDiscipline, StringComparison.OrdinalIgnoreCase) ||
                !entry.BuildingId.Equals(building?.Id ?? "", StringComparison.OrdinalIgnoreCase) ||
                !entry.BuildingName.Equals(building?.Name ?? "", StringComparison.Ordinal) ||
                (purpose == ProjectDesignSourcePurpose.GeneralPlan &&
                 !entry.ContentKind.Equals("master-plan", StringComparison.OrdinalIgnoreCase)) ||
                (purpose != ProjectDesignSourcePurpose.GeneralPlan &&
                 !string.IsNullOrWhiteSpace(entry.ContentKind)))
            {
                return false;
            }
        }

        return File.Exists(packagePdfPath);
    }

    private static IReadOnlyList<string> ResolvePageNames(
        PdfDocument document,
        string fallbackTitle)
    {
        string[] names = Enumerable.Range(1, document.PageCount)
            .Select(pageNumber => document.PageCount == 1
                ? fallbackTitle
                : $"{fallbackTitle} - {pageNumber}")
            .ToArray();
        int[] bookmarkDepths = Enumerable.Repeat(-1, document.PageCount).ToArray();

        try
        {
            ApplyBookmarkNames(document, document.Outlines, depth: 0, names, bookmarkDepths);
        }
        catch
        {
            // Malformed bookmark trees must not prevent the PDF pages from importing.
        }

        return names;
    }

    private static void ApplyBookmarkNames(
        PdfDocument document,
        PdfOutlineCollection outlines,
        int depth,
        string[] names,
        int[] bookmarkDepths)
    {
        foreach (PdfOutline outline in outlines)
        {
            string title = NormalizeBookmarkTitle(outline.Title);
            int pageIndex = FindPageIndex(document, outline.DestinationPage);
            if (pageIndex >= 0 &&
                title.Length > 0 &&
                depth > bookmarkDepths[pageIndex])
            {
                names[pageIndex] = title;
                bookmarkDepths[pageIndex] = depth;
            }

            if (outline.HasChildren)
            {
                ApplyBookmarkNames(
                    document,
                    outline.Outlines,
                    depth + 1,
                    names,
                    bookmarkDepths);
            }
        }
    }

    private static int FindPageIndex(PdfDocument document, PdfPage? destinationPage)
    {
        if (destinationPage is null)
        {
            return -1;
        }

        for (int index = 0; index < document.PageCount; index++)
        {
            PdfPage page = document.Pages[index];
            if (ReferenceEquals(page, destinationPage) ||
                page.Internals.ObjectID.Equals(destinationPage.Internals.ObjectID))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeBookmarkTitle(string? title) =>
        string.Join(
            " ",
            (title ?? "").Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static ProjectBuildingGroup? ResolveBuildingGroup(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        string groupId = ProjectDesignSourceClassification.BuildingGroupId(source);
        return string.IsNullOrWhiteSpace(groupId)
            ? null
            : project.BuildingGroups.FirstOrDefault(group =>
                group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase));
    }

    private static string DetectPageFormat(double widthMm, double heightMm)
    {
        const double tolerance = 1.0;
        if (Near(widthMm, 420, tolerance) && Near(heightMm, 297, tolerance))
            return PageFormatCatalog.ConceptA3LandscapeId;
        if (Near(widthMm, 297, tolerance) && Near(heightMm, 420, tolerance))
            return PageFormatCatalog.ConceptA3PortraitTopId;
        if (Near(widthMm, 210, tolerance) && Near(heightMm, 297, tolerance))
            return PageFormatCatalog.DocumentA4PortraitId;
        return PageFormatCatalog.SourceAsIsId;
    }

    private static bool Near(double left, double right, double tolerance) =>
        Math.Abs(left - right) <= tolerance;

    private static bool SourceIdentityMatches(
        ProjectDesignSource source,
        FileInfo sourceInfo)
    {
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return source.Metadata.TryGetValue(SourceLengthKey, out string? length) &&
               source.Metadata.TryGetValue(SourceWriteTicksKey, out string? writeTicks) &&
               length == sourceInfo.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
               writeTicks == sourceInfo.LastWriteTimeUtc.Ticks.ToString(
                   System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void RememberSourceIdentity(
        ProjectDesignSource source,
        FileInfo sourceInfo)
    {
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[SourceLengthKey] =
            sourceInfo.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        source.Metadata[SourceWriteTicksKey] =
            sourceInfo.LastWriteTimeUtc.Ticks.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SheetPackageLoadResult? TryLoadExisting(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;
        try
        {
            return SheetPackageReader.Load(manifestPath);
        }
        catch
        {
            return null;
        }
    }

    private static bool FilesHaveSameContent(string leftPath, string rightPath)
    {
        var left = new FileInfo(leftPath);
        var right = new FileInfo(rightPath);
        return left.Length == right.Length &&
               SheetPackageReader.ComputeSha256(leftPath).Equals(
                   SheetPackageReader.ComputeSha256(rightPath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyAtomically(string sourcePath, string destinationPath)
    {
        string temporaryPath =
            destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: true);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
}
