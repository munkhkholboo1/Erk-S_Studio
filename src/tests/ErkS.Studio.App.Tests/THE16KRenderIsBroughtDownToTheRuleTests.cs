using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Visualisation images are brought down to the album's own density before drawing.
///
/// 🔴 THE OWNER'S PROBLEM, IN THEIR WORDS: «харагдах байдлын нэг зураг л гэхэд 16к
/// рендер байгаа». The ceiling is Studio's, not the source's - they render at
/// whatever size suits them and keep improving those renders; the album is what has
/// to stay carriable.
///
/// 🔴 TESTED ON REAL FILES, because the claim is about bytes on a disk. A plan
/// computed from records proves the arithmetic; it does not prove that the thing
/// which writes JPEGs obeys it, and the arithmetic already has its own tests in Core.
/// </summary>
public sealed class THE16KRenderIsBroughtDownToTheRuleTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "erks-prepare-tests", Guid.NewGuid().ToString("N"));

    public THE16KRenderIsBroughtDownToTheRuleTests() => Directory.CreateDirectory(root);

    [Fact]
    public void ADENSEImageIsWrittenAtTheSizeTheRULEAsksFor()
    {
        (ProjectVisualizationSource source, string projectPath) = Project();
        ProjectVisualizationImage image = AddDense(source, projectPath);

        // 🔴 THE EXPECTATION IS DERIVED, NOT TYPED IN. Hand-writing «2126 px» here
        // would make this test agree with whatever number I had in mind today rather
        // than with the rule - and if the layout ever changes the frame, a typed
        // number goes quietly wrong instead of red.
        VisualizationImageTilePlan tile = OnlyTile(source);
        VisualizationRasterPlan expected = VisualizationRasterBudget.For(
            image.PixelWidth, image.PixelHeight, tile.Frame, tile.FitMode);
        Assert.True(expected.Resample, "the fixture is not dense enough to exercise this");

        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        Assert.Equal(1, result.PreparedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(expected.PixelWidth, Measure(Full(projectPath, image.RelativePath)).PixelWidth);
        Assert.EndsWith(".jpg", image.RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.PreparedBytes > 0);
    }

    [Fact]
    public void PREPARATIONChangesTheBytesAndNEVERTheLayout()
    {
        // 🔴 THE LOAD-BEARING CLAIM OF THE WHOLE FEATURE. The page composition is
        // chosen from each record's PixelWidth/PixelHeight and from the aspect ratios
        // that follow; writing the prepared sizes back would let preparation move
        // tiles and flip a fit mode across MaximumCropFraction - changing the very
        // frame whose size was just computed. An album that reshuffles itself because
        // it got smaller is a different album.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);
        Add(source, projectPath, 2600, 2600);
        Add(source, projectPath, 4000, 1500);

        string before = Describe(source);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        Assert.Equal(before, Describe(source));
    }

    [Fact]
    public void THEOwnersOWNRecordsStillPointAtTheirRenders()
    {
        // The build snapshot is a clone, and this is what that buys: after an album
        // is drawn, the owner's project still names their originals. Had preparation
        // rewritten their records, their 16k renders would be reachable only through
        // Studio's cache - and the next sweep would call them orphans.
        (ProjectVisualizationSource source, string projectPath) = Project();
        ProjectVisualizationImage original = AddDense(source, projectPath);
        string originalPath = original.RelativePath;

        var snapshot = new ProjectVisualizationSource
        {
            OwnerProjectId = source.OwnerProjectId,
            IsConfigured = true,
            ImagesPerPage = source.ImagesPerPage,
            Images = source.Images.Select(item => item.Clone()).ToList(),
        };
        StudioVisualizationRasterPreparer.PrepareForAlbum(snapshot, projectPath);

        Assert.NotEqual(originalPath, snapshot.Images[0].RelativePath);
        Assert.Equal(originalPath, original.RelativePath);
        Assert.True(File.Exists(Full(projectPath, originalPath)), "the source copy was consumed");
    }

    [Fact]
    public void ASECONDPassREUSESTheCopyAndRewritesNothing()
    {
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);

        VisualizationRasterPreparation first =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);
        string preparedPath = Full(projectPath, source.Images[0].RelativePath);
        DateTime written = File.GetLastWriteTimeUtc(preparedPath);

        (ProjectVisualizationSource again, _) = Reopen(source);
        VisualizationRasterPreparation second =
            StudioVisualizationRasterPreparer.PrepareForAlbum(again, projectPath);

        Assert.Equal(1, first.PreparedCount);
        Assert.Equal(0, first.ReusedCount);
        Assert.Equal(0, second.PreparedCount);
        Assert.Equal(1, second.ReusedCount);
        Assert.Equal(written, File.GetLastWriteTimeUtc(preparedPath));
    }

    [Fact]
    public void ALOSTPreparedCopyIsREBUILTNotReportedMissing()
    {
        // 🔴 THE PREPARED FILE IS DERIVED, AND THE DIFFERENCE MATTERS. A payload that
        // vanishes is «content missing» and the owner has to go and find it; a
        // derived file that vanishes is simply made again. Conflating the two would
        // have the album announce a lost render over a cache the product can rebuild
        // in a second.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);
        File.Delete(Full(projectPath, source.Images[0].RelativePath));

        (ProjectVisualizationSource again, _) = Reopen(source);
        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(again, projectPath);

        Assert.Equal(1, result.PreparedCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public void ANIMPROVEDRenderGetsItsOWNPreparedCopy()
    {
        // 🔴 THE OWNER'S WAY OF WORKING IS TO OVERWRITE: «тэр хангалтгүй хэмжээнд
        // байгаа зурагнуудаа сайжруулсаар байх болно». Keyed by content hash, so a
        // better render cannot be served yesterday's pixels - which is the failure a
        // cache keyed by file name or by date would have.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);
        string firstPrepared = source.Images[0].RelativePath;

        (ProjectVisualizationSource improved, _) = Reopen(source);
        improved.Images[0].Sha256 = new string('c', 64);
        StudioVisualizationRasterPreparer.PrepareForAlbum(improved, projectPath);

        Assert.NotEqual(firstPrepared, improved.Images[0].RelativePath);
    }

    [Theory]
    [InlineData("truncated-magic")]
    [InlineData("not-an-image")]
    [InlineData("empty")]
    [InlineData("locked-by-another-program")]
    public void ONEUnreadableRenderCostsTHATTileOnlyNotTheAlbum(string damage)
    {
        // 🔴 A REFUSAL THAT THREW AWAY THE BATCH WOULD TURN ONE BAD FILE INTO TOTAL
        // LOSS - 46 pages withheld over one render. The owner sees their image on
        // that tile, at source size, and the observation line says how many.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);
        ProjectVisualizationImage broken = AddDense(source, projectPath);
        AddDense(source, projectPath);

        string brokenPath = Full(projectPath, broken.RelativePath);
        string keptPath = broken.RelativePath;

        // 🔴 SEVERAL KINDS OF TROUBLE, BECAUSE ONE PAYLOAD PROVES ONE EXCEPTION
        // TYPE. The list of handled exceptions looked finished on a single corrupt
        // fixture; a locked file - the owner's render tool still holding it open, which
        // is the likeliest of the lot in practice - arrives as a different type
        // entirely, and «looked finished» is how one bad render ends up costing the
        // whole album.
        //
        // ⚠ A LENIENTLY TRUNCATED PNG IS NOT IN THIS LIST, AND THAT IS A FINDING
        // RATHER THAN AN OMISSION: WPF's decoder happily produces an image from a
        // half-written PNG, so nothing fails and the album carries what the decoder
        // made of it. That is what the album did before this feature too.
        FileStream? heldOpen = null;
        if (damage == "locked-by-another-program")
        {
            heldOpen = new FileStream(
                brokenPath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        else
        {
            byte[] rubbish = damage switch
            {
                "truncated-magic" => [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x00, 0x00],
                "not-an-image" => System.Text.Encoding.UTF8.GetBytes("this is not a picture"),
                _ => [],
            };
            File.WriteAllBytes(brokenPath, rubbish);
        }

        using (heldOpen)
        {
            AssertOnlyThatTileWasLost(source, projectPath, broken, keptPath);
        }
    }

    private static void AssertOnlyThatTileWasLost(
        ProjectVisualizationSource source,
        string projectPath,
        ProjectVisualizationImage broken,
        string keptPath)
    {

        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(2, result.PreparedCount);
        Assert.Equal(keptPath, broken.RelativePath);
        Assert.Equal(3, result.ConsideredCount);
    }

    [Fact]
    public void ANIMAGEAlreadyCoarseEnoughIsLEFTALONE()
    {
        // Enlarging invents detail nobody photographed, and rewriting a file that is
        // already correct would spend the disk the rule exists to save.
        (ProjectVisualizationSource source, string projectPath) = Project();
        ProjectVisualizationImage image = Add(source, projectPath, 120, 90);
        string before = image.RelativePath;

        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        Assert.Equal(1, result.AlreadyCoarseCount);
        Assert.Equal(0, result.PreparedCount);
        Assert.Equal(before, image.RelativePath);
    }

    [Fact]
    public void AMISSINGPayloadIsCountedApartFromAFailure()
    {
        // Nothing here could have prepared it, and the album already has a sentence
        // for it. Counting the two together would blame the preparer for a file the
        // owner never copied across.
        (ProjectVisualizationSource source, string projectPath) = Project();
        ProjectVisualizationImage image = AddDense(source, projectPath);
        File.Delete(Full(projectPath, image.RelativePath));

        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        Assert.Equal(1, result.MissingCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.PreparedCount);
    }

    [Fact]
    public void THEPreparedCopyLivesOUTSIDETheStoreTheSweepWalks()
    {
        // 🔴 DERIVED FILES MUST NOT SIT AMONG THE PAYLOADS THEY CAME FROM. The store
        // sweep removes anything in sources/visualizations/images that no record
        // points at; a prepared copy dropped in there is unreferenced by the owner's
        // project by construction, so it would be deleted on the next reconciling
        // build and rebuilt on the one after, for ever.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        string prepared = Full(projectPath, source.Images[0].RelativePath);
        string payloadStore = Path.Combine(
            Path.GetDirectoryName(projectPath)!, "sources", "visualizations", "images");

        Assert.False(
            ProjectWorkspacePaths.IsInside(payloadStore, prepared),
            "the prepared copy was written into the store the sweep walks");
        Assert.True(ProjectWorkspacePaths.IsInside(
            Path.Combine(Path.GetDirectoryName(projectPath)!, "sources"),
            prepared));
    }

    [Fact]
    public void ATRANSPARENTPixelBecomesWhiteBecauseJPEGHasNoAlpha()
    {
        // 🔴 WHITE IS WHAT THE PAGE ALREADY SHOWS. The writer fills each tile with a
        // white rectangle before drawing into it, so a transparent region reads white
        // in today's album. Discarding alpha instead hands the encoder whatever sat
        // underneath, which in a PNG's fully transparent areas is routinely black -
        // a render would come back with a border nobody drew.
        //
        // 🔴 AND THIS PATH IS UNREACHED BY EVERY IMAGE THE OWNER HAS TODAY: alpha was
        // measured absent in all 26. That is exactly why it needs a test and not a
        // comment promising it works.
        var transparent = new byte[] { 0, 0, 0, 0, 20, 40, 60, 255 };
        BitmapSource withAlpha = BitmapSource.Create(
            2, 1, 96, 96, PixelFormats.Bgra32, null, transparent, 2 * 4);

        BitmapSource flattened = StudioVisualizationRasterPreparer.OnWhite(withAlpha);

        Assert.Equal(PixelFormats.Bgr24, flattened.Format);
        var pixels = new byte[2 * 3];
        flattened.CopyPixels(pixels, 2 * 3, 0);
        Assert.Equal(255, pixels[0]);
        Assert.Equal(255, pixels[1]);
        Assert.Equal(255, pixels[2]);
        Assert.Equal(20, pixels[3]);
        Assert.Equal(40, pixels[4]);
        Assert.Equal(60, pixels[5]);
    }

    [Fact]
    public void ANOpaqueImageIsHANDEDBackUntouched()
    {
        // 🔴 THE LIMIT OF THE FLATTENING, ASSERTED. Copying every opaque image
        // through a managed blend would spend time on 26 of 26 renders to serve a
        // case none of them are in. Same instance, not merely equal pixels - equal
        // pixels would pass on a pointless copy.
        var opaque = new byte[] { 10, 20, 30 };
        BitmapSource noAlpha = BitmapSource.Create(
            1, 1, 96, 96, PixelFormats.Bgr24, null, opaque, 3);

        Assert.Same(noAlpha, StudioVisualizationRasterPreparer.OnWhite(noAlpha));
    }

    [Fact]
    public void NOProjectPathMeansNOPreparationAndNOThrow()
    {
        // A project that has never been saved has nowhere to put a prepared copy.
        // Preparation is an optimisation; taking the album build down with it would
        // make the album depend on a cache.
        (ProjectVisualizationSource source, _) = Project();

        Assert.Equal(
            VisualizationRasterPreparation.Nothing,
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, ""));
        Assert.Equal(
            VisualizationRasterPreparation.Nothing,
            StudioVisualizationRasterPreparer.PrepareForAlbum(null, "C:\\x\\project.erksproject"));
    }

    [Fact]
    public void ANIMPROVEDRenderLEAVESNoSupersededCopyBehind()
    {
        // 🔴 THE FIX FOR THE STORE'S GROWTH CREATED THE SAME GROWTH ONE FOLDER
        // OVER. Prepared copies are named by content hash too, so each render the owner
        // improves leaves its previous prepared copy behind - and improving renders is
        // their whole way of working. 26 stale copies at a few megabytes each is not
        // the 1.8 GB the payload store leaked, but it is the identical defect and it
        // arrived with my own feature.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);
        string firstCopy = Full(projectPath, source.Images[0].RelativePath);

        (ProjectVisualizationSource improved, _) = Reopen(source);
        improved.Images[0].Sha256 = new string('c', 64);
        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(improved, projectPath);

        string secondCopy = Full(projectPath, improved.Images[0].RelativePath);
        Assert.NotEqual(firstCopy, secondCopy);
        Assert.True(File.Exists(secondCopy), "the new prepared copy is missing");
        Assert.False(File.Exists(firstCopy), "the superseded copy was left behind");
        Assert.Equal(1, result.CacheRemovedCount);
        Assert.True(result.CacheRemovedBytes > 0);
    }

    [Fact]
    public void TICKINGAnImageOutRELAYSThePageAndREPREPARESTheRest()
    {
        // 🔴 THIS TEST WAS WRITTEN TO PROVE THE OPPOSITE AND DISPROVED IT. The
        // claim was that gating the sweep on «something new appeared» spares a
        // ticked-out image its prepared copy, so ticking it back in is free. It is not:
        // removing an image RE-LAYS OUT the page, the remaining ones land in bigger
        // frames, need more pixels, and are prepared afresh - so the pass writes new
        // copies and sweeps the superseded ones.
        //
        // ⚠ THE COST IS REAL AND BELONGS ON THE RECORD: changing the composition of
        // a 26-image album re-encodes everything still in it. It is inherent to sizing
        // by placed size, which is the owner's rule - a fixed pixel count would avoid
        // it and starve every large tile instead.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);
        AddDense(source, projectPath);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);
        string twoUpCopy = Full(projectPath, source.Images[0].RelativePath);
        string tickedOutCopy = Full(projectPath, source.Images[1].RelativePath);

        (ProjectVisualizationSource again, _) = Reopen(source);
        again.Images[1].IsIncludedInAlbum = false;
        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(again, projectPath);

        // One image alone gets the whole content area, so it needs MORE pixels than it
        // did sharing the page - a different file, not the one already on disk.
        Assert.Equal(1, result.PreparedCount);
        Assert.NotEqual(twoUpCopy, Full(projectPath, again.Images[0].RelativePath));
        Assert.True(File.Exists(Full(projectPath, again.Images[0].RelativePath)));

        // And both copies from the two-up layout are now referenced by nothing.
        Assert.Equal(2, result.CacheRemovedCount);
        Assert.False(File.Exists(twoUpCopy), "the superseded two-up copy was left behind");
        Assert.False(File.Exists(tickedOutCopy), "the ticked-out copy was left behind");
    }

    [Fact]
    public void IMPROVINGOneRenderLEAVESTheOtherImagesCopiesAlone()
    {
        // 🔴 THE MOST DANGEROUS CASE IN A CACHE SWEEP: deleting a live entry for an
        // image nobody touched. A reference set built only from the copies written THIS
        // pass would name one image and orphan the other twenty-five - so every build
        // after an improvement would re-encode the whole album, for ever, and the first
        // symptom would be «why is it always slow now».
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);
        AddDense(source, projectPath);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);
        string untouched = Full(projectPath, source.Images[1].RelativePath);

        (ProjectVisualizationSource improved, _) = Reopen(source);
        improved.Images[0].Sha256 = new string('z', 64);
        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(improved, projectPath);

        Assert.Equal(1, result.PreparedCount);
        Assert.Equal(1, result.ReusedCount);
        Assert.Equal(1, result.CacheRemovedCount);
        Assert.True(
            File.Exists(untouched),
            "the prepared copy of an image that did not change was swept away");
    }

    [Fact]
    public void THECacheSweepNEVERReachesTheOwnersPayloads()
    {
        // 🔴 DELETION CODE, SO THE LIMIT IS ASSERTED RATHER THAN REASONED ABOUT.
        // The prepared cache and the payload store are sibling folders; a sweep that
        // took the wrong one would remove the renders this whole feature exists to
        // preserve.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);
        string payload = Full(projectPath, source.Images[0].OriginalFileName);

        StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        (ProjectVisualizationSource improved, _) = Reopen(source);
        improved.Images[0].Sha256 = new string('c', 64);
        VisualizationRasterPreparation result =
            StudioVisualizationRasterPreparer.PrepareForAlbum(improved, projectPath);

        Assert.Equal(1, result.CacheRemovedCount);
        Assert.True(File.Exists(payload), "the sweep reached into the payload store");
    }

    [Fact]
    public void APASSWithNothingPreparedRemovesNothing()
    {
        // «Nothing to keep» and «keep nothing» must never be the same instruction in
        // deletion code. A pass over images already coarse enough writes no copy and
        // must take none away.
        //
        // ⚠ WHAT IS NOT CLAIMED HERE: the «only sweep when something was written»
        // gate is a COST choice, not a correctness one, and no test pins it. Sweeping
        // on every pass would remove nothing extra - reused copies are in the reference
        // set too, and a pass with an empty set is refused by the rule in Core. The gate
        // spares the steady state a directory listing, which is all it spares. Writing
        // a test that pretended otherwise would be a test of my own comment.
        (ProjectVisualizationSource source, string projectPath) = Project();
        Add(source, projectPath, 120, 90);

        VisualizationRasterPreparation coarse =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);

        Assert.Equal(0, coarse.PreparedCount);
        Assert.Equal(0, coarse.CacheRemovedCount);
    }

    [Fact]
    public void THEPassREPORTSHowLongItTook()
    {
        // 🔴 THE NUMBER HAS TO COME FROM THE WORK, NOT FROM A GUESS. Master asked
        // for the preparation time separately from the build time, and there was no
        // way to answer: nothing measured it. A pass that did real work must report
        // more than zero, and a pass that did nothing must not invent a duration.
        (ProjectVisualizationSource source, string projectPath) = Project();
        AddDense(source, projectPath);

        VisualizationRasterPreparation did =
            StudioVisualizationRasterPreparer.PrepareForAlbum(source, projectPath);
        VisualizationRasterPreparation nothing =
            StudioVisualizationRasterPreparer.PrepareForAlbum(null, projectPath);

        Assert.True(did.Seconds > 0d, "a pass that encoded an image reported no time");
        Assert.Equal(0d, nothing.Seconds);
    }

    [Fact]
    public void THEDrawStopwatchStartsAFTERTheBuildProjectIsMade()
    {
        // 🔴 THIS IS WHAT MAKES THE TWO DURATIONS DISJOINT. Preparation happens
        // inside CreateAlbumBuildProject; if the draw stopwatch started before that
        // call, the draw seconds would silently include the preparation and reporting
        // both would double-count the same forty seconds. The order is the whole
        // claim, so it is asserted rather than trusted.
        //
        // 🔴 THE FIRST STOPWATCH IN THE METHOD, NOT ANY STOPWATCH AFTER THE CALL.
        // Sabotage that ADDED a timestamp before the build call survived the first
        // version of this test: searching forward from the build call still found the
        // original one further down, so «a stopwatch starts after the build» was true
        // while the thing it stood for had been broken. What matters is that nothing
        // starts timing EARLIER.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private AlbumBuildResult BuildLatestAlbum(");

        int build = body.IndexOf(
            "state.CreateAlbumBuildProject(",
            StringComparison.Ordinal);
        int stopwatch = body.IndexOf("Stopwatch.GetTimestamp()", StringComparison.Ordinal);

        Assert.True(build > 0, "the album build project is no longer made here");
        Assert.True(stopwatch > 0, "the draw is no longer timed at all");
        Assert.True(
            stopwatch > build,
            "something starts timing before the build project is made, so the draw " +
            "seconds now include the preparation seconds and both report the same time");
    }

    [Fact]
    public void THEPreparerIsGivenTheSNAPSHOTNotTheOwnersRecords()
    {
        // 🔴 EVERY TEST ABOVE CALLS THE PREPARER DIRECTLY, SO NONE OF THEM CAN
        // SEE WHAT STUDIO HANDS IT. Passing the live source instead of the clone would
        // repoint the owner's own records at Studio's cache - their 16k renders would
        // then be referenced by nothing, and the next sweep would remove them as
        // orphans. The difference is one identifier at one call site.
        string body = MethodBody(
            ReadAppSource("AppState.cs"),
            "private ProjectVisualizationSource CreateAlbumVisualizationSnapshot()");

        Assert.Contains("PrepareForAlbum(snapshot,", body);
        Assert.DoesNotContain("PrepareForAlbum(Project.", body);
    }

    [Fact]
    public void ASTALEUnpreparedCountIsREADBeforeItIsDecidedNotToRecord()
    {
        // 🔴 THE PASS THAT HAS NOTHING TO SAY STILL HAS SOMETHING TO STOP
        // SAYING. Reuse-only passes write nothing, which is right - but if a stored
        // «2 images at source size» has since been fixed, skipping the write leaves
        // the line describing an album that no longer exists. The record itself is
        // tested for this above; what cannot be tested from there is whether the
        // caller bothers to look.
        //
        // ⚠ THIS IS A SOURCE ASSERTION AND IT ONLY PINS THAT THE STORED COUNT IS
        // CONSULTED BEFORE THE DECISION. It would survive a wrong comparison; the
        // behaviour it stands in for needs an AppState instance, which needs a
        // project on disk and an identity.
        string body = MethodBody(
            ReadAppSource("AppState.cs"),
            "private ProjectVisualizationSource CreateAlbumVisualizationSnapshot()");

        int read = body.IndexOf("LastUnpreparedImageCount", StringComparison.Ordinal);
        int decision = body.IndexOf("RecordRasterPreparation(", StringComparison.Ordinal);

        Assert.True(read > 0, "the stored count is never read, so a stale one cannot clear");
        Assert.True(decision > read, "the count is read after the decision, which is too late");
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
                return File.ReadAllText(candidate, System.Text.Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }

    /// <summary>
    /// The tiles as the writer would lay them out, spelled as text so a single
    /// assertion covers frames, fit modes, crop fractions and ORDER at once.
    /// </summary>
    private static string Describe(ProjectVisualizationSource source) =>
        string.Join(
            " | ",
            VisualizationPageLayoutPlanner.Create(source, 1)
                .SelectMany(page => page.Tiles.Select(tile =>
                    $"{page.Number}:{tile.Frame.X:0.###},{tile.Frame.Y:0.###}," +
                    $"{tile.Frame.Width:0.###},{tile.Frame.Height:0.###}," +
                    $"{tile.FitMode},{tile.CropFraction:0.#####}")));

    private static VisualizationImageTilePlan OnlyTile(ProjectVisualizationSource source) =>
        VisualizationPageLayoutPlanner.Create(source, 1).Single().Tiles.Single();

    private (ProjectVisualizationSource Source, string ProjectPath) Project()
    {
        string folder = Path.Combine(root, "project");
        Directory.CreateDirectory(folder);
        return (
            new ProjectVisualizationSource
            {
                OwnerProjectId = "p1",
                IsConfigured = true,
                ImagesPerPage = 4,
            },
            Path.Combine(folder, ProjectWorkspace.DefaultFileName));
    }

    /// <summary>The same records again, as a later build would see them.</summary>
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
                    copy.RelativePath = Original(image);
                    return copy;
                })
                .ToList(),
        }, "");

    /// <summary>
    /// Where the record pointed before any preparation. Stashed on the record's
    /// OriginalFileName at creation so a second pass starts where a real build
    /// starts - from the project on disk, which always names the payload.
    /// </summary>
    private static string Original(ProjectVisualizationImage image) => image.OriginalFileName;

    /// <summary>
    /// A render dense enough to need preparing, at the size the owner's actually are.
    ///
    /// 🔴 ONE IMAGE ON A PAGE GETS THE WHOLE 395 mm CONTENT AREA, so «dense
    /// enough to need preparing» means about 4600 px across - there is no cheap
    /// fixture for this, because the rule is about placed size and a small image
    /// fitted into a large frame is by definition not dense. Encoded once for the
    /// whole run and copied per fixture; encoding it per test cost seconds each.
    /// </summary>
    private static readonly Lazy<byte[]> DenseRender = new(() => EncodePng(4700, 3200));

    private const int DenseWidth = 4700;
    private const int DenseHeight = 3200;

    private ProjectVisualizationImage AddDense(
        ProjectVisualizationSource source,
        string projectPath) =>
        Add(source, projectPath, DenseWidth, DenseHeight);

    private ProjectVisualizationImage Add(
        ProjectVisualizationSource source,
        string projectPath,
        int pixelWidth,
        int pixelHeight)
    {
        string store = Path.Combine(
            Path.GetDirectoryName(projectPath)!, "sources", "visualizations", "images");
        Directory.CreateDirectory(store);

        var hash = new string((char)('a' + source.Images.Count), 64);
        string fileName = hash + ".png";
        string fullPath = Path.Combine(store, fileName);
        if (pixelWidth == DenseWidth && pixelHeight == DenseHeight)
            File.WriteAllBytes(fullPath, DenseRender.Value);
        else
            WritePng(fullPath, pixelWidth, pixelHeight);

        string relative = ProjectWorkspacePaths.ToRelativePath(projectPath, fullPath);
        var image = new ProjectVisualizationImage
        {
            OwnerProjectId = source.OwnerProjectId,
            RelativePath = relative,
            OriginalFileName = relative,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            Sha256 = hash,
            IsAvailable = true,
        };
        source.Images.Add(image);
        return image;
    }

    private static void WritePng(string path, int width, int height) =>
        File.WriteAllBytes(path, EncodePng(width, height));

    private static byte[] EncodePng(int width, int height)
    {
        int stride = width * 3;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            // A gradient rather than one flat colour: a decoder asked for a smaller
            // size has something to average, so a wrong scale shows up as a wrong
            // pixel count rather than as an identical block of grey.
            pixels[i] = (byte)(i % 251);
            pixels[i + 1] = (byte)((i / 3) % 241);
            pixels[i + 2] = (byte)((i / 7) % 239);
        }

        BitmapSource bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static string Full(string projectPath, string relativePath) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, relativePath));

    private static BitmapFrame Measure(string path)
    {
        using FileStream input = File.OpenRead(path);
        return BitmapDecoder
            .Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
            .Frames[0];
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
