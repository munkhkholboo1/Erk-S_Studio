namespace ErkS.Studio.App.Tests;

/// <summary>
/// Where the repository is, for the few tests that read files that ship as
/// source rather than as build output - scripts, contracts, documentation.
/// </summary>
internal static class TestRepository
{
    public static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string scriptPath = Path.Combine(
                directory.FullName,
                "src",
                "scripts",
                "Publish-Studio-Demo.ps1");
            if (File.Exists(scriptPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Erk-S Studio repository root.");
    }
}
