using System.Diagnostics;
using DokanNet;
using DokanNet.Logging;
using ErkS.Sync.Core;

namespace ErkS.Sync;

/// <summary>
/// The member half of Erk-S Central in the tray: each central another machine holds is mounted
/// (Dokany) and shown at its SAME path in this machine's sync folder - the model as a file link,
/// its _backup as a junction - so a local made here records the same central path as everywhere
/// (owner, 2026-10-23: «ижил замтай»). A real file already at that path is never touched.
/// </summary>
internal sealed class SyncCentralMounts(SyncHome home, Func<(Uri Server, string Token)?> road) : IDisposable
{
    private readonly Dictionary<string, Mounted> _mounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Dokan? _dokan;
    private readonly Dictionary<string, DateTime> _probedUtc = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _dokanTriedUtc = DateTime.MinValue;

    /// <summary>Why a central is not mounted, per central id - shown in Studio («Dokany суугаагүй» and the like).</summary>
    public IReadOnlyDictionary<string, string> Problems => _problems;

    private readonly Dictionary<string, string> _problems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Called after each state write: mounts the member centrals listed, unmounts those gone.</summary>
    public void Follow(SyncState state)
    {
        var wanted = new Dictionary<string, (SyncFolderState Folder, SyncCentralState Central)>(StringComparer.OrdinalIgnoreCase);
        foreach (SyncFolderState folder in state.Folders.Where(folder => folder.PresentHere))
            foreach (SyncCentralState central in folder.Centrals.Where(central => central.Role == "member"))
                wanted[folder.FolderId + "|" + central.CentralId] = (folder, central);

        lock (_gate)
        {
            foreach ((string key, (SyncFolderState folder, SyncCentralState central)) in wanted)
            {
                if (!_mounts.ContainsKey(key))
                    TryMount(key, folder, central);
            }
            foreach (string key in _mounts.Keys.Where(key => !wanted.ContainsKey(key)).ToArray())
            {
                _mounts[key].Dispose();
                _mounts.Remove(key);
            }
            PublishProblems();
        }
    }

    /// <summary>Dokany may have just been installed: the next state tries it at once.</summary>
    public void RetryDokany()
    {
        if (_dokan is null)
            _dokanTriedUtc = DateTime.MinValue;
    }

    private void TryMount(string key, SyncFolderState folder, SyncCentralState central)
    {
        if (_dokan is null)
        {
            // Not asked every state write: once in five minutes until Dokany is installed.
            if (DateTime.UtcNow - _dokanTriedUtc < TimeSpan.FromMinutes(5))
                return;
            _dokanTriedUtc = DateTime.UtcNow;
            // Dokany's own dll first: a Dokan made without it dies in its finalizer (DokanShutdown ->
            // DllNotFoundException on the GC thread) and takes the whole tray down - the owner's
            // desktop, 2026-10-26 03:31, before Dokany was installed.
            Dokan? made = null;
            try
            {
                if (!System.Runtime.InteropServices.NativeLibrary.TryLoad("dokan2", out IntPtr handle))
                    throw new DllNotFoundException("dokan2");
                System.Runtime.InteropServices.NativeLibrary.Free(handle);
                made = new Dokan(new NullLogger());
                _ = made.DriverVersion;
                _dokan = made;
            }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or DokanException or EntryPointNotFoundException)
            {
                if (made is not null)
                    GC.SuppressFinalize(made);
                _dokan = null;
                _problems[central.CentralId] = "Dokany суугаагүй байна — central-ыг энэ машинаас нээхэд нэг удаа суулгана (Studio санал болгоно).";
                return;
            }
        }

        string physical = new SyncPathMap().Physical(folder.CanonicalPath);
        string target = Path.GetFullPath(Path.Combine(physical, central.RelativePath));
        string stem = Path.GetFileNameWithoutExtension(target);
        string backup = Path.Combine(Path.GetDirectoryName(target)!, stem + "_backup");
        string mountPoint = Path.Combine(home.Root, "central", central.CentralId, "mount");
        if ((File.Exists(target) && !IsLink(target)) || (Directory.Exists(backup) && !IsLink(backup)))
        {
            _problems[central.CentralId] = "Энэ машинд central-ын замд жинхэнэ файл байна — хоёр central болохоос сэргийлж холбосонгүй.";
            return;
        }

        var channel = new SyncCentralRelayChannel(async token =>
        {
            (Uri Server, string Token) current = road() ?? throw new IOException("no road");
            return await SyncCentralWebSocketLink.ConnectAsync(current.Server, current.Token, folder.FolderId, central.CentralId, "member", token);
        });

        // Nothing is mounted - no link, no Windows question - until the host shares the central
        // and is online; asked again once a minute.
        if (_probedUtc.TryGetValue(key, out DateTime probed) && DateTime.UtcNow - probed < TimeSpan.FromMinutes(1))
        {
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }
        _probedUtc[key] = DateTime.UtcNow;
        SyncCentralResponse reach = new SyncCentralClient(channel).CallAsync(new SyncCentralRequest { Op = SyncCentralOp.List, Path = "" }).GetAwaiter().GetResult();
        if (reach.Error == SyncCentralErrors.HostUnreachable)
        {
            _problems[central.CentralId] = channel.LastRefusal == "sync_central_host_offline" || channel.LastRefusal.Length == 0
                ? "Central-ын эзэн машин унтарсан, хамтын ажиллагааг унтраасан, эсвэл түүний Studio хуучин (0.001.134-өөс өмнөх) байна — эзэн машины Studio-г шинэчилнэ үү."
                : "Сервер татгалзлаа: " + channel.LastRefusal;
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }
        Directory.CreateDirectory(mountPoint);
        DokanInstance instance;
        try
        {
            instance = new DokanInstanceBuilder(_dokan)
                .ConfigureOptions(options =>
                {
                    options.MountPoint = mountPoint;
                    options.Options = DokanOptions.FixedDrive;
                    options.TimeOut = TimeSpan.FromSeconds(30);
                })
                .Build(new SyncCentralMount(new SyncCentralClient(channel)));
        }
        catch (DokanException exception)
        {
            _problems[central.CentralId] = "Central-ыг холбож чадсангүй: " + exception.Message;
            return;
        }

        string? linkError = Link(target, Path.Combine(mountPoint, Path.GetFileName(target)), backup, Path.Combine(mountPoint, stem + "_backup"));
        _problems.Remove(central.CentralId);
        if (linkError is not null)
            _problems[central.CentralId] = linkError;
        _mounts[key] = new Mounted(instance, channel, target, backup);
    }

    /// <summary>
    /// The model as a file symbolic link (Windows Developer Mode, or an administrator, allows it),
    /// the _backup as a junction (no privilege). Revit then opens the central at its usual path.
    /// </summary>
    private static string? Link(string file, string fileTarget, string folder, string folderTarget)
    {
        try
        {
            if (!File.Exists(file))
                File.CreateSymbolicLink(file, fileTarget);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No Developer Mode is asked for (a system security setting - Master, 2026-10-25):
            // the one link is made once with the person's own consent (Windows asks, UAC).
            try
            {
                using Process? elevated = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink \"{file}\" \"{fileTarget}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                elevated?.WaitForExit(60_000);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The person said no: nothing is changed.
            }
            if (!File.Exists(file))
                return "Central-ын файлын холбоосыг үүсгэх зөвшөөрөл өгөөгүй — Windows-ийн асуултад «Тийм» гэвэл central энэ замаар нээгдэнэ.";
        }
        if (!Directory.Exists(folder))
        {
            using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{folder}\" \"{folderTarget}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(10_000);
            if (!Directory.Exists(folder))
                return "Central-ын _backup хавтсыг холбож чадсангүй.";
        }
        return null;
    }

    /// <summary>
    /// PFR's begin/end around Revit's synchronize of the central at <paramref name="centralPath"/>.
    /// On the host's own machine Revit works on the real files - nothing to hold («host»).
    /// </summary>
    public async Task<SyncPipeResponse> TransactionAsync(string centralPath, bool begin, CancellationToken cancellationToken)
    {
        Mounted? mounted;
        lock (_gate)
            mounted = _mounts.Values.FirstOrDefault(item => string.Equals(Path.GetFullPath(item.File), Path.GetFullPath(centralPath ?? ""), StringComparison.OrdinalIgnoreCase));
        if (mounted is null)
            return new SyncPipeResponse(true, "host");
        SyncCentralResponse response = await new SyncCentralClient(mounted.Channel)
            .CallAsync(new SyncCentralRequest { Op = begin ? SyncCentralOp.TxBegin : SyncCentralOp.TxEnd }, cancellationToken);
        return new SyncPipeResponse(response.Ok, response.Ok ? "member" : response.Error);
    }

    /// <summary>The problems, written where Studio reads them (central/problems.json) - only when they change.</summary>
    private void PublishProblems()
    {
        string path = Path.Combine(home.Root, "central", "problems.json");
        string json = System.Text.Json.JsonSerializer.Serialize(_problems);
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == json)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (Mounted mounted in _mounts.Values)
                mounted.Dispose();
            _mounts.Clear();
        }
        _dokan?.Dispose();
    }

    private sealed class Mounted(DokanInstance instance, SyncCentralRelayChannel channel, string file, string folder) : IDisposable
    {
        public string File { get; } = file;

        public SyncCentralRelayChannel Channel { get; } = channel;

        public void Dispose()
        {
            instance.Dispose();
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            // Only our own links go - a real file put there meanwhile stays.
            try
            {
                if (System.IO.File.Exists(file) && IsLink(file))
                    System.IO.File.Delete(file);
                if (Directory.Exists(folder) && IsLink(folder))
                    Directory.Delete(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
