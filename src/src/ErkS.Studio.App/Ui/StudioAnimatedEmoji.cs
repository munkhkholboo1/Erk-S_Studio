using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ErkS.Studio;

internal readonly record struct StudioEmojiSegment(string Text, bool IsEmoji);

internal static class StudioEmojiCatalog
{
    private static readonly IReadOnlyDictionary<string, string> AssetByEmoji =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["\U0001F525"] = "fluent-fire-animated.png",
            ["\U0001F44D"] = "fluent-thumbs-up-animated.png",
            ["\u2705"] = "fluent-check-mark-button.png",
            ["\U0001F64C"] = "fluent-raising-hands-animated.png",
            ["\U0001F44F"] = "fluent-clapping-hands-animated.png",
            ["\U0001F602"] = "fluent-face-tears-joy-animated.png",
            ["\U0001F60A"] = "fluent-smiling-eyes-animated.png",
            ["\U0001F60E"] = "fluent-sunglasses-animated.png",
            ["\u2764\uFE0F"] = "fluent-red-heart-animated.png",
            ["\u2764"] = "fluent-red-heart-animated.png",
            ["\U0001F60D"] = "fluent-heart-eyes-animated.png",
            ["\U0001F914"] = "fluent-thinking-face-animated.png",
            ["\U0001F44C"] = "fluent-ok-hand-animated.png",
            ["\U0001F440"] = "fluent-eyes-animated.png",
            ["\U0001F389"] = "fluent-party-popper-animated.png",
            ["\U0001F4AF"] = "fluent-hundred-points-animated.png",
            ["\U0001F91D"] = "fluent-handshake-animated.png",
            ["\U0001F64F"] = "fluent-folded-hands-animated.png",
            ["\u2728"] = "fluent-glowing-star-animated.png",
            ["\U0001F4AA"] = "fluent-flexed-biceps-animated.png",
            ["\U0001F680"] = "fluent-rocket-animated.png",
        };

    private static readonly string[] OrderedTokens =
        AssetByEmoji.Keys
            .Append(":erks:")
            .OrderByDescending(item => item.Length)
            .ToArray();

    public static IReadOnlyList<string> Choices { get; } =
    [
        "\U0001F525",
        ":erks:",
        "\U0001F44D",
        "\u2705",
        "\U0001F64C",
        "\U0001F44F",
        "\U0001F602",
        "\U0001F60A",
        "\U0001F60E",
        "\u2764\uFE0F",
        "\U0001F60D",
        "\U0001F914",
        "\U0001F44C",
        "\U0001F440",
        "\U0001F389",
        "\U0001F4AF",
        "\U0001F91D",
        "\U0001F64F",
        "\u2728",
        "\U0001F4AA",
        "\U0001F680",
    ];

    public static bool TryGetAssetPath(string emoji, out string path)
    {
        if (AssetByEmoji.TryGetValue(emoji, out string? fileName))
        {
            path = StudioWidgets.GetAssetPath(fileName);
            return true;
        }

        path = "";
        return false;
    }

    public static IReadOnlyList<StudioEmojiSegment> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var result = new List<StudioEmojiSegment>();
        var plain = new StringBuilder();
        int index = 0;
        while (index < text.Length)
        {
            string? match = OrderedTokens.FirstOrDefault(token =>
                text.AsSpan(index).StartsWith(
                    token,
                    token.Equals(":erks:", StringComparison.Ordinal)
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal));
            if (match is null)
            {
                plain.Append(text[index]);
                index++;
                continue;
            }

            if (plain.Length > 0)
            {
                result.Add(new StudioEmojiSegment(plain.ToString(), false));
                plain.Clear();
            }

            result.Add(new StudioEmojiSegment(match, true));
            index += match.Length;
        }

        if (plain.Length > 0)
            result.Add(new StudioEmojiSegment(plain.ToString(), false));
        return result;
    }
}

internal static class StudioEmojiPresenter
{
    public static FrameworkElement Create(string emoji, double size)
    {
        if (emoji.Equals(":erks:", StringComparison.OrdinalIgnoreCase))
        {
            return new Image
            {
                Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
            };
        }

        if (StudioEmojiCatalog.TryGetAssetPath(emoji, out string path))
            return new StudioAnimatedEmojiImage(path, emoji, size);

        return new TextBlock
        {
            Text = emoji,
            FontSize = Math.Max(12, size * 0.82),
            Foreground = StudioTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static FrameworkElement CreateWithCount(string emoji, int count, double emojiSize)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(Create(emoji, emojiSize));
        content.Children.Add(new TextBlock
        {
            Text = count.ToString(),
            Foreground = StudioTheme.TextBrush,
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return content;
    }
}

internal sealed class StudioAnimatedEmojiImage : Image
{
    private readonly string assetPath;
    private readonly string fallbackEmoji;
    private DispatcherTimer? timer;
    private StudioAnimatedPngSequence? sequence;
    private int frameIndex;
    private bool loading;

    public StudioAnimatedEmojiImage(string assetPath, string fallbackEmoji, double size)
    {
        this.assetPath = assetPath;
        this.fallbackEmoji = fallbackEmoji;
        Width = size;
        Height = size;
        Stretch = Stretch.Uniform;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (sequence is not null)
        {
            ShowCurrentFrame();
            StartTimer();
            return;
        }

        if (loading)
            return;

        loading = true;
        try
        {
            sequence = await StudioAnimatedPngCache.LoadAsync(assetPath);
            if (!IsLoaded || sequence is null || sequence.Frames.Count == 0)
                return;

            frameIndex = 0;
            ShowCurrentFrame();
            StartTimer();
        }
        catch
        {
            ToolTip = fallbackEmoji;
        }
        finally
        {
            loading = false;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        timer?.Stop();
    }

    private void StartTimer()
    {
        if (sequence is null || sequence.Frames.Count <= 1)
            return;

        timer ??= new DispatcherTimer(DispatcherPriority.Render, Dispatcher);
        timer.Stop();
        timer.Interval = ClampDelay(sequence.Delays[frameIndex]);
        timer.Tick -= OnTimerTick;
        timer.Tick += OnTimerTick;
        timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs args)
    {
        if (sequence is null || sequence.Frames.Count <= 1)
            return;

        frameIndex = (frameIndex + 1) % sequence.Frames.Count;
        ShowCurrentFrame();
        if (timer is not null)
            timer.Interval = ClampDelay(sequence.Delays[frameIndex]);
    }

    private void ShowCurrentFrame()
    {
        if (sequence is not null && frameIndex < sequence.Frames.Count)
            Source = sequence.Frames[frameIndex];
    }

    private static TimeSpan ClampDelay(TimeSpan delay)
    {
        double milliseconds = Math.Clamp(delay.TotalMilliseconds, 20, 5000);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}

internal sealed record StudioAnimatedPngSequence(
    IReadOnlyList<BitmapSource> Frames,
    IReadOnlyList<TimeSpan> Delays);

internal static class StudioAnimatedPngCache
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<StudioAnimatedPngSequence?>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<StudioAnimatedPngSequence?> LoadAsync(string path)
    {
        return Cache.GetOrAdd(
            path,
            key => new Lazy<Task<StudioAnimatedPngSequence?>>(
                () => Task.Run(() => StudioAnimatedPngDecoder.DecodeFile(key)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}

internal static class StudioAnimatedPngDecoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static StudioAnimatedPngSequence? DecodeFile(string path)
    {
        if (!File.Exists(path))
            return null;
        return Decode(File.ReadAllBytes(path));
    }

    public static StudioAnimatedPngSequence Decode(byte[] png)
    {
        if (png.Length < PngSignature.Length ||
            !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("Animated emoji is not a PNG file.");
        }

        byte[]? ihdr = null;
        int canvasWidth = 0;
        int canvasHeight = 0;
        bool animated = false;
        bool imageDataSeen = false;
        var headerChunks = new List<PngChunk>();
        var frames = new List<ApngFrame>();
        ApngFrame? currentFrame = null;

        int offset = PngSignature.Length;
        while (offset + 12 <= png.Length)
        {
            int length = checked((int)ReadUInt32(png, offset));
            if (length < 0 || offset + 12 + length > png.Length)
                throw new InvalidDataException("PNG chunk length is invalid.");

            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            byte[] data = png.AsSpan(offset + 8, length).ToArray();
            offset += length + 12;

            switch (type)
            {
                case "IHDR":
                    ihdr = data;
                    canvasWidth = checked((int)ReadUInt32(data, 0));
                    canvasHeight = checked((int)ReadUInt32(data, 4));
                    break;
                case "acTL":
                    animated = true;
                    break;
                case "fcTL":
                    if (currentFrame is not null)
                        frames.Add(currentFrame);
                    currentFrame = ParseFrameControl(data);
                    break;
                case "IDAT":
                    imageDataSeen = true;
                    currentFrame?.ImageData.Add(data);
                    break;
                case "fdAT":
                    imageDataSeen = true;
                    if (currentFrame is null || data.Length < 4)
                        throw new InvalidDataException("APNG frame data has no frame control.");
                    currentFrame.ImageData.Add(data.AsSpan(4).ToArray());
                    break;
                case "IEND":
                    if (currentFrame is not null)
                        frames.Add(currentFrame);
                    offset = png.Length;
                    break;
                default:
                    if (!imageDataSeen && type is not "acTL" and not "fcTL")
                        headerChunks.Add(new PngChunk(type, data));
                    break;
            }
        }

        if (ihdr is null || canvasWidth <= 0 || canvasHeight <= 0)
            throw new InvalidDataException("PNG header is missing.");

        if (!animated || frames.Count == 0)
        {
            BitmapSource single = DecodePng(png);
            return new StudioAnimatedPngSequence(
                [single],
                [TimeSpan.FromMilliseconds(100)]);
        }

        byte[] canvas = new byte[checked(canvasWidth * canvasHeight * 4)];
        var renderedFrames = new List<BitmapSource>(frames.Count);
        var delays = new List<TimeSpan>(frames.Count);
        foreach (ApngFrame frame in frames)
        {
            ValidateFrame(frame, canvasWidth, canvasHeight);
            byte[]? previous = frame.DisposeOperation == 2 ? canvas.ToArray() : null;
            byte[] framePng = BuildFramePng(ihdr, headerChunks, frame);
            BitmapSource decoded = DecodePng(framePng);
            byte[] source = CopyPbgraPixels(decoded, frame.Width, frame.Height);
            CompositeFrame(canvas, canvasWidth, source, frame);

            BitmapSource output = BitmapSource.Create(
                canvasWidth,
                canvasHeight,
                decoded.DpiX > 0 ? decoded.DpiX : 96,
                decoded.DpiY > 0 ? decoded.DpiY : 96,
                PixelFormats.Pbgra32,
                null,
                canvas.ToArray(),
                canvasWidth * 4);
            output.Freeze();
            renderedFrames.Add(output);
            delays.Add(frame.Delay);

            if (frame.DisposeOperation == 1)
                ClearFrame(canvas, canvasWidth, frame);
            else if (frame.DisposeOperation == 2 && previous is not null)
                canvas = previous;
        }

        return new StudioAnimatedPngSequence(renderedFrames, delays);
    }

    private static ApngFrame ParseFrameControl(byte[] data)
    {
        if (data.Length != 26)
            throw new InvalidDataException("APNG frame control length is invalid.");

        int width = checked((int)ReadUInt32(data, 4));
        int height = checked((int)ReadUInt32(data, 8));
        int x = checked((int)ReadUInt32(data, 12));
        int y = checked((int)ReadUInt32(data, 16));
        ushort delayNumerator = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(20, 2));
        ushort delayDenominator = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(22, 2));
        double delayMilliseconds = delayNumerator * 1000.0 / (delayDenominator == 0 ? 100 : delayDenominator);
        if (delayMilliseconds <= 0)
            delayMilliseconds = 10;

        return new ApngFrame(
            width,
            height,
            x,
            y,
            TimeSpan.FromMilliseconds(delayMilliseconds),
            data[24],
            data[25]);
    }

    private static void ValidateFrame(ApngFrame frame, int canvasWidth, int canvasHeight)
    {
        if (frame.Width <= 0 ||
            frame.Height <= 0 ||
            frame.X < 0 ||
            frame.Y < 0 ||
            frame.X + frame.Width > canvasWidth ||
            frame.Y + frame.Height > canvasHeight ||
            frame.DisposeOperation > 2 ||
            frame.BlendOperation > 1 ||
            frame.ImageData.Count == 0)
        {
            throw new InvalidDataException("APNG frame bounds or operations are invalid.");
        }
    }

    private static byte[] BuildFramePng(
        byte[] originalIhdr,
        IReadOnlyList<PngChunk> headerChunks,
        ApngFrame frame)
    {
        using var output = new MemoryStream();
        output.Write(PngSignature);
        byte[] ihdr = originalIhdr.ToArray();
        WriteUInt32(ihdr, 0, checked((uint)frame.Width));
        WriteUInt32(ihdr, 4, checked((uint)frame.Height));
        WriteChunk(output, "IHDR", ihdr);
        foreach (PngChunk chunk in headerChunks)
            WriteChunk(output, chunk.Type, chunk.Data);
        foreach (byte[] imageData in frame.ImageData)
            WriteChunk(output, "IDAT", imageData);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static BitmapSource DecodePng(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static byte[] CopyPbgraPixels(BitmapSource source, int width, int height)
    {
        BitmapSource converted = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        if (converted.CanFreeze)
            converted.Freeze();
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static void CompositeFrame(
        byte[] canvas,
        int canvasWidth,
        byte[] source,
        ApngFrame frame)
    {
        int sourceStride = frame.Width * 4;
        int canvasStride = canvasWidth * 4;
        for (int row = 0; row < frame.Height; row++)
        {
            int sourceOffset = row * sourceStride;
            int targetOffset = ((frame.Y + row) * canvasStride) + (frame.X * 4);
            if (frame.BlendOperation == 0)
            {
                Buffer.BlockCopy(source, sourceOffset, canvas, targetOffset, sourceStride);
                continue;
            }

            for (int column = 0; column < frame.Width; column++)
            {
                int sourcePixel = sourceOffset + (column * 4);
                int targetPixel = targetOffset + (column * 4);
                int sourceAlpha = source[sourcePixel + 3];
                int inverseAlpha = 255 - sourceAlpha;
                canvas[targetPixel] = Blend(source[sourcePixel], canvas[targetPixel], inverseAlpha);
                canvas[targetPixel + 1] = Blend(source[sourcePixel + 1], canvas[targetPixel + 1], inverseAlpha);
                canvas[targetPixel + 2] = Blend(source[sourcePixel + 2], canvas[targetPixel + 2], inverseAlpha);
                canvas[targetPixel + 3] = Blend(
                    source[sourcePixel + 3],
                    canvas[targetPixel + 3],
                    inverseAlpha);
            }
        }
    }

    private static byte Blend(int source, int destination, int inverseAlpha)
    {
        return checked((byte)Math.Min(255, source + ((destination * inverseAlpha + 127) / 255)));
    }

    private static void ClearFrame(byte[] canvas, int canvasWidth, ApngFrame frame)
    {
        int canvasStride = canvasWidth * 4;
        int clearLength = frame.Width * 4;
        for (int row = 0; row < frame.Height; row++)
        {
            int targetOffset = ((frame.Y + row) * canvasStride) + (frame.X * 4);
            Array.Clear(canvas, targetOffset, clearLength);
        }
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte value in typeBytes)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (byte value in data)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        crc ^= 0xFFFFFFFF;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }

    private sealed record PngChunk(string Type, byte[] Data);

    private sealed record ApngFrame(
        int Width,
        int Height,
        int X,
        int Y,
        TimeSpan Delay,
        byte DisposeOperation,
        byte BlendOperation)
    {
        public List<byte[]> ImageData { get; } = [];
    }
}
