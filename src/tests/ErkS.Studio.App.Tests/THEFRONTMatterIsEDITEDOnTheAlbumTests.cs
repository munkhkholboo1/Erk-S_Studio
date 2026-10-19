using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The working drawings' explanatory note, composition and indicators are the
/// album's, edited from the album page («ЕХ мэдээлэл») and saved with it -
/// never written into the project file from outside while Studio runs.
///
/// The view is WPF, so what it does is read from its source, as its
/// neighbours do.
/// </summary>
public sealed class THEFRONTMatterIsEDITEDOnTheAlbumTests
{
    [Fact]
    public void TheAlbumRibbonCarriesTheButton()
    {
        string source = ReadView("ShellView.Workspaces.cs");

        Assert.Contains("\"ЕХ мэдээлэл\"", source, StringComparison.Ordinal);
        Assert.Contains("documentGroup.Children.Add(frontMatter);", source, StringComparison.Ordinal);
        Assert.Contains("frontMatter.Click += (_, _) => EditWorkingDrawingFrontMatter();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEditIsSavedOnTheAlbumAndTheAlbumRedrawn()
    {
        string source = ReadView("ShellView.Workspaces.cs");
        string edit = Body(source, "private void EditWorkingDrawingFrontMatter()");
        // One function for the dialog and the bridge (docs/STU-BRIDGE.md).
        string apply = Body(source, "private bool ApplyWorkingDrawingFrontMatter(WorkingDrawingFrontMatter matter)");

        Assert.Contains("CanEditProjectContent()", edit, StringComparison.Ordinal);
        Assert.Contains("ApplyWorkingDrawingFrontMatter(dialog.Result);", edit, StringComparison.Ordinal);
        Assert.Contains("state.Album.FrontMatter = matter;", apply, StringComparison.Ordinal);
        Assert.Contains("state.SaveProject();", apply, StringComparison.Ordinal);
        Assert.Contains("UpdateAlbum(", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDialogNormalizesAndCanReadTheDraft()
    {
        string dialog = ReadView("WorkingDrawingFrontMatterDialog.cs");

        Assert.Contains("matter.Normalize();", dialog, StringComparison.Ordinal);
        Assert.Contains("WorkingDrawingFrontMatterDraft.Parse(", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void ASheetInThe01PlaceKeepsItWhenItsPropertiesAreEdited()
    {
        // The concept vocabulary would hand the page a concept slot and drop it
        // out of the working drawings' 01.
        string source = ReadView("ShellView.Workspaces.cs");

        Assert.Contains("if (sheet is not null && !page.FrontMatterOnSheet)", source, StringComparison.Ordinal);
    }

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " was renamed; check this test with it");
        int next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return source[start..(next < 0 ? source.Length : next)];
    }

    private static string ReadView(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8).Replace("\r\n", "\n");
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
