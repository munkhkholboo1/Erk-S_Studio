using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using ErkS.Platform.Core;
using ErkS.Sync.Core;

namespace ErkS.Studio;

/// <summary>
/// Studio installs Dokany itself when a central needs it (Erk-S Sync / owner, 2026-10-27: «энэ үйлдлээ
/// өөрөө хийгээд найдвартай ажиллагаатай болго … central model дээр олон талаас ажиллах»): the pinned
/// official msi is fetched, its SHA-256 and its Authenticode signature (LEOSAC) are checked, and Windows
/// asks the person once (UAC) - nothing is installed without that answer. Sync then tries the mount at once.
/// </summary>
internal static class StudioDokany
{
    internal enum Outcome { Installed, Declined, Failed }

    internal sealed record Result(Outcome Outcome, string MessageMn);

    public static string InstalledVersion => SyncDokany.InstalledVersion();

    public static bool NeedsInstall => SyncDokany.NeedsInstall(InstalledVersion);

    public static async Task<Result> InstallAsync(IAuthenticodeTrustVerifier verifier, CancellationToken cancellationToken)
    {
        string msi;
        try
        {
            msi = await FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or InvalidDataException or UnauthorizedAccessException)
        {
            return new Result(Outcome.Failed, "Dokany татагдсангүй: " + exception.Message);
        }
        AuthenticodeTrustResult trust = verifier.Verify(msi);
        if (!trust.IsTrusted || !trust.Publisher.Contains(SyncDokany.MsiPublisher, StringComparison.OrdinalIgnoreCase))
            return new Result(Outcome.Failed, "Dokany-ийн гарын үсэг баталгаажсангүй — суулгасангүй. " + trust.Error);
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msi}\" /qb /norestart")
            {
                UseShellExecute = true,
                Verb = "runas", // Windows asks the person (UAC); a driver is never put in without it
            });
            if (process is null)
                return new Result(Outcome.Failed, "Dokany-ийн суулгагч эхэлсэнгүй.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            // 0 = done; 3010 = done, a restart finishes it; 1602 = the person cancelled.
            if (process.ExitCode == 1602)
                return new Result(Outcome.Declined, "Dokany-ийн суулгалтыг цуцаллаа.");
            if (process.ExitCode is not (0 or 3010))
                return new Result(Outcome.Failed, $"Dokany суусангүй (msiexec {process.ExitCode}).");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new Result(Outcome.Declined, "Dokany-ийн суулгалтыг (UAC) зөвшөөрсөнгүй.");
        }
        SyncTrayProcess.Poke(); // Sync tries the mount now
        return new Result(Outcome.Installed, NeedsInstall
            ? "Dokany суулаа — Windows-ийг дахин эхлүүлсний дараа central холбогдоно."
            : $"Dokany {InstalledVersion} суулаа — central холбогдож байна.");
    }

    /// <summary>The pinned msi in this user's temp, fetched once; a file whose hash differs is never kept.</summary>
    private static async Task<string> FetchAsync(CancellationToken cancellationToken)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ErkS-Dokany");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"Dokan_x64-{SyncDokany.PinnedVersion}.msi");
        if (File.Exists(path) && Sha256Of(path) == SyncDokany.MsiSha256)
            return path;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        byte[] bytes = await http.GetByteArrayAsync(SyncDokany.MsiUrl, cancellationToken).ConfigureAwait(false);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(SyncDokany.MsiSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dokany-ийн файлын хэш таарахгүй байна.");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
