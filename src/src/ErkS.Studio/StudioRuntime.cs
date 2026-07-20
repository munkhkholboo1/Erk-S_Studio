using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using ErkS.Studio.Contracts;

namespace ErkS.Studio;

/// <summary>One loaded app-module version and everything needed to retire it.</summary>
internal sealed class LoadedStudioModule
{
    public required IStudioAppModule Module { get; init; }
    public StudioLoadContext? Context { get; init; }
    public required string ShadowDirectory { get; init; }
    public required DateTime LoadedAt { get; init; }
    public bool IsStaticFallback { get; init; }
}

/// <summary>
/// DevUpdate engine of the host: finds the app-module binaries, loads each
/// version from a shadow copy (so rebuilds never fight file locks), rebuilds
/// from source in dev mode, and retires old versions.
///
/// Dev mode is active when a "*.devroot" marker (written by the host build)
/// points at the product root; the app module is then loaded from
/// builds/devmod/app, which every ErkS.Studio.App build refreshes.
/// </summary>
internal sealed class StudioRuntime
{
    private const string AppAssemblyFileName = "ErkS.Studio.App.dll";
    private const string ModuleTypeName = "ErkS.Studio.StudioAppModule";
    private const string StaticModeMarkerFileName = "ErkS.Studio.static";
    private const int RequiredDotnetSdkMajor = 9;
    private static readonly string[] SharedAssemblyNames =
    [
        "ErkS.Studio.Contracts",
    ];

    public string? DevRoot { get; }

    public bool IsDevMode => DevRoot is not null;

    /// <summary>
    /// True for a bundled development host that must not probe unsigned
    /// shadow DLLs under Windows Application Control.
    /// </summary>
    public bool PreferStaticModule { get; }

    /// <summary>Folder the app module is loaded from (dev or release layout).</summary>
    public string AppSourceDirectory { get; }

    public StudioRuntime()
    {
        EnsureSharedAssembliesLoaded();
        DevRoot = TryReadDevRoot();
        PreferStaticModule = DevRoot is not null && File.Exists(
            Path.Combine(AppContext.BaseDirectory, StaticModeMarkerFileName));
        AppSourceDirectory = DevRoot is not null
            ? Path.Combine(DevRoot, "builds", "devmod", "app")
            : Path.Combine(AppContext.BaseDirectory, "app");
        CleanStaleShadowCopies();
    }

    private static void EnsureSharedAssembliesLoaded()
    {
        foreach (var assemblyName in SharedAssemblyNames)
        {
            if (AssemblyLoadContext.Default.Assemblies.Any(assembly => string.Equals(
                    assembly.GetName().Name,
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName));
                continue;
            }
            catch
            {
                // Fall through to a development output next to the host.
            }

            var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            }
            catch
            {
                // The normal load path will report a useful error if this is required.
            }
        }
    }

    private static string? TryReadDevRoot()
    {
        try
        {
            var marker = Path.Combine(AppContext.BaseDirectory, "ErkS.Studio.devroot");
            if (!File.Exists(marker))
            {
                return null;
            }

            var devRoot = File.ReadAllText(marker).Trim();
            return Directory.Exists(devRoot) ? devRoot : null;
        }
        catch
        {
            return null;
        }
    }

    private string ShadowCacheRoot => DevRoot is not null
        ? Path.Combine(DevRoot, "builds", "devmod", "app-cache")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Erk-S Studio",
            "app-cache");

    public LoadedStudioModule LoadModule()
    {
        var sourceDll = Path.Combine(AppSourceDirectory, AppAssemblyFileName);
        if (!File.Exists(sourceDll))
        {
            throw new FileNotFoundException(
                $"App module not found: {sourceDll}. Build ErkS.Studio.App first.");
        }

        var shadowDirectory = Path.Combine(ShadowCacheRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        CopyDirectory(AppSourceDirectory, shadowDirectory);

        var shadowDll = Path.Combine(shadowDirectory, AppAssemblyFileName);
        var context = new StudioLoadContext(shadowDll);
        var assembly = context.LoadFromAssemblyPath(shadowDll);
        var moduleType = assembly.GetType(ModuleTypeName)
            ?? throw new InvalidOperationException($"Module type missing: {ModuleTypeName}");
        var module = Activator.CreateInstance(moduleType) as IStudioAppModule
            ?? throw new InvalidOperationException($"{ModuleTypeName} does not implement IStudioAppModule.");

        return new LoadedStudioModule
        {
            Module = module,
            Context = context,
            ShadowDirectory = shadowDirectory,
            LoadedAt = DateTime.Now,
        };
    }

    public LoadedStudioModule LoadStaticModule()
    {
        return new LoadedStudioModule
        {
            Module = new StudioAppModule(),
            Context = null,
            ShadowDirectory = "",
            LoadedAt = DateTime.Now,
            IsStaticFallback = true,
        };
    }

    public static void Retire(LoadedStudioModule loaded)
    {
        try
        {
            loaded.Module.Shutdown();
        }
        catch
        {
        }

        try
        {
            loaded.Context?.Unload();
        }
        catch
        {
        }
        // Shadow files may stay pinned until the GC collects the old context;
        // stale copies are removed on the next start instead.
    }

    /// <summary>Rebuilds the app module from source (dev mode only).</summary>
    public async Task<(bool Success, string Output)> DevBuildAsync()
    {
        if (DevRoot is null)
        {
            return (false, "Dev root not found - DevUpdate needs a source checkout.");
        }

        var projectPath = Path.Combine(
            DevRoot, "src", "src", "ErkS.Studio.App", "ErkS.Studio.App.csproj");
        var dotnetPath = ResolveDotnetCli();
        if (dotnetPath is null)
        {
            return (false, MissingDotnetSdkMessage());
        }

        var startInfo = CreateDotnetStartInfo(
            dotnetPath,
            $"build \"{projectPath}\" -c Debug --nologo -v m");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet build could not be started.");
        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        var output = (stdout + Environment.NewLine + stderr).Trim();
        return (process.ExitCode == 0, output);
    }

    /// <summary>
    /// Builds a bundled host for machines whose Application Control policy
    /// does not allow freshly built managed DLLs to be loaded from disk.
    /// </summary>
    public async Task<(bool Success, string Output, string? ExecutablePath)> DevBuildSingleFileHostAsync()
    {
        if (DevRoot is null)
        {
            return (false, "Dev root not found - DevUpdate needs a source checkout.", null);
        }

        var outputDirectory = Path.Combine(
            DevRoot,
            "builds",
            "devmod",
            "host-run",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(outputDirectory);
        var projectPath = Path.Combine(
            DevRoot, "src", "src", "ErkS.Studio", "ErkS.Studio.csproj");
        var dotnetPath = ResolveDotnetCli();
        if (dotnetPath is null)
        {
            return (false, MissingDotnetSdkMessage(), null);
        }

        var startInfo = CreateDotnetStartInfo(
            dotnetPath,
            $"publish \"{projectPath}\" -c Debug -r win-x64 --self-contained false " +
            $"-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true " +
            $"-p:StudioDevBundle=true " +
            $"-o \"{outputDirectory}\" --nologo -v m");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet publish could not be started.");
        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        var output = (stdout + Environment.NewLine + stderr).Trim();
        var executablePath = Path.Combine(outputDirectory, "ErkS.Studio.exe");
        return process.ExitCode == 0 && File.Exists(executablePath)
            ? (true, output, executablePath)
            : (false, output, null);
    }

    private static ProcessStartInfo CreateDotnetStartInfo(string dotnetPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        ConfigureDotnetEnvironment(startInfo, dotnetPath);
        return startInfo;
    }

    private static void ConfigureDotnetEnvironment(ProcessStartInfo startInfo, string dotnetPath)
    {
        if (!Path.IsPathFullyQualified(dotnetPath))
        {
            return;
        }

        var dotnetRoot = Path.GetDirectoryName(dotnetPath);
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            return;
        }

        startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
        var path = startInfo.Environment.TryGetValue("PATH", out var currentPath)
            ? currentPath
            : Environment.GetEnvironmentVariable("PATH") ?? "";
        startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(path)
            ? dotnetRoot
            : dotnetRoot + Path.PathSeparator + path;
    }

    private static string? ResolveDotnetCli()
    {
        var candidates = GetDotnetCliCandidates()
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (HasRequiredSdk(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetDotnetCliCandidates()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            yield return hostPath;
        }

        foreach (var rootVariable in new[] { "DOTNET_ROOT", "DOTNET_ROOT(x86)" })
        {
            var root = Environment.GetEnvironmentVariable(rootVariable);
            if (!string.IsNullOrWhiteSpace(root))
            {
                yield return Path.Combine(root, DotnetExecutableName());
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, ".dotnet", DotnetExecutableName());
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "dotnet", DotnetExecutableName());
        }

        yield return "dotnet";
    }

    private static bool HasRequiredSdk(string dotnetPath)
    {
        return Path.IsPathFullyQualified(dotnetPath)
            ? HasRequiredSdkInInstallDirectory(dotnetPath)
            : HasRequiredSdkFromProcess(dotnetPath);
    }

    private static bool HasRequiredSdkInInstallDirectory(string dotnetPath)
    {
        if (!File.Exists(dotnetPath))
        {
            return false;
        }

        var installDirectory = Path.GetDirectoryName(dotnetPath);
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return false;
        }

        var sdkDirectory = Path.Combine(installDirectory, "sdk");
        return Directory.Exists(sdkDirectory) &&
            Directory.EnumerateDirectories(sdkDirectory)
                .Select(path => Path.GetFileName(path))
                .Any(IsRequiredSdkVersion);
    }

    private static bool HasRequiredSdkFromProcess(string dotnetPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = "--list-sdks",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return false;
            }

            return process.ExitCode == 0 &&
                output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(IsRequiredSdkVersion);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRequiredSdkVersion(string value)
    {
        var versionText = value.Split(new[] { ' ', '[' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Version.TryParse(versionText, out var version) &&
            version.Major >= RequiredDotnetSdkMajor;
    }

    private static string MissingDotnetSdkMessage() =>
        $".NET SDK {RequiredDotnetSdkMajor}.x was not found. Install .NET {RequiredDotnetSdkMajor} SDK or set DOTNET_ROOT to an installation that contains it.";

    private static string DotnetExecutableName() =>
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private void CleanStaleShadowCopies()
    {
        try
        {
            if (!Directory.Exists(ShadowCacheRoot))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(ShadowCacheRoot))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-2))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch
                {
                    // Still locked by a previous session - retry next start.
                }
            }
        }
        catch
        {
        }
    }
}
