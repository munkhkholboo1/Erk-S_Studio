using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErkS.Updates;

/// <summary>One installed file: its path inside the install folder (forward slashes), hash and size.</summary>
public sealed class UpdateFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>SHA-256 of the file, lower-case hex - also the file's address on the server.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// «live» - replaced while the program runs (settings, templates, icons, fonts, a reloadable
    /// App); «restart» (the default) - a loaded exe/dll, a host plugin: needs the program closed.
    /// The product's publish script sets it (contract 5a).
    /// </summary>
    [JsonPropertyName("apply")]
    public string Apply { get; set; } = ApplyRestart;

    public const string ApplyLive = "live";

    public const string ApplyRestart = "restart";

    [JsonIgnore]
    public bool IsLive => ApplyLive.Equals(Apply, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A version's file list as the server publishes it (contract: data\products\&lt;CODE&gt;\manifest-&lt;version&gt;.json).
/// The same shape is kept beside an install as <see cref="InstallManifest.FileName"/>.
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("files")]
    public List<UpdateFile> Files { get; set; } = new();

    /// <summary>Paths the version no longer has; an older install drops them.</summary>
    [JsonPropertyName("removed")]
    public List<string> Removed { get; set; } = new();

    /// <summary>
    /// Where the server keeps this product's files by hash (SRV: «/updates/&lt;folder&gt;/files»);
    /// empty in an install manifest. The downloader falls back to the product code.
    /// </summary>
    [JsonPropertyName("filesUrl")]
    public string FilesUrl { get; set; } = "";

    public long TotalBytes => Files.Sum(file => file.Size);

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static UpdateManifest FromJson(string json) =>
        JsonSerializer.Deserialize<UpdateManifest>(json, Json) ?? throw new InvalidDataException("Шинэчлэлийн манифест хоосон байна.");
}

/// <summary>
/// What is installed now: «.erks-install.json» beside the program. The installer writes it; when it
/// is missing or damaged, the install folder is hashed once and the file is made.
/// </summary>
public static class InstallManifest
{
    public const string FileName = ".erks-install.json";

    /// <summary>Folder names never part of an install (a WebView2 profile, an earlier update's leftovers).</summary>
    public static readonly string[] SkippedFolders = { ".rollback", ".staging" };

    public static UpdateManifest LoadOrScan(string installDirectory, string productCode, string version, Func<string, bool>? include = null)
    {
        string path = System.IO.Path.Combine(installDirectory, FileName);
        try
        {
            if (File.Exists(path))
            {
                UpdateManifest saved = UpdateManifest.FromJson(File.ReadAllText(path, Encoding.UTF8));
                if (saved.Files.Count > 0 && saved.ProductCode.Equals(productCode, StringComparison.OrdinalIgnoreCase))
                    return saved;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
        }
        UpdateManifest scanned = Scan(installDirectory, productCode, version, include);
        Save(installDirectory, scanned);
        return scanned;
    }

    /// <summary>The version the install manifest names, or null when there is none (a build not installed by an installer).</summary>
    public static string? TryReadVersion(string installDirectory)
    {
        try
        {
            string path = System.IO.Path.Combine(installDirectory, FileName);
            return File.Exists(path) ? UpdateManifest.FromJson(File.ReadAllText(path, Encoding.UTF8)).Version : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Hashes every file in the folder (not the manifest itself, not skipped folders).</summary>
    public static UpdateManifest Scan(string directory, string productCode, string version, Func<string, bool>? include = null)
    {
        var manifest = new UpdateManifest { ProductCode = productCode, Version = version };
        string root = System.IO.Path.GetFullPath(directory);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(name => name, StringComparer.Ordinal))
        {
            string relative = Relative(root, file);
            if (relative.Equals(FileName, StringComparison.OrdinalIgnoreCase) ||
                SkippedFolders.Any(skip => relative.StartsWith(skip + "/", StringComparison.OrdinalIgnoreCase)) ||
                (include is not null && !include(relative)))
            {
                continue;
            }
            manifest.Files.Add(new UpdateFile { Path = relative, Sha256 = Hashes.Sha256OfFile(file), Size = new FileInfo(file).Length });
        }
        return manifest;
    }

    public static void Save(string installDirectory, UpdateManifest manifest)
    {
        string path = System.IO.Path.Combine(installDirectory, FileName);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, manifest.ToJson(), new UTF8Encoding(false));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporary, path);
    }

    /// <summary>A path inside the install, with forward slashes - the form every manifest uses.</summary>
    public static string Relative(string root, string full)
    {
        string prefix = root.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Файл суулгалтын хавтсанд алга: " + full);
        return full.Substring(prefix.Length).Replace('\\', '/');
    }

    /// <summary>A manifest path made safe: relative, no «..», no rooted or drive form.</summary>
    public static string Local(string root, string manifestPath)
    {
        string normal = manifestPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        if (normal.Length == 0 || System.IO.Path.IsPathRooted(normal) || normal.Split(System.IO.Path.DirectorySeparatorChar).Any(part => part == ".." || part.Length == 0))
            throw new InvalidDataException("Манифестын зам буруу: " + manifestPath);
        return System.IO.Path.Combine(root, normal);
    }
}

internal static class Hashes
{
    public static string Sha256OfFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using SHA256 sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream));
    }

    public static string Hex(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
            text.Append(b.ToString("x2"));
        return text.ToString();
    }

    public static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
}
