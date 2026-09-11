using System.Runtime.CompilerServices;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// No test writes into the Studio folder a real person uses.
///
/// 🔴 THEY DID, AND IT DESTROYED THE EVIDENCE OF A DAY'S DIAGNOSIS. The owner's
/// machine recorded two refusals - «bot_state_owner_action_forbidden» - and those
/// two lines were what finally explained why their seat list was empty and their
/// exit did nothing. Then a test run on this machine wrote four fixture refusals
/// into the same file, in the same second, and the owner's two were gone.
///
/// The suite had been writing there all along. Exactly one test class set
/// ERKS_STUDIO_DATA_ROOT to a private folder; seven others exercise routes that
/// record a refusal and set nothing, so every one of their writes landed in
/// «%LocalAppData%\Erk-S Studio» - a real person's data, changed by a test.
///
/// Fixing those seven would have been right until the eighth was written. The
/// redirect is done ONCE for the whole assembly instead, before any test runs, so
/// a new test cannot reach the real folder even by forgetting.
/// </summary>
public static class TestsNeverTouchTheRealStudioFolder
{
    /// <summary>
    /// Where this assembly's tests keep anything Studio would otherwise write to
    /// the account data root.
    /// </summary>
    public static readonly string Root = Path.Combine(
        Path.GetTempPath(),
        "erks-studio-test-data-root",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Runs when the test assembly loads - before any fixture, any collection and
    /// any test. That is the point: a per-class redirect protects the classes
    /// somebody remembered, and this protects the ones they did not.
    /// </summary>
    [ModuleInitializer]
    internal static void RedirectTheAccountDataRoot()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", Root);
    }
}

public sealed class TESTSNeverTouchTheRealStudioFolderTests
{
    [Fact]
    public void THEAccountDataRootIsNotTheRealOne()
    {
        // 🔴 THE ASSERTION THAT WOULD HAVE SAVED THE OWNER'S REFUSAL RECORD. Asked
        // of the product's own resolver, so it holds however the path is built -
        // and it is asked from an ordinary test class, with no fixture of its own,
        // which is exactly the shape of the seven that were writing to the real
        // folder.
        string real = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Erk-S Studio");

        Assert.NotEqual(
            Path.GetFullPath(real),
            Path.GetFullPath(StudioAccountService.AccountDataRoot));
    }

    [Fact]
    public void EVERYTHINGStudioWritesLandsInATemporaryFolder()
    {
        // The positive half: «not the real one» would also pass if the redirect
        // pointed somewhere else that mattered. Everything has to land in a
        // throwaway place.
        //
        // 🔴 ASKED AS «UNDER TEMP», NOT «UNDER MY OWN FOLDER», AND THE FIRST
        // VERSION GOT THAT WRONG. The variable is process-wide and the classes
        // that redirect it run in PARALLEL with this one, so whichever private
        // root is in force at this instant is not knowable from here - the strict
        // assertion failed for a reason that was never a defect. What every one
        // of those roots has in common, and what actually protects a person, is
        // that none of them is outside the temporary area.
        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(StudioAccountService.AccountDataRoot),
            StringComparison.OrdinalIgnoreCase);

        // And the refusal store - the file the owner lost - is inside it.
        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(StudioBoundaryRefusals.StorePath),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EVERYTestThatDrivesTheSERVICEIsSerialisedWithTheStore()
    {
        // 🔴 NINE CLASSES WERE WRITING INTO ANOTHER CLASS'S PRIVATE FOLDER. The
        // refusal store resolves its path through a process-wide variable, so a
        // refusal raised anywhere lands in whatever data root is active at that
        // instant - and while the boundary-refusal tests run, that is THEIR folder.
        // Their counts moved under them about one full-suite run in two, and the
        // failure landed on a class that had done nothing wrong.
        //
        // Derived, not listed: the tenth class to drive an HTTP response would
        // reintroduce it silently, and the symptom would again appear somewhere
        // else entirely.
        var offenders = new List<string>();
        var checkedFiles = 0;
        foreach ((string name, string source) in TestSources())
        {
            bool drives =
                source.Contains("HttpMessageHandler", StringComparison.Ordinal) ||
                source.Contains("HttpResponseMessage", StringComparison.Ordinal);
            if (!drives)
                continue;

            checkedFiles++;
            if (!source.Contains("StudioDataRootCollection.Name", StringComparison.Ordinal))
                offenders.Add(name);
        }

        // The instrument: a scan that matched nothing would pass without reading
        // a line of the suite.
        Assert.True(checkedFiles >= 5, "only " + checkedFiles + " such test files were found");
        Assert.Empty(offenders);
    }

    private static IEnumerable<(string Name, string Source)> TestSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "tests", "ErkS.Studio.App.Tests");
            if (Directory.Exists(candidate))
            {
                foreach (FileInfo file in new DirectoryInfo(candidate)
                             .GetFiles("*.cs", SearchOption.TopDirectoryOnly))
                {
                    yield return (file.Name, File.ReadAllText(file.FullName, System.Text.Encoding.UTF8));
                }

                yield break;
            }

            directory = directory.Parent;
        }

        Assert.Fail("the test project was not found; this test reads it from source");
    }
}
