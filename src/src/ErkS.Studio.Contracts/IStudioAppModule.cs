using System.Windows;

namespace ErkS.Studio.Contracts;

/// <summary>
/// Contract between the thin Studio host process and the hot-reloadable app
/// module. The host process (window, dispatcher) stays alive; DevUpdate loads
/// a fresh app module build into a new AssemblyLoadContext and swaps the view
/// without restarting the program.
///
/// This assembly is shared across load contexts, so it must stay small and
/// change rarely - breaking it requires a host restart.
/// </summary>
public interface IStudioAppModule
{
    /// <summary>Human-readable module version shown in the dev bar.</summary>
    string Version { get; }

    /// <summary>Builds the root view hosted inside the shell window.</summary>
    UIElement CreateRootView();

    /// <summary>
    /// Stops timers, watchers, and background work so the old module can be
    /// unloaded after a DevUpdate.
    /// </summary>
    void Shutdown();
}
