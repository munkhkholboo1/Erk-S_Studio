namespace ErkS.Studio.App.Tests;

public sealed class StudioForegroundActivationTests
{
    [Fact]
    public void AlbumPdfNavigation_DoesNotActivateOrFocusTheStudioWindow()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "src",
            "ErkS.Studio.App",
            "ShellView.Workspaces.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("SetForegroundWindow(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("selector.SetFocus()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("keybd_event(", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Erk-S Studio repository root was not found.");
    }
}
