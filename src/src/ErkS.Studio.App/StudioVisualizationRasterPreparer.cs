using System.IO;
using System.Windows.Media.Imaging;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>What one preparation pass did to the visualisation images.</summary>
/// <param name="PreparedCount">Copies written by this pass.</param>
/// <param name="ReusedCount">Copies a previous pass had already written.</param>
/// <param name="AlreadyCoarseCount">
/// Images left exactly as they are because they are already at or below the
/// album's density. Enlarging one would invent detail nobody photographed.
/// </param>
/// <param name="FailedCount">
/// Images that could not be prepared and went into the album AT SOURCE SIZE.
///
/// 🔴 THIS IS THE WHOLE POINT OF THE COUNT AND WHY IT IS NOT AN EXCEPTION. One
/// unreadable render out of 26 must not cost the owner a 46-page album - a refusal
/// that throws away the whole batch turns one bad file into total loss. The page
/// still shows their image; the album is simply heavier, and this number says by
/// how many.
/// </param>
/// <param name="MissingCount">
/// Images whose payload is not on this machine at all. Counted separately from a
/// failure because the album already has a sentence for it («Зураг олдсонгүй») and
/// because nothing here could have prepared it.
/// </param>
/// <param name="PreparedBytes">Total size of the prepared copies now in use.</param>
/// <param name="CacheRemovedCount">
/// Superseded prepared copies deleted.
///
/// 🔴 THE FEATURE THAT FIXED THE STORE'S GROWTH CREATED ITS OWN. Prepared copies
/// are named by content hash too, so every render the owner improves leaves its
/// previous prepared copy behind - the same defect, one folder over, introduced by
/// the fix for the first one. Reported separately from the payload sweep because
/// these files are DERIVED: losing one costs a re-encode, losing a payload costs the
/// owner their render, and a single number would make the two indistinguishable on
/// the day one of them goes wrong.
/// </param>
/// <param name="CacheRemovedBytes">How much disk those gave back.</param>
/// <param name="Seconds">
/// How long the pass took.
///
/// 🔴 SEPARATELY MEASURABLE FROM THE DRAW, WHICH IS THE WHOLE REASON IT EXISTS.
/// The build stopwatch starts AFTER the build project is made, so the draw seconds
/// and these never overlap - «the album took two minutes» can be split into «and
/// forty of them were the first preparation of 26 images», which is the difference
/// between a one-off cost and a permanent one. Timed here rather than at the caller
/// so previews, which also prepare, are timed by the same clock.
/// </param>
internal sealed record VisualizationRasterPreparation(
    int PreparedCount,
    int ReusedCount,
    int AlreadyCoarseCount,
    int FailedCount,
    int MissingCount,
    long PreparedBytes,
    double Seconds = 0d,
    int CacheRemovedCount = 0,
    long CacheRemovedBytes = 0L)
{
    internal static VisualizationRasterPreparation Nothing { get; } =
        new(0, 0, 0, 0, 0, 0);

    /// <summary>How many images the pass looked at, whatever it decided.</summary>
    public int ConsideredCount =>
        PreparedCount + ReusedCount + AlreadyCoarseCount + FailedCount + MissingCount;
}

/// <summary>
/// One image's share of a preparation pass, decided WITHOUT touching the disk.
///
/// 🔴 THE SPLIT EXISTS SO THE COUNT IS KNOWN BEFORE THE WORK STARTS. «26 images
/// are being prepared, once» cannot be said by a loop that discovers its own size as
/// it goes - and a window that freezes for forty seconds without saying why is
/// indistinguishable from a hang. It is also the seam the work will move across when
/// it leaves the UI thread: the plan is what the album fingerprint is taken from, so
/// it must stay synchronous and cheap, while the encoding need not.
/// </summary>
/// <param name="Image">The record whose path will be repointed.</param>
/// <param name="SourcePath">The payload on this disk.</param>
/// <param name="PreparedPath">Where the prepared copy goes.</param>
/// <param name="PreparedRelativePath">That path as the project names it.</param>
/// <param name="Plan">The size the rule asks for.</param>
/// <param name="AlreadyWritten">A previous pass wrote it; nothing to encode.</param>
internal sealed record VisualizationRasterStep(
    ProjectVisualizationImage Image,
    string SourcePath,
    string PreparedPath,
    string PreparedRelativePath,
    VisualizationRasterPlan Plan,
    bool AlreadyWritten);

/// <summary>
/// Brings every visualisation image down to the album's own density before it is
/// drawn.
///
/// 🔴 THE PROBLEM IN THE OWNER'S WORDS: «харагдах байдлын нэг зураг л гэхэд 16к
/// рендер байгаа». They render at whatever size their tool produces and improve
/// those renders for weeks; the album is what has to stay carriable. The ceiling is
/// STUDIO'S, not the source's - the rule lives in <see cref="AlbumRasterRule"/> and
/// the arithmetic in <see cref="VisualizationRasterBudget"/>; this file only
/// executes them.
///
/// 🔴 PREPARATION CHANGES BYTES AND NEVER THE LAYOUT. The page composition is
/// chosen from each record's PixelWidth/PixelHeight, so writing the prepared sizes
/// back would let preparation move tiles, flip a fit mode across
/// <see cref="VisualizationPageLayoutPlanner.MaximumCropFraction"/>, and thereby
/// change the very frame the size was computed for. Only RelativePath is swapped.
/// The album that comes out is the same album, made of smaller files.
///
/// 🔴 IT WORKS ON THE BUILD SNAPSHOT, WHICH IS A CLONE. The owner's project keeps
/// pointing at their originals - a preparation that rewrote their records would
/// mean their 16k renders were reachable only through Studio's cache.
///
/// 🔴 THE PREPARED FILE IS DERIVED. It is named by (source content hash, needed
/// pixels), so a lost cache is REBUILT rather than read as «content missing», and a
/// re-rendered image gets a new name instead of quietly reusing yesterday's pixels.
/// That is also why it lives in its own folder: the store sweep walks
/// sources/visualizations/images only, and derived files must never be mistaken for
/// the payloads they were derived from.
/// </summary>
internal static class StudioVisualizationRasterPreparer
{
    /// <summary>
    /// Where prepared copies live - beside the payload store, not inside it.
    /// </summary>
    internal static readonly string[] PreparedSegments =
        ["sources", "visualizations", "prepared"];

    /// <summary>
    /// Prepares <paramref name="snapshot"/>'s images in place for the album.
    ///
    /// The snapshot's images are clones, so their RelativePath is repointed at the
    /// prepared copy. Anything that cannot be prepared keeps its original path.
    /// </summary>
    /// <param name="announceWork">
    /// Told how many images are about to be ENCODED, before the first one is, and
    /// only when that number is more than zero.
    ///
    /// 🔴 A SILENT FORTY SECONDS IS A HANG AS FAR AS ANYBODY CAN TELL. The owner's
    /// complaint was «программ ингэтлээ гацаад байвал хэн ч хэрэглэхгүй»; the first
    /// album build after a set of renders pays this once and never again, and saying
    /// so is the difference between a wait and a fault. It cannot be said from inside
    /// the loop - by then the window is already blocked - so the plan is made first
    /// and counted.
    /// </param>
    public static VisualizationRasterPreparation PrepareForAlbum(
        ProjectVisualizationSource? snapshot,
        string? projectPath,
        Action<int>? announceWork = null)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(projectPath))
            return VisualizationRasterPreparation.Nothing;

        string projectFolder;
        string preparedFolder;
        try
        {
            projectFolder = ProjectWorkspacePaths.GetProjectFolder(projectPath);
            preparedFolder = Path.Combine(projectFolder, Path.Combine(PreparedSegments));
        }
        catch (Exception exception) when (IsFileTrouble(exception))
        {
            return VisualizationRasterPreparation.Nothing;
        }

        // 🔴 THE SAME PLANNER THE WRITER USES, ON THE SAME IMAGES. The needed pixel
        // count depends on the FRAME an image lands in, and the frame is chosen by
        // the layout - so a preparer that guessed «the biggest tile» would soften
        // every large tile or bloat every small one. Running the planner here is not
        // a second opinion about the layout: it is the same deterministic function
        // on the same records, and the records are deliberately left untouched so
        // that stays true.
        IReadOnlyList<VisualizationAlbumPagePlan> plans =
            VisualizationPageLayoutPlanner.Create(snapshot, firstPageNumber: 1);

        long startedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var inUse = new List<string>();
        var prepared = 0;
        var reused = 0;
        var alreadyCoarse = 0;
        var failed = 0;
        var missing = 0;
        long bytes = 0;

        // 🔴 PLANNED FIRST, WORKED SECOND. Nothing here touches an image's bytes,
        // so the whole plan - and with it the number of files that will be encoded -
        // is known before the expensive part begins. That is what makes «26 зургийг
        // нэг удаа бэлтгэж байна» sayable, and it is the seam the encoding will move
        // across when it leaves the UI thread.
        var steps = new List<VisualizationRasterStep>();
        foreach (VisualizationAlbumPagePlan page in plans)
        {
            foreach (VisualizationImageTilePlan tile in page.Tiles)
            {
                ProjectVisualizationImage image = tile.Image;
                string? sourcePath = ResolvePayload(projectFolder, image.RelativePath);
                if (sourcePath is null)
                {
                    missing++;
                    continue;
                }

                VisualizationRasterPlan plan = VisualizationRasterBudget.For(
                    image.PixelWidth,
                    image.PixelHeight,
                    tile.Frame,
                    tile.FitMode);
                if (!plan.Resample)
                {
                    alreadyCoarse++;
                    continue;
                }

                try
                {
                    string key = ContentKey(image, sourcePath);
                    string fileName =
                        $"{key}-{plan.PixelWidth}x{plan.PixelHeight}q{AlbumRasterRule.JpegQuality}.jpg";
                    string preparedPath = Path.Combine(preparedFolder, fileName);
                    steps.Add(new VisualizationRasterStep(
                        image,
                        sourcePath,
                        preparedPath,
                        ProjectWorkspacePaths.ToRelativePath(projectPath, preparedPath),
                        plan,
                        File.Exists(preparedPath)));
                }
                catch (Exception exception) when (IsFileTrouble(exception))
                {
                    // Planning can still fail on a path: the hash of a record that has
                    // none is read from the file. Counted as the pass it would have been.
                    failed++;
                }
            }
        }

        int toEncode = steps.Count(step => !step.AlreadyWritten);
        if (toEncode > 0)
            announceWork?.Invoke(toEncode);

        foreach (VisualizationRasterStep step in steps)
        {
            try
            {
                if (!step.AlreadyWritten)
                {
                    Directory.CreateDirectory(preparedFolder);
                    Write(step.SourcePath, step.PreparedPath, step.Plan);
                }

                step.Image.RelativePath = step.PreparedRelativePath;
                inUse.Add(step.PreparedRelativePath);
                bytes += new FileInfo(step.PreparedPath).Length;
                if (step.AlreadyWritten)
                    reused++;
                else
                    prepared++;
            }
            catch (Exception exception) when (IsFileTrouble(exception))
            {
                // 🔴 THE SOURCE PATH IS LEFT IN PLACE ON PURPOSE. The owner gets
                // their image at full size on that one tile - a heavier album, not a
                // missing page and not a failed build.
                failed++;
            }
        }

        // 🔴 SWEPT ONLY WHEN A NEW COPY WAS WRITTEN, BECAUSE THAT IS THE ONLY WAY
        // GARBAGE APPEARS. Nothing in the prepared folder can become unreferenced
        // during a pass that wrote nothing, so the steady state - every copy reused,
        // which is every build after the first - does not even enumerate the folder.
        //
        // ⚠ A FIRST DRAFT OF THIS COMMENT CLAIMED THE GATE ALSO SPARED AN IMAGE
        // TICKED OUT OF THE ALBUM ITS PREPARED COPY. A test disproved it: removing one
        // image RE-LAYS OUT the page, so every remaining image lands in a bigger frame,
        // needs more pixels, and is prepared afresh - which makes the pass a
        // «something new appeared» pass after all, and sweeps the superseded copies
        // including the ticked-out one. That is correct behaviour and it is not free:
        // changing the composition of a 26-image album re-encodes what is still in it.
        // Inherent to sizing per frame, which is the rule; named rather than hidden.
        //
        // ⚠ AND AN IMAGE DELETED FROM THE PROJECT while nothing else changes leaves
        // its prepared copy until the next pass that prepares anything. Disk, not
        // correctness, bounded by the number of images.
        VisualizationStoreSweepResult cacheSweep = prepared > 0
            ? StudioVisualizationStoreMaintenance.SweepFolder(projectPath, preparedFolder, inUse)
            : new VisualizationStoreSweepResult(0, 0, 0, "");

        return new VisualizationRasterPreparation(
            prepared,
            reused,
            alreadyCoarse,
            failed,
            missing,
            bytes,
            (double)(System.Diagnostics.Stopwatch.GetTimestamp() - startedTicks) /
                System.Diagnostics.Stopwatch.Frequency,
            cacheSweep.RemovedCount,
            cacheSweep.RemovedBytes);
    }

    /// <summary>
    /// The cache key: what the image IS, plus what it is needed AT.
    ///
    /// The record's own hash is used when it has one, because that is the value the
    /// rest of the product identifies this payload by. It is computed from the file
    /// only when the record has none - an uninspected record must not silently share
    /// a cache entry with a different picture.
    /// </summary>
    /// <summary>
    /// The cache name for one prepared copy: what the picture IS, and what it is
    /// needed AT.
    ///
    /// 🔴 SHARED WITH THE PORTFOLIO RATHER THAN COPIED. Two writers place the same
    /// files at different sizes, and a second spelling of this name would give the two
    /// caches different opinions about whether a copy already exists - which shows up
    /// as re-encoding on every export, and looks like slowness rather than a bug.
    /// </summary>
    internal static string PreparedFileName(
        string? declaredSha256,
        string sourcePath,
        VisualizationRasterPlan plan)
    {
        string declared = (declaredSha256 ?? "").Trim().ToLowerInvariant();
        string key = declared.Length > 0
            ? declared
            : ProjectVisualizationFileStore.ComputeSha256(sourcePath);
        return $"{key}-{plan.PixelWidth}x{plan.PixelHeight}q{AlbumRasterRule.JpegQuality}.jpg";
    }

    /// <summary>Encodes one prepared copy. Shared, for the reason above.</summary>
    internal static void WritePrepared(
        string sourcePath,
        string preparedPath,
        VisualizationRasterPlan plan) =>
        Write(sourcePath, preparedPath, plan);

    /// <summary>What counts as «this file would not cooperate». Shared.</summary>
    internal static bool IsFileTroubleForSharing(Exception exception) =>
        IsFileTrouble(exception);

    private static string ContentKey(ProjectVisualizationImage image, string sourcePath)
    {
        string declared = (image.Sha256 ?? "").Trim().ToLowerInvariant();
        return declared.Length > 0
            ? declared
            : ProjectVisualizationFileStore.ComputeSha256(sourcePath);
    }

    /// <summary>
    /// Decodes at the needed size and writes one JPEG.
    ///
    /// 🔴 DECODED AT THE TARGET SIZE, NOT SHRUNK AFTERWARDS. DecodePixelWidth makes
    /// the decoder produce the smaller image directly; loading a 16k render in full
    /// first would cost about a gigabyte of pixels per image before any of it was
    /// needed. Only the width is set, so the decoder keeps the aspect ratio - the
    /// plan's two numbers already come from one shared factor.
    /// </summary>
    private static void Write(string sourcePath, string preparedPath, VisualizationRasterPlan plan)
    {
        BitmapSource decoded;
        using (FileStream input = File.OpenRead(sourcePath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = input;
            bitmap.DecodePixelWidth = plan.PixelWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            decoded = bitmap;
        }

        BitmapSource opaque = OnWhite(decoded);
        var encoder = new JpegBitmapEncoder { QualityLevel = AlbumRasterRule.JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(opaque));

        // Written aside and moved into place, so a crash half way through cannot
        // leave a truncated file wearing a name that says «already prepared».
        string temporaryPath = preparedPath + ".partial";
        using (FileStream output = File.Create(temporaryPath))
        {
            encoder.Save(output);
        }

        File.Move(temporaryPath, preparedPath, overwrite: true);
    }

    /// <summary>
    /// Flattens transparency onto white, because JPEG has no alpha channel.
    ///
    /// 🔴 WHITE IS NOT A GUESS - IT IS WHAT THE PAGE ALREADY SHOWS. The writer fills
    /// each tile with a white rectangle before drawing the image into it, so a
    /// transparent region reads as white in today's album. Discarding the alpha
    /// channel instead would hand the encoder whatever colour sat under it, which in
    /// a PNG's fully transparent areas is routinely black - a render would come back
    /// with a black border nobody drew.
    ///
    /// 🔴 THE ALPHA TEST IS DERIVED FROM THE FORMAT, NOT A LIST OF FORMAT NAMES. A
    /// hand-written table of «formats with alpha» is a declaration that rots; the
    /// channel masks are the format describing itself. Alpha was measured absent in
    /// all 26 of the owner's current renders, which is exactly why this path needs a
    /// test rather than a reassuring comment - nothing they have today would reach it.
    /// </summary>
    internal static BitmapSource OnWhite(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Format.Masks.Count <= 3)
            return source;

        var straight = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0d);
        straight.Freeze();

        int width = straight.PixelWidth;
        int height = straight.PixelHeight;
        int sourceStride = width * 4;
        var pixels = new byte[sourceStride * height];
        straight.CopyPixels(pixels, sourceStride, 0);

        int targetStride = width * 3;
        var flattened = new byte[targetStride * height];
        for (var y = 0; y < height; y++)
        {
            int sourceRow = y * sourceStride;
            int targetRow = y * targetStride;
            for (var x = 0; x < width; x++)
            {
                int from = sourceRow + (x * 4);
                int to = targetRow + (x * 3);
                int alpha = pixels[from + 3];
                int inverse = 255 - alpha;

                // Rounded, not truncated: a half-covered pixel over white should
                // land on the nearer value, and truncation darkens every edge.
                flattened[to] = (byte)(((pixels[from] * alpha) + (255 * inverse) + 127) / 255);
                flattened[to + 1] = (byte)(((pixels[from + 1] * alpha) + (255 * inverse) + 127) / 255);
                flattened[to + 2] = (byte)(((pixels[from + 2] * alpha) + (255 * inverse) + 127) / 255);
            }
        }

        BitmapSource result = BitmapSource.Create(
            width,
            height,
            straight.DpiX,
            straight.DpiY,
            System.Windows.Media.PixelFormats.Bgr24,
            null,
            flattened,
            targetStride);
        result.Freeze();
        return result;
    }

    private static string? ResolvePayload(string projectFolder, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        try
        {
            string full = Path.IsPathRooted(relativePath)
                ? Path.GetFullPath(relativePath)
                : Path.GetFullPath(Path.Combine(projectFolder, relativePath));
            return File.Exists(full) ? full : null;
        }
        catch (Exception exception) when (IsFileTrouble(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// What counts as «this file would not cooperate» rather than a defect.
    ///
    /// Kept in one place so the two call sites cannot drift apart: a list that
    /// catches more in the loop than around the folder would turn a per-image
    /// problem into a lost pass.
    /// </summary>
    private static bool IsFileTrouble(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or InvalidDataException or
            System.Runtime.InteropServices.COMException or OverflowException or
            FormatException;

    // 🔴 WHAT IS PROVEN ABOUT THIS LIST, AND WHAT IS NOT. The tests reach two of
    // these by construction: a COMException from a picture the decoder rejects, and an
    // IOException from a file another program holds open - the likeliest of the two in
    // the owner's day. They were reached only because the theory beside them tries
    // SEVERAL kinds of trouble; on one fixture the list looked finished.
    //
    // ⚠ FormatException is here on the strength of the type hierarchy rather than a
    // test: FileFormatException, what WPF documents for a malformed image, derives from
    // FormatException and NOT from NotSupportedException, so it was absent. No fixture
    // here produces one reliably. It is kept because the cost of being wrong is the
    // album build dying over one render, and the cost of being right is a name in a list.
}
