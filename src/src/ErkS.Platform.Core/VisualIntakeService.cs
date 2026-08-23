namespace ErkS.Platform.Core;

public sealed record VisualPackageArrival(
    string ManifestPath,
    string PackageFolder,
    string SourceId,
    VisualPackageLoadResult Result);

public sealed record RefusedVisualPackage(
    string ManifestPath,
    DateTimeOffset SeenAtUtc,
    IReadOnlyList<string> Issues);

/// <summary>
/// Watches source folders for visual packages, beside the intake that watches
/// them for sheet packages.
///
/// It is deliberately a second watcher rather than a wider filter on the first.
/// The two channels answer to different contracts - one refuses a page with no
/// vector content, the other exists precisely to carry raster - and the sheet
/// intake is the path a user's day runs through. Widening it to carry a second
/// set of rules would put that path at risk for no gain; the duplication here
/// is small, obvious, and cannot reach the other channel.
/// </summary>
public sealed class VisualIntakeService : IDisposable
{
    /// <summary>Watcher events arrive in bursts; one file is handled once.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    /// <summary>A manifest is written last, but the write may still be flushing.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    private readonly object sync = new();
    private readonly Dictionary<string, Registration> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RefusedVisualPackage> refused = [];

    /// <summary>Raised after a manifest is read, whether it was accepted or not.</summary>
    public event Action<VisualPackageArrival>? PackageProcessed;

    public event Action<string>? IntakeError;

    /// <summary>
    /// Packages that were seen and could not be used. Kept so a refusal can be
    /// shown rather than leaving the user to wonder why nothing happened.
    /// </summary>
    public IReadOnlyList<RefusedVisualPackage> RefusedPackages
    {
        get
        {
            lock (sync)
            {
                return refused.ToList();
            }
        }
    }

    public IReadOnlyList<string> WatchedFolders
    {
        get
        {
            lock (sync)
            {
                return watchers.Keys.ToList();
            }
        }
    }

    public void WatchFolder(
        string folder,
        string? sourceId = null,
        bool scanExisting = true)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folder);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            IntakeError?.Invoke(exception.Message);
            return;
        }

        Registration registration;
        lock (sync)
        {
            if (watchers.ContainsKey(fullPath))
                return;

            try
            {
                Directory.CreateDirectory(fullPath);
                var watcher = new FileSystemWatcher(fullPath)
                {
                    Filter = "*" + VisualPackageContract.ManifestSuffix,
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                };
                registration = new Registration(watcher, (sourceId ?? "").Trim());
                watcher.Created += (_, args) => Handle(args.FullPath, registration);
                watcher.Changed += (_, args) => Handle(args.FullPath, registration);
                watcher.Renamed += (_, args) => Handle(args.FullPath, registration);
                watcher.Error += (_, args) =>
                    IntakeError?.Invoke($"Ажиглагчийн алдаа: {args.GetException().Message}");
                watcher.EnableRaisingEvents = true;
                watchers[fullPath] = registration;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                IntakeError?.Invoke(exception.Message);
                return;
            }
        }

        if (scanExisting)
            ScanFolder(fullPath, registration);
    }

    public void UnwatchFolder(string folder)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folder);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        Registration? registration;
        lock (sync)
        {
            watchers.Remove(fullPath, out registration);
        }
        registration?.Stop();
    }

    /// <summary>
    /// Reads one manifest and reports it. Public so a caller can absorb a
    /// package it already knows about without waiting on a filesystem event.
    /// </summary>
    public VisualPackageArrival Process(string manifestPath, string? sourceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string fullPath = Path.GetFullPath(manifestPath);
        VisualPackageLoadResult result = VisualPackageReader.Load(fullPath);
        var arrival = new VisualPackageArrival(
            fullPath,
            Path.GetDirectoryName(fullPath) ?? "",
            (sourceId ?? "").Trim(),
            result);

        if (!result.IsLoaded)
        {
            lock (sync)
            {
                refused.RemoveAll(item =>
                    string.Equals(item.ManifestPath, fullPath, StringComparison.OrdinalIgnoreCase));
                refused.Add(new RefusedVisualPackage(fullPath, DateTimeOffset.UtcNow, result.Issues));
            }
        }

        PackageProcessed?.Invoke(arrival);
        return arrival;
    }

    private void ScanFolder(string folder, Registration registration)
    {
        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(
                folder,
                "*" + VisualPackageContract.ManifestSuffix,
                SearchOption.AllDirectories);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            IntakeError?.Invoke(exception.Message);
            return;
        }

        foreach (string path in manifests.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (registration.IsStopped)
                return;
            Process(path, registration.SourceId);
        }
    }

    private void Handle(string manifestPath, Registration registration)
    {
        lock (sync)
        {
            if (registration.IsStopped)
                return;

            // Watchers fire several times for one write. The same file inside
            // the debounce window is one arrival, not three.
            DateTime now = DateTime.UtcNow;
            if (recent.TryGetValue(manifestPath, out DateTime last) && now - last < Debounce)
                return;
            recent[manifestPath] = now;
        }

        _ = Task.Run(async () =>
        {
            // The manifest is written last, but the write may still be in
            // flight. A moment's wait costs nothing and avoids reading half a
            // file and calling the package broken.
            await Task.Delay(SettleDelay).ConfigureAwait(false);
            try
            {
                if (!registration.IsStopped && File.Exists(manifestPath))
                    Process(manifestPath, registration.SourceId);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                IntakeError?.Invoke(exception.Message);
            }
        });
    }

    public void Dispose()
    {
        List<Registration> registrations;
        lock (sync)
        {
            registrations = watchers.Values.ToList();
            watchers.Clear();
        }
        foreach (Registration registration in registrations)
            registration.Stop();
    }

    private sealed class Registration(FileSystemWatcher watcher, string sourceId)
    {
        public string SourceId { get; } = sourceId;

        public bool IsStopped { get; private set; }

        public void Stop()
        {
            IsStopped = true;
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception exception) when (
                exception is ObjectDisposedException or IOException)
            {
                // Already gone; nothing to tidy.
            }
        }
    }
}

public sealed record PendingVisualPackageSurvey(int Count, DateTimeOffset? NewestExportedAtUtc)
{
    public static PendingVisualPackageSurvey None { get; } = new(0, null);

    public bool HasPending => Count > 0;
}

/// <summary>
/// Looks for visual packages in a source's folder that the project has not
/// taken in yet.
///
/// The sheet channel already tells a user when a delivery has arrived and not
/// been absorbed, and they will expect the same of this one. A package that
/// landed and did nothing, with no error and no notice, is the failure this
/// codebase keeps finding.
/// </summary>
public static class VisualInboxScanner
{
    public static PendingVisualPackageSurvey Survey(
        string? inboxFolder,
        DateTimeOffset? absorbedUpToUtc)
    {
        if (string.IsNullOrWhiteSpace(inboxFolder) || !Directory.Exists(inboxFolder))
            return PendingVisualPackageSurvey.None;

        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(
                inboxFolder,
                "*" + VisualPackageContract.ManifestSuffix,
                SearchOption.AllDirectories);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return PendingVisualPackageSurvey.None;
        }

        int count = 0;
        DateTimeOffset? newest = null;
        foreach (string path in manifests)
        {
            // Only the header is needed, and an unreadable one is intake's
            // business to refuse and report rather than this survey's to guess
            // about.
            VisualPackageLoadResult result = VisualPackageReader.Load(path);
            if (result.Manifest is not { } manifest)
                continue;
            if (absorbedUpToUtc is { } absorbed && manifest.ExportedAtUtc <= absorbed)
                continue;

            count++;
            if (newest is null || manifest.ExportedAtUtc > newest)
                newest = manifest.ExportedAtUtc;
        }

        return count == 0 ? PendingVisualPackageSurvey.None : new PendingVisualPackageSurvey(count, newest);
    }

    /// <summary>
    /// How far this project has already taken a source's visuals in. Read from
    /// the material itself, so no second record has to be kept in step with it.
    /// </summary>
    public static DateTimeOffset? AbsorbedUpTo(ProjectPortfolio? portfolio, string sourceId)
    {
        if (portfolio is null)
            return null;

        string prefix = VisualPackageImportService.MakeKey(sourceId, "");
        DateTimeOffset? newest = null;
        foreach (ProjectPortfolioItem item in portfolio.Items)
        {
            if (!item.Kind.Equals(ProjectPortfolioItemKinds.Visual, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!item.SourceSheetKey.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (item.SourceExportedAtUtc is { } exported && (newest is null || exported > newest))
                newest = exported;
        }
        return newest;
    }
}
