using System.Text;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The first album build after a set of renders says what it is doing.
///
/// 🔴 THE OWNER'S FIRST COMPLAINT WAS NEVER ABOUT SECONDS: «студио үндсэндээ пдф
/// альбом л харуулж байгаа. гэтэл энэ программ ингэтлээ гацаад байвал хэн ч
/// хэрэглэхгүй». A window that stops answering for forty seconds cannot be told from
/// one that has died, and the reader has no way to know the cost is paid once.
///
/// 🔴 SO THE COUNT HAS TO BE KNOWN BEFORE THE WORK, which is why the pass plans first
/// and encodes second. A loop that discovers its own size as it goes can only report
/// when it is finished - and by then nobody needed telling.
///
/// ⚠ THIS IS NOT PROGRESS AND THE CODE SAYS SO. Per-image reporting needs the encoding
/// off the UI thread, which is a larger change and the owner's decision. What is
/// measured here is that the number is correct and arrives BEFORE the work.
/// </summary>
public sealed class ASILENTWaitIsAHangTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "erks-announce-tests", Guid.NewGuid().ToString("N"));

    public ASILENTWaitIsAHangTests() => Directory.CreateDirectory(root);

    [Fact]
    public void THECountIsAnnouncedBEFOREAnyFileIsWritten()
    {
        // 🔴 THE ORDERING IS THE POINT. Announced after the encoding, the sentence
        // reaches the screen exactly when the wait is over.
        (ProjectVisualizationSource source, string projectPath) = Album(3);
        var announced = new List<int>();
        var writtenWhenAnnounced = -1;
        string preparedFolder = Path.Combine(
            Path.GetDirectoryName(projectPath)!, "sources", "visualizations", "prepared");

        StudioVisualizationRasterPreparer.PrepareForAlbum(
            source,
            projectPath,
            images =>
            {
                announced.Add(images);
                writtenWhenAnnounced = Directory.Exists(preparedFolder)
                    ? Directory.GetFiles(preparedFolder).Length
                    : 0;
            });

        Assert.Equal([2], announced);
        Assert.Equal(0, writtenWhenAnnounced);
    }

    [Fact]
    public void ITCountsTheFilesThatWillActuallyBeEncoded()
    {
        // Not «how many images» and not «how many tiles»: the wait is the encoding, so
        // the number is the encodes. One of the three sits alone on its own page at
        // full width and is already coarser than the rule, so it costs nothing.
        (ProjectVisualizationSource source, string projectPath) = Album(3);
        var announced = new List<int>();

        VisualizationRasterPreparation first =
            StudioVisualizationRasterPreparer.PrepareForAlbum(
                source, projectPath, announced.Add);

        Assert.Equal(first.PreparedCount, announced.Single());
        Assert.Equal(3, first.PreparedCount + first.AlreadyCoarseCount);
    }

    [Fact]
    public void ASECONDBuildSaysNOTHINGBecauseThereIsNothingToWaitFor()
    {
        // 🔴 A SENTENCE ON EVERY BUILD IS A SENTENCE NOBODY READS. Everything is reused
        // from the second build onwards, and that build is fast - announcing there
        // would teach the owner to ignore the line that matters.
        (ProjectVisualizationSource source, string projectPath) = Album(3);
        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        (ProjectVisualizationSource again, _) = Reopen(source);
        var announced = new List<int>();
        VisualizationRasterPreparation second =
            StudioVisualizationRasterPreparer.PrepareForAlbum(
                again, projectPath, announced.Add);

        Assert.Empty(announced);
        Assert.Equal(0, second.PreparedCount);
    }

    [Fact]
    public void NOHookMeansNOTrouble()
    {
        // Every other caller of the build project passes nothing. A pass that needed
        // the hook would break twenty-odd call sites and a test suite.
        (ProjectVisualizationSource source, string projectPath) = Album(2);

        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath, null);

        Assert.True(result.PreparedCount > 0);
    }

    [Fact]
    public void THESHELLSaysITIsONETIMEAndPumpsARenderBeforeBlocking()
    {
        // ⚠ SOURCE-ANCHORED, because the sentence and the repaint both need a window.
        // Two things are pinned: the words tell the reader the cost is not repeated,
        // and a render pass is forced - setting the text without that would show it
        // only after the wait, which is when it is useless.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private void AnnounceRasterPreparation(int images)");

        Assert.Contains("нэг удаагийн ажил", body, StringComparison.Ordinal);
        Assert.Contains("давтагдахгүй", body, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", body, StringComparison.Ordinal);

        // And the density is read from the rule, not typed beside it.
        Assert.Contains("AlbumRasterRule.DotsPerInch", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THESHELLIsWiredToTheStateThatRunsThePass()
    {
        // The hook is useless unless somebody sets it, and the only place that can is
        // the shell's own construction.
        string source = ReadAppSource("ShellView.cs");

        Assert.Contains(
            "state.AnnounceRasterPreparation = AnnounceRasterPreparation;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANDTheStateActuallyPassesItOn()
    {
        // 🔴 SABOTAGE FOUND THIS GAP: replacing the argument with null left every
        // test green. «The shell sets the hook» and «the pass is given the hook» are two
        // statements, and only the first one was being made - so the wire could be cut
        // in the middle and nothing would notice. Compared without whitespace, because
        // the claim is the argument, not the line breaks.
        string compact = new string(
            ReadAppSource("AppState.cs").Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.Contains(
            "PrepareForAlbum(snapshot,ProjectPath,AnnounceRasterPreparation)",
            compact,
            StringComparison.Ordinal);
    }

    private (ProjectVisualizationSource Source, string ProjectPath) Album(int images)
    {
        string folder = Path.Combine(root, "project");
        string store = Path.Combine(folder, "sources", "visualizations", "images");
        Directory.CreateDirectory(store);
        string projectPath = Path.Combine(folder, ProjectWorkspace.DefaultFileName);
        var source = new ProjectVisualizationSource
        {
            OwnerProjectId = "p1",
            IsConfigured = true,
            ImagesPerPage = 2,
        };

        for (var i = 0; i < images; i++)
        {
            var hash = new string((char)('a' + i), 64);
            string full = Path.Combine(store, hash + ".png");
            File.WriteAllBytes(full, Dense.Value);
            string relative = ProjectWorkspacePaths.ToRelativePath(projectPath, full);
            source.Images.Add(new ProjectVisualizationImage
            {
                OwnerProjectId = source.OwnerProjectId,
                RelativePath = relative,
                OriginalFileName = relative,
                PixelWidth = Width,
                PixelHeight = Height,
                Sha256 = hash,
                IsAvailable = true,
            });
        }

        return (source, projectPath);
    }

    private static (ProjectVisualizationSource Source, string _) Reopen(
        ProjectVisualizationSource prepared) =>
        (new ProjectVisualizationSource
        {
            OwnerProjectId = prepared.OwnerProjectId,
            IsConfigured = true,
            ImagesPerPage = prepared.ImagesPerPage,
            Images = prepared.Images
                .Select(image =>
                {
                    ProjectVisualizationImage copy = image.Clone();
                    copy.RelativePath = image.OriginalFileName;
                    return copy;
                })
                .ToList(),
        }, "");

    private const int Width = 2400;
    private const int Height = 1600;

    private static readonly Lazy<byte[]> Dense = new(() =>
    {
        int stride = Width * 3;
        var pixels = new byte[stride * Height];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = (byte)(i % 251);
            pixels[i + 1] = (byte)((i / 3) % 241);
            pixels[i + 2] = (byte)((i / 7) % 239);
        }

        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            Width, Height, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, pixels, stride);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    });

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

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
