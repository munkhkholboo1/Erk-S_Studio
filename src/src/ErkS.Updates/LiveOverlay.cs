using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace ErkS.Updates;

/// <summary>
/// The user's live layer (contract 5a, Master 2026-10-26): a product installed by an administrator
/// (Program Files, ProgramData) cannot have its «live» files replaced by the person using it, so a
/// no-close update would wait for the next elevated install. The live files go to
/// <c>%LOCALAPPDATA%\Erk-S\&lt;product&gt;\overlay\&lt;version&gt;</c> instead, beside the signed manifest
/// they came with; the host asks <see cref="Resolve"/> for every live file it loads and gets the
/// overlay copy only while the manifest's signature (pinned), the file's hash and - for an ErkS
/// binary - its signer all hold, else the installed file. The next full or elevated install clears it.
/// One function for every product (CLAUDE.md §1): CGA, CGM, PFA, PFR and Studio call this, none
/// keeps its own.
/// </summary>
public static class LiveOverlay
{
    public const string SignedManifestName = "manifest.signed.json";
    private const string CurrentName = "current.txt";

    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, string Sha)> Hashed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, UpdateManifest? Manifest)> Verified = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The overlay of a product and channel: <c>%LOCALAPPDATA%\Erk-S\&lt;product&gt;[\&lt;channel&gt;]\overlay</c>
    /// (or under <paramref name="baseDirectory"/>, tests). One code base installed for several hosts
    /// (Revit/AutoCAD 2026, 2027) has one overlay per channel, so one year's full install never clears
    /// another's (Master, 2026-10-26). An empty channel is the product's single one.
    /// </summary>
    public static string Root(string productCode, string? baseDirectory = null, string? channel = null)
    {
        string product = Path.Combine(
            baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Erk-S"),
            Safe(productCode));
        return string.IsNullOrWhiteSpace(channel)
            ? Path.Combine(product, "overlay")
            : Path.Combine(product, Safe(channel!.Trim()), "overlay");
    }

    /// <summary>Whether this process can write the install folder (else the live files go to the overlay).</summary>
    public static bool CanWrite(string installDirectory)
    {
        try
        {
            string probe = Path.Combine(installDirectory, ".erks-write-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The plan's live files, where this user can put them: in place when the install folder is
    /// writable (<see cref="UpdateApplier.ApplyLive"/>), else in the overlay. The caller raises its
    /// ReloadRequested with <see cref="LiveApplyResult.Applied"/>.
    /// </summary>
    /// <param name="signedManifestJson">The server's envelope exactly as received (<see cref="DeltaDownloader.LastSignedManifestJson"/>).</param>
    public static LiveApplyResult ApplyLive(string installDirectory, string stagingDirectory, UpdatePlan plan, string signedManifestJson, string? baseDirectory = null) =>
        CanWrite(installDirectory)
            ? UpdateApplier.ApplyLive(installDirectory, stagingDirectory, plan)
            : Apply(plan.Target.ProductCode, stagingDirectory, plan, signedManifestJson, baseDirectory);

    /// <summary>Puts the plan's live files in this user's overlay for the target version, with the signed manifest.</summary>
    public static LiveApplyResult Apply(string productCode, string stagingDirectory, UpdatePlan plan, string signedManifestJson, string? baseDirectory = null, string pinnedThumbprint = ManifestSignature.ErkSThumbprint)
    {
        UpdateManifest verified = SignedManifest.FromJson(signedManifestJson).Verify(pinnedThumbprint);
        if (!verified.Version.Equals(plan.Target.Version, StringComparison.OrdinalIgnoreCase) ||
            !verified.ProductCode.Equals(productCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Overlay-ийн манифест шинэчлэлийн хувилбартай таарахгүй байна.");
        }
        string root = Root(productCode, baseDirectory, verified.Channel);
        string version = Path.Combine(root, Safe(verified.Version));
        var result = new LiveApplyResult();
        Directory.CreateDirectory(version);
        foreach (UpdateFile file in plan.Changed.Where(file => file.IsLive))
        {
            string staged = DeltaDownloader.StagedPath(stagingDirectory, file.Sha256);
            if (!File.Exists(staged) || Hashes.Sha256OfFile(staged) != file.Sha256)
                throw new InvalidDataException("Шинэчлэлийн файл бүрэн биш: " + file.Path);
            string target = InstallManifest.Local(version, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + ".erks-new";
            File.Copy(staged, temporary, overwrite: true);
            if (File.Exists(target))
                File.Delete(target);
            File.Move(temporary, target);
            result.Applied.Add(file.Path);
        }
        // The live files the plan did not change are the installed ones - the overlay names only what it holds.
        File.WriteAllText(Path.Combine(version, SignedManifestName), signedManifestJson);
        File.WriteAllText(Path.Combine(root, CurrentName), verified.Version);
        foreach (string older in Directory.EnumerateDirectories(root).Where(path => !path.Equals(version, StringComparison.OrdinalIgnoreCase)))
            TryDelete(older);
        return result;
    }

    /// <summary>
    /// The file a host loads for <paramref name="relativePath"/>: the overlay copy while it is the
    /// signed manifest's live content for a version newer than the installed one, else the installed file.
    /// </summary>
    /// <param name="channel">The host's channel (Revit/AutoCAD year) - the one its updates were published under.</param>
    public static string Resolve(string installDirectory, string productCode, string relativePath, string? baseDirectory = null, string pinnedThumbprint = ManifestSignature.ErkSThumbprint, string? channel = null)
    {
        string installed = InstallManifest.Local(installDirectory, relativePath.Replace('\\', '/'));
        try
        {
            if (Current(installDirectory, productCode, baseDirectory, pinnedThumbprint, channel) is not { } overlay)
                return installed;
            (string folder, UpdateManifest? manifest) = overlay;
            string normal = relativePath.Replace('\\', '/');
            UpdateFile? entry = manifest?.Files.FirstOrDefault(file => file.IsLive && file.Path.Equals(normal, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return installed;
            string candidate = InstallManifest.Local(folder, entry.Path);
            if (!File.Exists(candidate) || HashOf(candidate) != entry.Sha256 || !SignerHolds(candidate, pinnedThumbprint))
                return installed;
            return candidate;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return installed;
        }
    }

    /// <summary>
    /// The version the host is at once its live files are counted: the verified overlay's version when
    /// it is newer than the install (a live-only update moved it), else null - the install's own
    /// version stands (PFA, 2026-10-26: after a live-only update the check kept offering the same version).
    /// </summary>
    public static string? TryReadVersion(string installDirectory, string productCode, string? baseDirectory = null, string pinnedThumbprint = ManifestSignature.ErkSThumbprint, string? channel = null)
    {
        try
        {
            return Current(installDirectory, productCode, baseDirectory, pinnedThumbprint, channel) is { Manifest: { } manifest } ? manifest.Version : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The overlay that counts: its folder and verified manifest, when its version is newer than the install's.</summary>
    private static (string Folder, UpdateManifest? Manifest)? Current(string installDirectory, string productCode, string? baseDirectory, string pinnedThumbprint, string? channel)
    {
        string root = Root(productCode, baseDirectory, channel);
        string current = Path.Combine(root, CurrentName);
        if (!File.Exists(current))
            return null;
        string version = File.ReadAllText(current).Trim();
        // An install as new as the overlay (or newer) carries the files itself.
        if (InstallManifest.TryReadVersion(installDirectory) is { } installedVersion && Compare(installedVersion, version) >= 0)
            return null;
        string folder = Path.Combine(root, Safe(version));
        UpdateManifest? manifest = VerifiedManifest(Path.Combine(folder, SignedManifestName), pinnedThumbprint);
        return manifest is not null && manifest.Version.Equals(version, StringComparison.OrdinalIgnoreCase) ? (folder, manifest) : null;
    }

    /// <summary>The next full or elevated install carries everything: that channel's overlay goes.</summary>
    public static void Clear(string productCode, string? baseDirectory = null, string? channel = null) => TryDelete(Root(productCode, baseDirectory, channel));

    private static UpdateManifest? VerifiedManifest(string path, string pinnedThumbprint)
    {
        if (!File.Exists(path))
            return null;
        var info = new FileInfo(path);
        string key = path + "|" + pinnedThumbprint; // verified against THIS pin only
        if (Verified.TryGetValue(key, out var seen) && seen.Length == info.Length && seen.Written == info.LastWriteTimeUtc)
            return seen.Manifest;
        UpdateManifest? manifest;
        try
        {
            manifest = SignedManifest.FromJson(File.ReadAllText(path)).Verify(pinnedThumbprint);
        }
        catch (InvalidDataException)
        {
            manifest = null;
        }
        Verified[key] = (info.Length, info.LastWriteTimeUtc, manifest);
        return manifest;
    }

    private static string HashOf(string path)
    {
        var info = new FileInfo(path);
        if (Hashed.TryGetValue(path, out var seen) && seen.Length == info.Length && seen.Written == info.LastWriteTimeUtc)
            return seen.Sha;
        string sha = Hashes.Sha256OfFile(path);
        Hashed[path] = (info.Length, info.LastWriteTimeUtc, sha);
        return sha;
    }

    /// <summary>An ErkS exe/dll in the overlay must carry the Erk-S signature when it carries one at all.</summary>
    private static bool SignerHolds(string path, string pinnedThumbprint)
    {
        string extension = Path.GetExtension(path);
        if (!(extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) ||
            !Path.GetFileName(path).StartsWith("ErkS.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        try
        {
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return signer.Thumbprint.Equals(pinnedThumbprint, StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return true; // unsigned: the signed manifest's hash vouches for it
        }
    }

    /// <summary>Release versions compared by their numbers («v0.001.105» &lt; «V0.001.106»).</summary>
    public static int Compare(string left, string right)
    {
        long[] a = Numbers(left), b = Numbers(right);
        for (int index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            long x = index < a.Length ? a[index] : 0, y = index < b.Length ? b[index] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    private static long[] Numbers(string version) =>
        version.Trim().TrimStart('v', 'V').Split('.', '-', '+').Select(part => long.TryParse(part, out long n) ? n : 0).ToArray();

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Safe(string value) =>
        string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
