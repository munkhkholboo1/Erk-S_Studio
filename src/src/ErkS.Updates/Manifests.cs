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
/// The server's answer: the manifest's exact JSON text and a signature over its UTF-8 bytes.
/// The text travels as a string, so the bytes that were signed are the bytes that are checked -
/// no re-serialising, no canonical form to agree on.
/// </summary>
public sealed class SignedManifest
{
    [JsonPropertyName("manifestJson")]
    public string ManifestJson { get; set; } = "";

    /// <summary>Base64 of a detached CMS (PKCS#7) signature by the Erk-S certificate.</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this, UpdateManifest.Json);

    public static SignedManifest FromJson(string json) =>
        JsonSerializer.Deserialize<SignedManifest>(json, UpdateManifest.Json) ?? throw new InvalidDataException("Гарын үсэгтэй манифест хоосон байна.");

    /// <summary>The manifest, only once its signature is good and made by the pinned certificate.</summary>
    public UpdateManifest Verify(string pinnedThumbprint)
    {
        if (!ManifestSignature.Verify(ManifestJson, Signature, pinnedThumbprint))
            throw new InvalidDataException("Шинэчлэлийн манифестын гарын үсэг таарахгүй байна — татахгүй.");
        return UpdateManifest.FromJson(ManifestJson);
    }
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
