using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The album is drawn when something changed it, and at no other time.
///
/// 🔴 THE OWNER'S PROJECT TOOK TEN MINUTES TO OPEN AND REACHED 9.7 GB, redrawing
/// an album that was already correct. Three things did it: a timer that fired
/// 1.5 seconds after any activity, opening the album page, and the fact that
/// nothing ever consulted the finished album on disk. They ruled on it twice:
///
///   «студио руу илгээхэд альбумаа шинэчлэнэ. синк хийхэд шинэчлэнэ. ингээд л
///    болоошт»
///   «хааж нээх, комыг унтрааж асаах гэх мэт ямар ч үйлдэл нэгэнт шинэчилчихсэн
///    альбумыг дахин уншихад асуудал үүсгэх учиргүй»
///
/// and named the stake: «ингэтлээ гацаад байвал ХЭН Ч ХЭРЭГЛЭХГҮЙ».
/// </summary>
public sealed class THEAlbumIsDrawnByChangeAloneTests
{
    private const string Same = "aaaa1111";
    private const string Moved = "bbbb2222";

    [Fact]
    public void THETwoTriggersTheOwnerNamedAlwaysDraw()
    {
        // A package arriving from a plugin, and a sync. These do not ask whether
        // anything moved - they ARE the two things that move it.
        foreach (StudioWorkspaceOperation origin in new[]
        {
            StudioWorkspaceOperation.SourceRefresh,
            StudioWorkspaceOperation.CloudSync,
        })
        {
            Assert.True(StudioAlbumRebuildPolicy.AlwaysDraws(origin));
            Assert.True(
                StudioAlbumRebuildPolicy.MustDraw(origin, Same, Same, builtAlbumIsPresent: true));
        }
    }

    [Fact]
    public void EVERYTHINGElseDrawsONLYWhenTheInputMoved()
    {
        // Derived from the enum rather than listed: every operation that is NOT
        // one of the owner's two triggers has to obey the fingerprint, including
        // any value added after this was written.
        foreach (StudioWorkspaceOperation origin in Enum.GetValues<StudioWorkspaceOperation>())
        {
            if (StudioAlbumRebuildPolicy.AlwaysDraws(origin))
                continue;

            Assert.False(
                StudioAlbumRebuildPolicy.MustDraw(origin, Same, Same, builtAlbumIsPresent: true));
            Assert.True(
                StudioAlbumRebuildPolicy.MustDraw(origin, Moved, Same, builtAlbumIsPresent: true));
        }
    }

    [Fact]
    public void ANALBUMWhoseFileIsGoneIsDrawnAgain()
    {
        // 🔴 A RECORD IS NOT AN ALBUM. Reading a pointer at a deleted PDF as «still
        // current» would leave a person with nothing on screen and no way to
        // refill it - the skip would have taken away their only route back.
        Assert.True(
            StudioAlbumRebuildPolicy.MustDraw(
                StudioWorkspaceOperation.ExplicitAlbumEdit,
                Same,
                Same,
                builtAlbumIsPresent: false));
    }

    [Theory]
    [InlineData("", "aaaa1111")]
    [InlineData("aaaa1111", "")]
    [InlineData("", "")]
    public void ANUNKNOWNAnswerDrawsRatherThanAssumingNothingChanged(string now, string built)
    {
        // An older project file carries no fingerprint, and a fingerprint that
        // could not be computed carries none either. Reading «I do not know» as
        // «nothing changed» shows somebody an album that no longer matches their
        // work - worse than the delay this rule removes.
        Assert.True(
            StudioAlbumRebuildPolicy.MustDraw(
                StudioWorkspaceOperation.ExplicitAlbumEdit,
                now,
                built,
                builtAlbumIsPresent: true));
    }

    [Fact]
    public void NOTIMERRedrawsTheAlbumANYWHEREInTheApp()
    {
        // 🔴 DERIVED OVER THE PROJECT. The timer is not disabled, defaulted off, or
        // guarded by a checkbox - it is gone, because a timer is «I do not know
        // what changed, so do everything again» and both things that change an
        // album are named events. A disabled timer is a timer somebody re-enables.
        foreach ((string name, string source) in AppSources())
        {
            Assert.DoesNotContain("autoRebuildTimer", source, StringComparison.Ordinal);
        }

        // The instrument: the scan must actually be reading the shell.
        Assert.Contains(
            "UpdateAlbum", ReadAppSource("ShellView.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void OPENINGTheAlbumPageDoesNotRedrawIt()
    {
        // «төсөл нээгдэх үед заавал шинээр бүтээх ямар хэрэг байна вэ?» - the
        // album already on disk is what this page shows.
        string body = MethodBody(ReadAppSource("ShellView.cs"), "private void SelectPage(StudioPage page)");

        Assert.DoesNotContain("UpdateAlbum(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEGuardIsAskedBEFOREAnyOfTheWork()
    {
        // Twenty-six places call the album update. The guard sits at the one
        // place they all pass through, ahead of everything expensive - not at
        // each caller, where the twenty-seventh would miss it.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private bool UpdateAlbum(");

        int guard = body.IndexOf("AlbumMustBeDrawn(origin)", StringComparison.Ordinal);
        int work = body.IndexOf("ShouldCollectProjectUi", StringComparison.Ordinal);
        Assert.True(guard > 0, "the album update no longer asks whether it must draw");
        Assert.True(work > guard, "the guard must come before the work");
    }

    [Fact]
    public void THEFingerprintIsStoredONLYAfterTheBuildSucceeded()
    {
        // 🔴 STORING IT FIRST WOULD SILENCE EVERY LATER BUILD. A fingerprint sitting
        // beside a PDF that was never produced says «this album is current» about
        // a file that does not exist, and nothing would ever draw it again.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"), "private AlbumBuildResult BuildLatestAlbum(");

        int built = body.IndexOf("state.Builder.Build(", StringComparison.Ordinal);
        int stored = body.IndexOf("LastBuildFingerprint = fingerprint", StringComparison.Ordinal);
        Assert.True(built > 0, "the build call is gone");
        Assert.True(stored > built, "the fingerprint must be stored after the build, not before");
    }

    private static IEnumerable<(string Name, string Source)> AppSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
            {
                foreach (FileInfo file in new DirectoryInfo(candidate)
                             .GetFiles("*.cs", SearchOption.TopDirectoryOnly))
                {
                    yield return (file.Name, File.ReadAllText(file.FullName, Encoding.UTF8));
                }

                yield break;
            }

            directory = directory.Parent;
        }

        Assert.Fail("the app project was not found; this test reads it from source");
    }

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
        int start = normalised.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = normalised.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return normalised[start..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
