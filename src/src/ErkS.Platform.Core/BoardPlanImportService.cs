namespace ErkS.Platform.Core;

/// <summary>
/// A general plan the project holds, rather than one it merely points at.
///
/// It is kept the way a delivered page is kept: copied into the project, named
/// by its content, and remembering where it came from so a later export can
/// refresh it in place. A card cites it by id, so re-importing brings the new
/// drawing to every board that shows it without any of them being touched.
/// </summary>
public sealed class ProjectBoardPlanAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Shown when choosing one. Defaults to the drawing's own name.</summary>
    public string Title { get; set; } = "";

    /// <summary>Project-relative path of the copy this project owns.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Where it was imported from. Kept so re-importing the same export
    /// refreshes this asset instead of adding a second one beside it.
    /// </summary>
    public string SourcePath { get; set; } = "";

    public string SourceSha256 { get; set; } = "";

    /// <summary>The drawing the export was taken from, as the file itself says.</summary>
    public string SourceDocument { get; set; } = "";

    public int ObjectCount { get; set; }

    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Title = (Title ?? "").Trim();
        RelativePath = (RelativePath ?? "").Trim();
        SourcePath = (SourcePath ?? "").Trim();
        SourceSha256 = (SourceSha256 ?? "").Trim();
        SourceDocument = (SourceDocument ?? "").Trim();
        ObjectCount = Math.Max(0, ObjectCount);
    }

    public ProjectBoardPlanAsset Clone() => new()
    {
        Id = Id,
        Title = Title,
        RelativePath = RelativePath,
        SourcePath = SourcePath,
        SourceSha256 = SourceSha256,
        SourceDocument = SourceDocument,
        ObjectCount = ObjectCount,
        ImportedAtUtc = ImportedAtUtc,
    };
}

public sealed record BoardPlanImportResult(
    ProjectBoardPlanAsset? Asset,
    bool Created,
    bool Refreshed,
    IReadOnlyList<string> Issues)
{
    public bool Succeeded => Asset is not null;

    /// <summary>The same file again, unchanged. Nothing was written.</summary>
    public bool Unchanged => Succeeded && !Created && !Refreshed;
}

public static class BoardPlanImportService
{
    /// <summary>
    /// Brings a CityGen export into the project.
    ///
    /// The file is read before it is copied. A project should not end up
    /// holding something that is not the contract it claims to be - the copy
    /// would then fail every time a board was built, long after the moment when
    /// the reason was obvious.
    /// </summary>
    public static BoardPlanImportResult Import(
        ProjectBoardSeries series,
        string projectPath,
        string sourcePlanPath)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePlanPath);
        series.Plans ??= [];

        string fullSourcePath;
        try
        {
            fullSourcePath = Path.GetFullPath(sourcePlanPath.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new BoardPlanImportResult(null, false, false, [exception.Message]);
        }

        if (!File.Exists(fullSourcePath))
            return new BoardPlanImportResult(null, false, false, ["Файл олдсонгүй."]);

        CityGenBoardLoadResult loaded = CityGenGraphicBoardReader.Load(fullSourcePath);
        if (!loaded.IsLoaded)
            return new BoardPlanImportResult(null, false, false, loaded.Issues);

        string sha;
        try
        {
            sha = ProjectDocumentFileStore.ComputeSha256(fullSourcePath);
        }
        catch (IOException exception)
        {
            return new BoardPlanImportResult(null, false, false, [exception.Message]);
        }

        ProjectBoardPlanAsset? existing = series.Plans.FirstOrDefault(asset =>
            string.Equals(asset.SourcePath, fullSourcePath, StringComparison.OrdinalIgnoreCase));

        // The same file again: say so rather than writing a second copy of it.
        if (existing is not null &&
            string.Equals(existing.SourceSha256, sha, StringComparison.OrdinalIgnoreCase) &&
            FileStillThere(projectPath, existing.RelativePath))
        {
            return new BoardPlanImportResult(existing, false, false, loaded.SkippedObjects);
        }

        string relativePath;
        try
        {
            relativePath = Store(projectPath, fullSourcePath, sha);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new BoardPlanImportResult(null, false, false, [exception.Message]);
        }

        CityGenBoardManifest manifest = loaded.Manifest!;
        string title = string.IsNullOrWhiteSpace(manifest.SourceDocument)
            ? Path.GetFileNameWithoutExtension(fullSourcePath)
            : Path.GetFileNameWithoutExtension(manifest.SourceDocument);

        if (existing is not null)
        {
            // Refreshed in place, keeping its identity, so every card showing
            // this plan gets the new drawing without being touched.
            existing.RelativePath = relativePath;
            existing.SourceSha256 = sha;
            existing.SourceDocument = manifest.SourceDocument;
            existing.ObjectCount = manifest.Objects.Count;
            existing.ImportedAtUtc = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(existing.Title))
                existing.Title = title;
            existing.Normalize();
            return new BoardPlanImportResult(existing, false, true, loaded.SkippedObjects);
        }

        var created = new ProjectBoardPlanAsset
        {
            Title = title,
            RelativePath = relativePath,
            SourcePath = fullSourcePath,
            SourceSha256 = sha,
            SourceDocument = manifest.SourceDocument,
            ObjectCount = manifest.Objects.Count,
        };
        created.Normalize();
        series.Plans.Add(created);
        return new BoardPlanImportResult(created, true, false, loaded.SkippedObjects);
    }

    /// <summary>
    /// Copies the export into the project, named by its content.
    ///
    /// The document store is not used for this. It accepts only what a project
    /// shows a person - a PDF, a photograph - and a classified plan is data the
    /// board draws from rather than a document anyone opens. The convention is
    /// the same, which is what matters: one folder, content-addressed names, so
    /// an export that has not changed is never written twice.
    /// </summary>
    private static string Store(string projectPath, string sourcePath, string sha)
    {
        string folder = Path.Combine(
            ProjectWorkspacePaths.GetProjectFolder(projectPath),
            "foundation",
            "documents",
            ProjectDocumentCategories.BoardPlan);
        Directory.CreateDirectory(folder);

        string targetPath = Path.Combine(folder, sha + CityGenGraphicBoardContract.SidecarSuffix);
        if (!File.Exists(targetPath))
            File.Copy(sourcePath, targetPath, overwrite: false);
        return ProjectWorkspacePaths.ToRelativePath(projectPath, targetPath);
    }

    private static bool FileStillThere(string projectPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        try
        {
            return File.Exists(ProjectWorkspacePaths.ResolveInsideProject(projectPath, relativePath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or
                InvalidDataException)
        {
            return false;
        }
    }
}

/// <summary>
/// Keeps the board plan store to what the boards still cite, for the same
/// reason the portfolio's store is kept: the files are content addressed, so
/// re-importing a plan that changed leaves the old copy behind with nothing
/// pointing at it.
/// </summary>
public static class BoardPlanStorageMaintenance
{
    public static int RemoveUnreferencedFiles(ProjectBoardSeries series, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        string folder;
        try
        {
            folder = Path.Combine(
                ProjectWorkspacePaths.GetProjectFolder(projectPath),
                "foundation",
                "documents",
                ProjectDocumentCategories.BoardPlan);
        }
        catch (InvalidDataException)
        {
            return 0;
        }
        if (!Directory.Exists(folder))
            return 0;

        HashSet<string> referenced = (series.Plans ?? [])
            .Select(asset => Path.GetFileName((asset.RelativePath ?? "").Trim()))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int removed = 0;
        foreach (string path in Directory.EnumerateFiles(folder))
        {
            if (referenced.Contains(Path.GetFileName(path)))
                continue;
            try
            {
                File.Delete(path);
                removed++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // A file in use is left alone; tidying must never fail a save.
            }
        }
        return removed;
    }
}
