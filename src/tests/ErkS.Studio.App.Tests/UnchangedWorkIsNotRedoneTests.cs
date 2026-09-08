using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The user's own question, turned into a rule: «шинэчлэгдээгүй мэдээллийг
/// дахин дахин татах процесс хийгдэж байгаа эсэхийг шалга.»
///
/// 🔴 THE ANSWER WAS YES, AND THE EVIDENCE WAS ALREADY IN THE METHOD. Refreshing
/// the assigned organization computes `changed` - a full comparison covering the
/// logo, both scan lists and the timestamp - and used it to decide whether to
/// rebind the UI while rebuilding the ALBUM either way. An album rebuild
/// reconciles every linked project asset and composes the cloud union album, so
/// an organisation whose details had not moved since yesterday still cost the
/// full price, every time.
///
/// This is the fourth appearance tonight of one shape: expensive work done
/// without first asking whether there is any work to do.
/// </summary>
public sealed class UnchangedWorkIsNotRedoneTests
{
    [Fact]
    public void ANUnchangedOrganizationDoesNotRebuildTheAlbum()
    {
        string companies = ReadAppSource("ShellView.Companies.cs");
        int guard = UnchangedGuard(companies);

        int rebuild = companies.IndexOf(
            "UpdateAlbum(silent: true, statusPrefix: \"Компанийн мэдээлэл шинэчлэгдлээ\")",
            StringComparison.Ordinal);
        Assert.True(rebuild > guard, "the rebuild must sit behind the unchanged guard");
    }

    [Fact]
    public void SKIPPINGIsSAIDOutLoudRatherThanDoneSilently()
    {
        // Silence is indistinguishable from being broken - and "nothing
        // happened, no explanation" is what sent the user hunting for a defect.
        string companies = ReadAppSource("ShellView.Companies.cs");
        int guard = UnchangedGuard(companies);
        string branch = companies[guard..(guard + 400)];
        Assert.Contains("SetStatus(", branch, StringComparison.Ordinal);
        Assert.Contains("өөрчлөгдөөгүй", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void THEChangedFlagIsAFULLComparisonAndNotAShallowOne()
    {
        // The guard is only as good as what `changed` looks at. If it stopped
        // comparing the logo or the scans, an organisation whose certificate
        // was replaced would be judged unchanged and the album would keep the
        // old page - a silent loss far worse than the slowness being fixed.
        string service = ReadCoreSource("ProjectCompanyAssignmentService.cs");
        int equal = service.IndexOf("private static bool ProfilesEqual(", StringComparison.Ordinal);
        Assert.True(equal > 0, "the comparison is gone");

        string body = service[equal..service.IndexOf("\n    /// <summary>", equal, StringComparison.Ordinal)];

        Assert.Contains("LogoPath", body, StringComparison.Ordinal);
        Assert.Contains("RegistrationCertificateDocuments", body, StringComparison.Ordinal);
        Assert.Contains("DesignLicenseDocuments", body, StringComparison.Ordinal);
        Assert.Contains("UpdatedAtUtc", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard THAT GATES THE REBUILD, not the first "if (!changed)" in the
    /// file - there are two, and the earlier one belongs to a different method.
    /// The first version of these tests matched that one and passed while
    /// asserting nothing about the fix. Anchored to the rebuild guard above it.
    /// </summary>
    private static int UnchangedGuard(string companies)
    {
        int rebuildGate = companies.IndexOf("if (!rebuildAlbum)", StringComparison.Ordinal);
        Assert.True(rebuildGate > 0, "the rebuild gate is gone");

        int guard = companies.IndexOf("if (!changed)", rebuildGate, StringComparison.Ordinal);
        Assert.True(guard > rebuildGate, "the unchanged guard is gone");
        return guard;
    }

    private static string ReadAppSource(string fileName) =>
        ReadSource("ErkS.Studio.App", fileName);

    private static string ReadCoreSource(string fileName) =>
        ReadSource("ErkS.Platform.Core", fileName);

    private static string ReadSource(string project, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", project, fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8).Replace("\r\n", "\n");
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
