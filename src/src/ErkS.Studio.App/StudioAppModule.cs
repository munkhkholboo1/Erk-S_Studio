using System.Reflection;
using System.Windows;
using ErkS.Studio.Contracts;

namespace ErkS.Studio;

/// <summary>
/// Hot-reloadable module entry point. The host discovers this type by name
/// ("ErkS.Studio.StudioAppModule") inside a fresh load context on
/// every DevUpdate.
/// </summary>
public sealed class StudioAppModule : IStudioAppModule
{
    private ShellView? view;

    public string Version
    {
        get
        {
            var assembly = typeof(StudioAppModule).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? assembly.GetName().Version?.ToString() ?? "dev"
                : informational;
        }
    }

    public UIElement CreateRootView()
    {
        // PDFsharp statics live per load context - register the font resolver
        // for this module instance.
        ErkS.Platform.Pdf.WindowsFontResolver.Register();
        view = new ShellView();
        return view.Root;
    }

    public void Shutdown()
    {
        view?.Dispose();
        view = null;
    }
}
