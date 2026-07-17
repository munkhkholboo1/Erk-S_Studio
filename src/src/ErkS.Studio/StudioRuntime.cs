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
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Debug --nologo -v m",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

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
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments =
                $"publish \"{projectPath}\" -c Debug -r win-x64 --self-contained false " +
                $"-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true " +
                $"-p:StudioDevBundle=true " +
                $"-o \"{outputDirectory}\" --nologo -v m",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

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
