using System.Text.Json;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// What a source's inbox holds that the project has not taken in yet.
/// </summary>
public sealed record PendingSourcePackageSurvey(
    int Count,
    DateTimeOffset? NewestExportedAtUtc)
{
    public static PendingSourcePackageSurvey None { get; } = new(0, null);

    public bool Any => Count > 0;
}

/// <summary>
/// Looks in a source's inbox for deliveries the project has not absorbed.
///
/// A package that arrives while the project is closed sits in the inbox until
/// something rescans, and until then the project simply looks as though the
/// drawing was never sent - which is indistinguishable, from the outside, from
/// having exported the wrong thing. This is how the project can say so.
///
/// It reads manifest headers only: this answers "is there something waiting",
/// not "is it any good". Verification stays where it belongs, in intake.
/// </summary>
public static class SourceInboxScanner
{
    public static PendingSourcePackageSurvey Survey(
        string? inboxFolder,
        string? recordedPackageId,
        DateTimeOffset? recordedExportedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(inboxFolder) || !Directory.Exists(inboxFolder))
            return PendingSourcePackageSurvey.None;

        string recorded = (recordedPackageId ?? "").Trim();
        int count = 0;
        DateTimeOffset? newest = null;
        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(
                inboxFolder,
                "*" + SheetPackageManifest.ManifestSuffix,
                SearchOption.AllDirectories);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return PendingSourcePackageSurvey.None;
        }

        foreach (string path in manifests)
        {
            SheetPackageManifest? manifest = TryReadHeader(path);
            if (manifest is null)
                continue;
            // The delivery the project already holds, and anything exported
            // before it, are not waiting for anyone.
            if (manifest.PackageId.ToString("N").Equals(recorded, StringComparison.OrdinalIgnoreCase))
                continue;
            if (recordedExportedAtUtc is { } recordedAt && manifest.ExportedAtUtc <= recordedAt)
                continue;

            count++;
            if (newest is null || manifest.ExportedAtUtc > newest)
                newest = manifest.ExportedAtUtc;
        }

        return count == 0
            ? PendingSourcePackageSurvey.None
            : new PendingSourcePackageSurvey(count, newest);
    }

    private static SheetPackageManifest? TryReadHeader(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<SheetPackageManifest>(
                File.ReadAllText(path),
                SheetPackageJson.Options);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            // Unreadable input is intake's business to reject and report, not
            // this survey's to guess about.
            return null;
        }
    }
}
