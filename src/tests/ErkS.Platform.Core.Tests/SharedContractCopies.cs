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
    /// The same sheet, measured WHOLE: all 75 objects, with every text and line.
    ///
    /// 🔴 REGISTERED LATE, AND THE GAP WAS THE ONE THIS FILE WARNS ABOUT. The role
    /// labels' four anchors and the six column headings were taken from this file on
    /// 2026-09-12 and turned into product constants, while the file itself stayed
    /// unvendored - so it was outside the drift comparison, and a revised measurement
    /// would have left those constants asserting the old sheet, green. That is the same
    /// fault recorded against ProjectAddressVectors below, committed in the same folder
    /// a few lines away from the warning about it.
    /// </summary>
    public const string ConceptCoverA3Full = "concept-cover-A3-full-2026-09-12.json";

    /// <summary>
    /// MTEXT attachment points, TEXT justification, and the ten TEXT extents AutoCAD
    /// measured for itself.
    ///
    /// This is the file that settles which convention a coordinate is in, and the
    /// answers are not uniform: the title block's ten entities are TEXT with no
    /// attachment point, so their anchor is a baseline, while every label and heading is
    /// top-left MTEXT. It also states what could NOT be measured - textbox returns nil
    /// on MTEXT - which is why «the headings are centred» is a choice here and not a
    /// measurement.
    /// </summary>
    public const string ConceptCoverA3TextExtents = "concept-cover-A3-text-extents-2026-09-12.json";

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
    [InlineData(SharedContractCopies.ConceptCoverA3Full)]
    [InlineData(SharedContractCopies.ConceptCoverA3TextExtents)]
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
    public void EVERYRegisteredCopyIsACaseOfTheDriftComparison()
    {
        // 🔴 REGISTERING A COPY AND FORGETTING THE CASE IS THE SAME SILENT GAP AS NOT
        // REGISTERING IT AT ALL - the constant makes it LOOK covered. Derived from the
        // class's own constants so a new one cannot be added without being compared.
        string[] registered = typeof(SharedContractCopies)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(registered);

        IEnumerable<string> cases = typeof(SharedContractCopyTests)
            .GetMethod(nameof(ACopyThatHasDRIFTEDFromTheOriginalIsLoud))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .Select(attribute => (string)attribute.GetData(null!).First()[0]!);
        var compared = new HashSet<string>(cases, StringComparer.Ordinal);

        foreach (string fileName in registered)
        {
            Assert.True(
                compared.Contains(fileName),
                fileName + " is registered but is not a case of the drift comparison");
        }

        // And every vendored file is registered - a copy in the folder that no constant
        // names is reachable by path and outside all of this.
        foreach (string path in Directory.GetFiles(
                     Path.Combine(AppContext.BaseDirectory, "contracts"), "*.json"))
        {
            Assert.Contains(Path.GetFileName(path), registered);
        }
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
