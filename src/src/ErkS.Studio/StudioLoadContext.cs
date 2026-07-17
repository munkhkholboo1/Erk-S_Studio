using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ErkS.Studio;

/// <summary>
/// Collectible load context for one complete app-module version. Only the
/// stable host contract stays in the default context; the UI and its domain,
/// PDF, and publishing dependencies are replaced together during DevUpdate.
/// </summary>
internal sealed class StudioLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ErkS.Studio.Contracts",
    };

    private readonly AssemblyDependencyResolver resolver;

    public StudioLoadContext(string mainAssemblyPath)
        : base(name: $"studio-app-{Path.GetFileName(Path.GetDirectoryName(mainAssemblyPath))}", isCollectible: true)
    {
        resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && SharedAssemblyNames.Contains(assemblyName.Name))
        {
            return Default.Assemblies.FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name,
                assemblyName.Name,
                StringComparison.OrdinalIgnoreCase));
        }

        var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }
}
