using System.Text;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The cross-product contracts, read from a copy that ships with these tests.
///
/// 🔴 WHY A COPY AT ALL. The originals live in the platform root's `_shared`,
/// which is a DIFFERENT REPOSITORY from this one. A test that reads across that
/// boundary passes on the machine where both are checked out and fails
/// everywhere else - and it fails by not finding a file, which reads as a broken
/// test rather than as a missing dependency. SRV lost a CI run to exactly that
/// on 2026-09-07; these tests had acquired the same dependency the same day.
///
/// A copy has its own failure, and it is worse because it is quiet: the original
/// changes and the copy goes on asserting the old contract, green. That is what
/// <see cref="SharedContractCopyTests"/> is for - it compares the two wherever
/// both are present, so drift is loud on any machine that has them.
/// </summary>
internal static class SharedContractCopies
{
    public const string AdministrativeDivisions = "mongolia-admin-divisions-contract-2026-09-06.json";
    public const string EnvelopeSample = "mongolia-admin-divisions-envelope-sample.json";

    /// <summary>The owner's measured A3 concept cover: frame, tables, row heights.</summary>
    public const string ConceptCoverA3 = "concept-cover-A3-2026-09-11.json";

    /// <summary>
    /// The project-address vectors.
    ///
    /// 🔴 THIS FILE WAS VENDORED AND NOT REGISTERED - the exact silent failure
    /// the summary above describes, sitting in the same folder. Its reader found
    /// it by path, so it never joined the drift comparison and could have gone on
    /// asserting an older contract indefinitely, green.
    /// </summary>
    public const string ProjectAddressVectors = "project-address-vectors.json";

    /// <summary>The copy that travels with the tests. Always present.</summary>
    public static string Read(string fileName) =>
        File.ReadAllText(PathTo(fileName), Encoding.UTF8);

    public static string PathTo(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "contracts", fileName);
        Assert.True(File.Exists(path), "the vendored copy of " + fileName + " is missing from the test output");
        return path;
    }

    /// <summary>
    /// The original in the other repository, or null when it is not reachable -
    /// which is the ordinary state of a checkout of this repository alone.
    /// </summary>
    public static string? TryFindOriginal(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "_shared", fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>
/// Keeps the vendored copies honest.
/// </summary>
public sealed class SharedContractCopyTests
{
    [Theory]
    [InlineData(SharedContractCopies.AdministrativeDivisions)]
    [InlineData(SharedContractCopies.EnvelopeSample)]
    [InlineData(SharedContractCopies.ConceptCoverA3)]
    [InlineData(SharedContractCopies.ProjectAddressVectors)]
    public void ACopyThatHasDRIFTEDFromTheOriginalIsLoud(string fileName)
    {
        string? original = SharedContractCopies.TryFindOriginal(fileName);
        if (original is null)
        {
            // The other repository is not checked out here. That is a normal
            // checkout of this one, and the tests still run against the copy -
            // this comparison is simply not available to make.
            return;
        }

        string published = File.ReadAllText(original, Encoding.UTF8);
        string vendored = SharedContractCopies.Read(fileName);

        Assert.True(
            string.Equals(Normalise(published), Normalise(vendored), StringComparison.Ordinal),
            fileName + " has changed in _shared. Copy it over the file in " +
            "tests/ErkS.Platform.Core.Tests/contracts/ and check what the change means - " +
            "these tests are asserting the older contract until you do.");
    }

    [Fact]
    public void THETestsReadTheCOPYRatherThanReachingIntoTheOtherRepository()
    {
        // The rule this file exists to keep. A test that finds `_shared` by
        // walking up from the output directory works on a developer machine and
        // nowhere else, and the next person to add one will copy an existing
        // example - so no example may remain.
        //
        // TryFindOriginal is the single exception and lives here, where it is
        // used to COMPARE rather than to read the contract.
        foreach (string file in Directory.GetFiles(SourceDirectory(), "*.cs"))
        {
            if (Path.GetFileName(file).Equals("SharedContractCopies.cs", StringComparison.Ordinal))
                continue;

            string source = File.ReadAllText(file, Encoding.UTF8);
            Assert.False(
                source.Contains("\"_shared\"", StringComparison.Ordinal),
                Path.GetFileName(file) + " reads _shared from another repository; " +
                "use SharedContractCopies instead");
        }
    }

    /// <summary>Line endings differ between a checkout and a copy; content does not.</summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n', ' ');

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "tests", "ErkS.Platform.Core.Tests");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        Assert.Fail("the test sources were not found");
        return "";
    }
}
