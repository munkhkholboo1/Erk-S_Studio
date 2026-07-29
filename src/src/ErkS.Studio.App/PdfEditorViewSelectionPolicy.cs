namespace ErkS.Studio;

internal enum PdfEditorView
{
    Source,
    Studio,
}

internal readonly record struct PdfEditorViewSelection(
    bool SourceActive,
    bool StudioActive);

internal static class PdfEditorViewSelectionPolicy
{
    public static PdfEditorViewSelection Resolve(PdfEditorView displayedView) =>
        displayedView switch
        {
            PdfEditorView.Source => new(SourceActive: true, StudioActive: false),
            PdfEditorView.Studio => new(SourceActive: false, StudioActive: true),
            _ => throw new ArgumentOutOfRangeException(
                nameof(displayedView),
                displayedView,
                "Unsupported PDF editor view."),
        };
}
