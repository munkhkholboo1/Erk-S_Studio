using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class PdfEditorViewSelectionPolicyTests
{
    [Fact]
    public void Resolve_MarksExactlyTheDisplayedViewAsActive()
    {
        PdfEditorViewSelection source =
            PdfEditorViewSelectionPolicy.Resolve(PdfEditorView.Source);
        PdfEditorViewSelection studio =
            PdfEditorViewSelectionPolicy.Resolve(PdfEditorView.Studio);

        Assert.True(source.SourceActive);
        Assert.False(source.StudioActive);
        Assert.False(studio.SourceActive);
        Assert.True(studio.StudioActive);
        Assert.NotEqual(source.SourceActive, source.StudioActive);
        Assert.NotEqual(studio.SourceActive, studio.StudioActive);
    }
}
