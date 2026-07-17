using System.Windows;

namespace ErkS.Studio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        var window = new StudioHostWindow();
        MainWindow = window;
        window.Show();
    }
}