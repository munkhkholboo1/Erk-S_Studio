namespace ErkS.Sync.Core;

/// <summary>Why a folder cannot become, or join, a sync folder - a code and a sentence.</summary>
public sealed record SyncFolderRefusal(string Code, string Message);

/// <summary>
/// The checks before a folder is registered («Erk-S Sync фолдер болгох») or joined
/// («Erk-S Sync фолдерт холбох»). One function for both, so the two verbs can never disagree
/// about what a usable folder is.
/// </summary>
public static class SyncFolderEligibility
{
    public const string NotFoundCode = "sync_folder_not_found";
    public const string DriveRootCode = "sync_folder_drive_root";
    public const string InsideSyncFolderCode = "sync_folder_inside_sync_folder";
    public const string ContainsSyncFolderCode = "sync_folder_contains_sync_folder";
    public const string AlreadySyncFolderCode = "sync_folder_already_registered";
    public const string ResilioActiveCode = "sync_folder_resilio_active";
    public const string PathMismatchCode = "sync_folder_path_mismatch";

    /// <summary>Refusal for registering <paramref name="folder"/> as a new sync folder, or null.</summary>
    public static SyncFolderRefusal? CheckMake(string folder) => CheckCommon(folder, alreadyRegisteredRefuses: true);

    /// <summary>
    /// Refusal for joining <paramref name="folder"/> to a sync folder registered at
    /// <paramref name="canonicalPath"/>, or null.
    /// </summary>
    /// <remarks>
    /// 🔴 THE PATH MUST BE EXACTLY THE REGISTERED ONE - whole path, case aside. A copy at
    /// another place would break every xref and link it holds; refusing loudly beats a folder
    /// that «works» and opens drawings with missing references.
    /// </remarks>
    public static SyncFolderRefusal? CheckJoin(string folder, string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (!SyncPath.Same(folder, canonicalPath))
        {
            return new SyncFolderRefusal(
                PathMismatchCode,
                $"Энэ хавтас «{SyncPath.Canonical(canonicalPath)}» замд байх ёстой. Замыг өөрчлөөд дахин оролдоно уу.");
        }
        return CheckCommon(folder, alreadyRegisteredRefuses: false);
    }

    private static SyncFolderRefusal? CheckCommon(string folder, bool alreadyRegisteredRefuses)
    {
        string canonical = SyncPath.Canonical(folder);
        if (!Directory.Exists(canonical))
            return new SyncFolderRefusal(NotFoundCode, $"«{canonical}» хавтас олдсонгүй.");
        if (SyncPath.IsDriveRoot(canonical))
            return new SyncFolderRefusal(DriveRootCode, "Дискийн үндсийг бүхэлд нь синк хийхгүй. Төслийн хавтсаа сонгоно уу.");

        string? enclosing = SyncPath.IsDriveRoot(canonical)
            ? null
            : SyncFolderMarker.FindEnclosing(Path.GetDirectoryName(canonical)!);
        if (enclosing is not null)
            return new SyncFolderRefusal(InsideSyncFolderCode, $"Энэ хавтас «{enclosing}» синк хавтсан дотор байна.");

        if (alreadyRegisteredRefuses && SyncFolderMarker.TryRead(canonical) is { } existing)
        {
            return new SyncFolderRefusal(
                AlreadySyncFolderCode,
                $"Энэ хавтас аль хэдийн «{existing.ProjectCode}» төслийн синк хавтас байна.");
        }

        string? nested = SyncFolderMarker.FindNested(canonical);
        if (nested is not null)
            return new SyncFolderRefusal(ContainsSyncFolderCode, $"Энэ хавтас дотор «{nested}» синк хавтас байна.");

        string? resilio = ResilioDetector.FindMarker(canonical);
        if (resilio is not null)
        {
            return new SyncFolderRefusal(
                ResilioActiveCode,
                $"Resilio энэ хавтсыг хөтөлж байна («{resilio}»). Энэ хавтсыг тэр Resilio хавтасны «.sync/IgnoreList»-д «/{Path.GetFileName(canonical)}» мөрөөр нэмэх, эсвэл Resilio-г зогсоогоод дахин оролдоно уу.");
        }
        return null;
    }
}

/// <summary>
/// Whether Resilio Sync is keeping this folder. Two syncers in one folder fight each other
/// (plan §4.6), so registration stops while Resilio holds it.
/// </summary>
public static class ResilioDetector
{
    /// <summary>
    /// The first Resilio trace found, or null: a «.sync» folder here or above - unless that
    /// share's IgnoreList leaves this folder out - or a «*.!sync» file below.
    /// </summary>
    public static string? FindMarker(string folder)
    {
        string canonical = SyncPath.Canonical(folder);
        DirectoryInfo? current = new(canonical);
        while (current is not null)
        {
            string marker = Path.Combine(current.FullName, ".sync");
            if (Directory.Exists(marker))
            {
                if (IsIgnoredByShare(marker, SyncPath.Relative(current.FullName, canonical)))
                    break;
                return marker;
            }
            current = current.Parent;
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            return Directory.EnumerateFiles(SyncPath.Canonical(folder), "*.!sync", options).FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the share's «.sync\IgnoreList» leaves out <paramref name="relativePath"/> (or a
    /// folder above it inside the share). Resilio's rules: «/A/B» is anchored at the share root,
    /// a plain name matches that name at any depth, «?» and «*» are wildcards, «#» a comment.
    /// The list is each machine's own - it is not synced - so this is the machine's own answer.
    /// </summary>
    private static bool IsIgnoredByShare(string syncFolder, string relativePath)
    {
        string list = Path.Combine(syncFolder, "IgnoreList");
        string[] lines;
        try
        {
            if (!File.Exists(list))
                return false;
            lines = File.ReadAllLines(list);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        string[] segments = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false; // the share itself is never «ignored» by its own list
        foreach (string raw in lines)
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            string rule = line.Replace('\\', '/');
            bool anchored = rule.StartsWith('/');
            string[] parts = rule.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;
            if (anchored || parts.Length > 1)
            {
                if (parts.Length <= segments.Length &&
                    parts.Select((part, index) => Matches(part, segments[index])).All(match => match))
                {
                    return true;
                }
            }
            else if (segments.Any(segment => Matches(parts[0], segment)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Matches(string pattern, string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            name,
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
