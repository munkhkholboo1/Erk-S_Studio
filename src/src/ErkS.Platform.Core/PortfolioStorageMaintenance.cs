namespace ErkS.Platform.Core;

/// <summary>
/// Keeps the portfolio's own file store to what the portfolio still shows.
///
/// Portfolio files are content addressed, so re-exporting a page that changed
/// writes a new file and leaves the old one behind. Nothing points at it any
/// more, and it would sit in the project forever.
/// </summary>
public static class PortfolioStorageMaintenance
{
    /// <summary>
    /// Deletes portfolio files no item refers to, and returns how many went.
    /// Items the user removed still count as referring to their file, so a
    /// page taken out can still be restored with its drawing intact.
    /// </summary>
    public static int RemoveUnreferencedFiles(ProjectWorkspace project, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        string folder;
        try
        {
            folder = Path.Combine(
                ProjectWorkspacePaths.GetProjectFolder(projectPath),
                "foundation",
                "documents",
                ProjectDocumentCategories.Portfolio);
        }
        catch (InvalidDataException)
        {
            return 0;
        }
        if (!Directory.Exists(folder))
            return 0;

        HashSet<string> referenced = project.Portfolio.Items
            .Select(item => (item.RelativePath ?? "").Trim())
            .Where(path => path.Length > 0)
            .Select(path => Path.GetFileName(path))
            .Where(name => name.Length > 0)
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
                // A file held open elsewhere is left for the next pass; tidying
                // storage must never fail the work that triggered it.
            }
        }

        return removed;
    }
}
