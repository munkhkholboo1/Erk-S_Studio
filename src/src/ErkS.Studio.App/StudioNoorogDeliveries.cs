using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>One file of a Noorog package on the server (a vector PDF, or the package's manifest).</summary>
internal sealed record NoorogDeliveryFile(string Name, string Sha256, long Size);

/// <summary>
/// One Noorog package delivered to a project (SRV «…/noorog-deliveries»): the sheets of one board,
/// in Studio's sheet-package contract (schema 5), its manifest named by <see cref="ManifestFile"/>.
/// </summary>
internal sealed record NoorogDelivery(
    string PackageId,
    string BoardId,
    string BoardTitle,
    string SourceId,
    DateTimeOffset ExportedAtUtc,
    string ManifestFile,
    IReadOnlyList<NoorogDeliveryFile>? Files);

internal sealed record NoorogDeliveryListResponse(IReadOnlyList<NoorogDelivery>? Deliveries);

/// <summary>Where one board's packages go on this device, or why they do not come here.</summary>
internal sealed record NoorogSourceSlot(string? InboxFolder, string Skip)
{
    public static NoorogSourceSlot Inbox(string folder) => new(folder, "");

    public static NoorogSourceSlot Elsewhere(string reason) => new(null, reason);
}

internal sealed record NoorogFetchReport(
    int Listed,
    int Fetched,
    int AlreadyHere,
    int Elsewhere,
    IReadOnlyList<string> Failures)
{
    public static readonly NoorogFetchReport Nothing = new(0, 0, 0, 0, []);
}

/// <summary>
/// Brings a project's Noorog packages from the server into the project's source inbox
/// (Ноорог агент, 2026-11-19: the owner approved «Ноорог → Studio холболт»).
///
/// Each board is one source, «Ноорог — &lt;board&gt;», whose id is the package's own sourceId
/// (stable per board) - so the ordinary intake admits it with no Noorog-specific rule. A package
/// lands in <c>&lt;inbox&gt;\&lt;yyyyMMdd-HHmmss&gt;-&lt;package 8&gt;\</c>: the PDFs first, the manifest last
/// (the intake watches manifests), every file through a temporary name and checked against its
/// hash and size. A package already brought (the ledger beside the inbox, or its manifest on disk)
/// is never fetched again. Read when a project opens, on «Эх үүсвэрээс шинэчлэх», and when the
/// server says a package arrived - never on a clock.
/// </summary>
internal static class NoorogDeliveryIntake
{
    public const string SourceNamePrefix = "Ноорог — ";

    public const string BoardIdMetadataKey = "NoorogBoardId";

    public const string LedgerFileName = "noorog-received.txt";

    /// <summary>One file at most; the PDFs are vector sheets, far below this.</summary>
    public const long MaxFileBytes = 256L * 1024 * 1024;

    private const string ManifestSuffix = ".erks-sheets.json";

    private const string PartSuffix = ".download";

    public static string ListPath(string projectId) =>
        "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/noorog-deliveries";

    public static string FilePath(string projectId, string packageId, string name) =>
        ListPath(projectId) + "/" + Uri.EscapeDataString(packageId) + "/files/" + Uri.EscapeDataString(name);

    public static string SourceNameOf(NoorogDelivery delivery)
    {
        string title = (delivery.BoardTitle ?? "").Trim();
        return SourceNamePrefix + (title.Length > 0 ? title : "самбар");
    }

    /// <summary>The folder name of one package inside the source's inbox.</summary>
    public static string FolderNameOf(NoorogDelivery delivery)
    {
        string id = new string((delivery.PackageId ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());
        return delivery.ExportedAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
            "-" + (id.Length > 8 ? id[..8] : id).ToLowerInvariant();
    }

    /// <summary>What is wrong with a listed package (it is then left on the server), or null.</summary>
    public static string? Problem(NoorogDelivery delivery)
    {
        if (delivery is null)
            return "хоосон бичлэг";
        string packageId = (delivery.PackageId ?? "").Trim();
        if (packageId.Length == 0 || packageId.Length > 128 || !packageId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return "багцын дугаар буруу";
        if (!IsSourceId(delivery.SourceId))
            return "эх үүсвэрийн дугаар 32 hex биш";
        IReadOnlyList<NoorogDeliveryFile> files = delivery.Files ?? [];
        if (files.Count == 0)
            return "файлгүй";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NoorogDeliveryFile file in files)
        {
            if (file is null || !IsPlainName(file.Name))
                return "файлын нэр буруу";
            if (!names.Add(file.Name))
                return "давхардсан файл: " + file.Name;
            bool manifest = file.Name.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase);
            if (!manifest && !file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return "PDF, manifest-аас өөр файл: " + file.Name;
            if (file.Size < 0 || file.Size > MaxFileBytes)
                return "файлын хэмжээ буруу: " + file.Name;
            if ((file.Sha256 ?? "").Length != 64 || !file.Sha256!.All(Uri.IsHexDigit))
                return "файлын hash буруу: " + file.Name;
        }
        string manifestFile = delivery.ManifestFile ?? "";
        if (!manifestFile.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase) || !names.Contains(manifestFile))
            return "manifest файл жагсаалтад алга";
        return null;
    }

    public static bool IsSourceId(string? value) =>
        value is { Length: 32 } && value.All(Uri.IsHexDigit);

    private static bool IsPlainName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 200 &&
        name.Trim() == name &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        name is not "." and not ".." &&
        !name.EndsWith('.');

    /// <summary>
    /// Fetches every listed package this device has not brought yet. <paramref name="slotFor"/> says
    /// where a board's packages go here (registering its source when the project has none), or that
    /// they go to another device. Nothing already on disk is touched; a failed package leaves only
    /// its own temporary files behind, which the next attempt replaces.
    /// </summary>
    public static async Task<NoorogFetchReport> FetchAsync(
        INoorogDeliveryClient client,
        string projectId,
        Func<NoorogDelivery, NoorogSourceSlot> slotFor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(slotFor);
        IReadOnlyList<NoorogDelivery> listed = await client.ListNoorogDeliveriesAsync(projectId, cancellationToken).ConfigureAwait(true);
        int fetched = 0, here = 0, elsewhere = 0;
        var failures = new List<string>();
        foreach (NoorogDelivery delivery in listed.OrderBy(item => item?.ExportedAtUtc ?? DateTimeOffset.MinValue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? problem = Problem(delivery);
            if (problem is not null)
            {
                failures.Add($"«{delivery?.BoardTitle}» багц: {problem}");
                continue;
            }

            NoorogSourceSlot slot = slotFor(delivery);
            if (slot.InboxFolder is not { Length: > 0 } inbox)
            {
                elsewhere++;
                continue;
            }

            string target = Path.Combine(inbox, FolderNameOf(delivery));
            if (AlreadyHere(inbox, target, delivery))
            {
                here++;
                continue;
            }

            try
            {
                await BringAsync(client, projectId, delivery, target, cancellationToken).ConfigureAwait(true);
                Remember(inbox, delivery.PackageId);
                fetched++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add($"«{delivery.BoardTitle}» багц татагдсангүй: {exception.Message}");
            }
        }
        return new NoorogFetchReport(listed.Count, fetched, here, elsewhere, failures);
    }

    private static string LedgerPathOf(string inbox) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inbox).TrimEnd(Path.DirectorySeparatorChar)) ?? inbox, LedgerFileName);

    internal static bool AlreadyHere(string inbox, string target, NoorogDelivery delivery)
    {
        if (File.Exists(Path.Combine(target, delivery.ManifestFile)))
            return true;
        string ledger = LedgerPathOf(inbox);
        return File.Exists(ledger) &&
            File.ReadLines(ledger).Any(line => line.Trim().Equals(delivery.PackageId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static void Remember(string inbox, string packageId)
    {
        string ledger = LedgerPathOf(inbox);
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        File.AppendAllLines(ledger, [packageId.Trim()]);
    }

    private static async Task BringAsync(
        INoorogDeliveryClient client,
        string projectId,
        NoorogDelivery delivery,
        string target,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        // The PDFs first; the manifest - the only file the intake watches - last.
        IEnumerable<NoorogDeliveryFile> ordered = delivery.Files!
            .OrderBy(file => file.Name.Equals(delivery.ManifestFile, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        try
        {
            foreach (NoorogDeliveryFile file in ordered)
            {
                string final = Path.Combine(target, file.Name);
                string part = final + PartSuffix;
                await using (var stream = new FileStream(part, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    await client.CopyNoorogDeliveryFileAsync(projectId, delivery.PackageId, file.Name, stream, MaxFileBytes, cancellationToken).ConfigureAwait(true);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(true);
                    if (stream.Length != file.Size)
                        throw new InvalidDataException($"{file.Name}: хэмжээ зөрлөө ({stream.Length} ≠ {file.Size}).");
                    stream.Position = 0;
                    string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(true));
                    if (!hash.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{file.Name}: hash зөрлөө.");
                }
                written.Add(part);
                File.Move(part, final, overwrite: true);
                written[^1] = final;
            }
        }
        catch
        {
            // Only what this attempt wrote (the manifest, written last, never got this far).
            foreach (string leftover in written.Concat(Directory.EnumerateFiles(target, "*" + PartSuffix)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(leftover);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }
            throw;
        }
    }

    /// <summary>The project's row for a board's source, made the way «Эх үүсвэр нэмэх» makes a folder source.</summary>
    public static ProjectDesignSource NewSource(NoorogDelivery delivery) => new()
    {
        Id = delivery.SourceId.Trim().ToLowerInvariant(),
        Kind = DesignSourceKind.Folder,
        Name = SourceNameOf(delivery),
        ApplicationVersion = "Erk-S Ноорог",
        NativeDocumentTitle = (delivery.BoardTitle ?? "").Trim(),
        Status = DesignSourceStatuses.Connected,
        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [BoardIdMetadataKey] = (delivery.BoardId ?? "").Trim(),
        },
    };
}
