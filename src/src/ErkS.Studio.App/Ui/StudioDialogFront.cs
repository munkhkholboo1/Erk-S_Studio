using System.Windows;

namespace ErkS.Studio;

/// <summary>
/// A Studio dialog that waits for a person is shown above every other window and takes focus
/// (Master, 2026-10-27: the update dialog opened behind an elevated CityGen window - clicks went to
/// that window and only Enter reached the dialog). Topmost only while it is open; it closes with the
/// dialog. A window the person did not ask for is still never kept on top afterwards.
/// </summary>
internal static class StudioDialogFront
{
    public static void Keep(Window dialog)
    {
        dialog.Topmost = true;
        dialog.ContentRendered += (_, _) => dialog.Activate();
    }

    /// <summary>The owner a dialog centres on and stays with: the given window, else Studio's main window.</summary>
    public static Window? Owner(Window? preferred) =>
        preferred is { IsVisible: true }
            ? preferred
            : Application.Current?.MainWindow is { IsVisible: true } main ? main : null;
}
