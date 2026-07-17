using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ErkS.Platform.Core;

public static class CompanySyncStatuses
{
    public const string Cloud = "Cloud";
    public const string PendingCreate = "PendingCreate";
    public const string PendingUpdate = "PendingUpdate";
    public const string ProjectSnapshot = "ProjectSnapshot";
}

public sealed class CompanyCatalogEntry
{
    public CompanyProfile Profile { get; set; } = new();
    public bool CanManage { get; set; }
    public string CurrentUserRole { get; set; } = "";
    public string SyncStatus { get; set; } = CompanySyncStatuses.Cloud;
    public bool LogoRemovalPending { get; set; }
    public DateTimeOffset CachedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Account-scoped offline cache for the Cloud ERA company catalog. This is not
/// an interchange format: server organization ids remain the source of truth.
/// </summary>
public sealed class CompanyLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string catalogPath;
    private readonly string assetFolder;

    public CompanyLibraryStore(string catalogPath, string assetFolder)
    {
        this.catalogPath = Path.GetFullPath(catalogPath);
        this.assetFolder = Path.GetFullPath(assetFolder);
    }

    public static CompanyLibraryStore ForAccount(string accountEmail)
    {
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((accountEmail ?? "").Trim().ToLowerInvariant())))
            .ToLowerInvariant()[..20];
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Erk-S Studio",
            "company-cache",
            identity);
        return new CompanyLibraryStore(Path.Combine(root, "companies.json"), Path.Combine(root, "logos"));
    }

    public IReadOnlyList<CompanyCatalogEntry> Load()
    {
        if (!File.Exists(catalogPath))
        {
            return [];
        }

        try
        {
            List<CompanyCatalogEntry> entries = JsonSerializer.Deserialize<List<CompanyCatalogEntry>>(
                File.ReadAllText(catalogPath),
                JsonOptions) ?? [];
            foreach (CompanyCatalogEntry entry in entries)
            {
                entry.Profile ??= new CompanyProfile();
                entry.Profile.Normalize();
                entry.CurrentUserRole ??= "";
                entry.SyncStatus = string.IsNullOrWhiteSpace(entry.SyncStatus)
                    ? CompanySyncStatuses.Cloud
                    : entry.SyncStatus;
            }
            return entries;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<CompanyCatalogEntry> entries)
    {
        List<CompanyCatalogEntry> normalized = entries.ToList();
        foreach (CompanyCatalogEntry entry in normalized)
        {
            entry.Profile ??= new CompanyProfile();
            entry.Profile.Normalize();
            entry.CachedAtUtc = DateTimeOffset.UtcNow;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        string temporaryPath = catalogPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, catalogPath, true);
    }

    public string StoreLogo(string organizationId, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Logo image was not found.", sourcePath);
        }

        string extension = NormalizeImageExtension(Path.GetExtension(sourcePath));
        Directory.CreateDirectory(assetFolder);
        string targetPath = Path.Combine(assetFolder, SafeOrganizationId(organizationId) + extension);
        File.Copy(sourcePath, targetPath, true);
        return targetPath;
    }

    public string StoreLogo(string organizationId, byte[] bytes, string contentType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            throw new InvalidDataException("Logo image is empty.");
        }

        string extension = contentType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => throw new InvalidDataException("Logo must be a PNG or JPEG image."),
        };
        Directory.CreateDirectory(assetFolder);
        string targetPath = Path.Combine(assetFolder, SafeOrganizationId(organizationId) + extension);
        File.WriteAllBytes(targetPath, bytes);
        return targetPath;
    }

    private static string NormalizeImageExtension(string extension)
    {
        return extension.Trim().ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ".jpg",
            ".png" => ".png",
            _ => throw new InvalidDataException("Logo must be a PNG or JPEG image."),
        };
    }

    private static string SafeOrganizationId(string organizationId)
    {
        string value = new((organizationId ?? "")
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(100)
            .ToArray());
        return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
    }
}
