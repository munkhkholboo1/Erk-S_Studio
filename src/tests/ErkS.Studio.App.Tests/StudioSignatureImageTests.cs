using System.IO;
using ErkS.Platform.Pdf;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The signed-in person's prepared signature (owner, 2026-11-18): it signs only
/// their own name, a one-word name signs nothing, and the device copy is the
/// holder's alone - encrypted for this Windows user, named by a hash, not the address.
/// </summary>
public sealed class StudioSignatureImageTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "erks-sig-app-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void ASignatureSignsTheHoldersNameAsTheCornerTablePrintsIt()
    {
        IReadOnlyList<string> names = StudioSignatureImage.NamesOf("Энхбаатар", "Мөнххолбоо", "");
        var signature = new TitleBlockSignature(names, [1]);

        Assert.True(signature.Signs("Э.Мөнххолбоо"));
        Assert.True(signature.Signs("Энхбаатар Мөнххолбоо"));
        Assert.False(signature.Signs("М.Баасанжав"));
    }

    [Fact]
    public void AOneWordNameSignsNothing()
    {
        Assert.Empty(StudioSignatureImage.NamesOf("", "", "Мөнххолбоо"));
        Assert.Empty(StudioSignatureImage.NamesOf("", "", "someone@example.test"));
        Assert.Equal(["Э.Мөнххолбоо"], StudioSignatureImage.NamesOf("", "", "Энхбаатар Мөнххолбоо"));
    }

    [Fact]
    public void OnlyAReasonablePngIsKept()
    {
        Assert.Null(StudioSignatureImage.Validate(Png(600, 200)));
        Assert.NotNull(StudioSignatureImage.Validate([]));
        Assert.NotNull(StudioSignatureImage.Validate("not a png, not at all, padding padding"u8));
        Assert.NotNull(StudioSignatureImage.Validate(Png(9000, 200)));
    }

    [Fact]
    public void TheDeviceCopyIsEncryptedAndNamedByAHash()
    {
        byte[] png = Png(600, 200);
        string path = StudioSignatureImage.CachePath(folder, "Owner@Example.test");

        StudioSignatureImage.WriteCache(path, png);

        Assert.DoesNotContain("example", Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(path, StudioSignatureImage.CachePath(folder, "owner@example.test "));
        Assert.NotEqual(png, File.ReadAllBytes(path));
        Assert.Equal(png, StudioSignatureImage.ReadCache(path));

        StudioSignatureImage.WriteCache(path, null);
        Assert.False(File.Exists(path));
        Assert.Null(StudioSignatureImage.ReadCache(path));
    }

    [Fact]
    public void AScanOnWhitePaperBecomesATransparentTrimmedStroke()
    {
        // 400 x 200 white paper with a dark bar in the middle.
        int width = 400, height = 200, stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int x = i / 4 % width, y = i / 4 / width;
            bool ink = x >= 100 && x < 300 && y >= 90 && y < 110;
            byte v = ink ? (byte)30 : (byte)250;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }
        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);

        byte[] normalized = StudioSignatureImage.Normalize(stream.ToArray());

        Assert.Null(StudioSignatureImage.Validate(normalized));
        Assert.Equal(6, normalized[25]);   // RGBA
        Assert.Equal(0, normalized[28]);   // not interlaced
        (int w, int h) = (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(normalized.AsSpan(16)),
                          System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(normalized.AsSpan(20)));
        Assert.InRange(w, 200, 206);
        Assert.InRange(h, 20, 26);
    }

    [Fact]
    public void THEChosenHeightTravelsInsideThePngAndTheImageStaysReadable()
    {
        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            4, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4 * 4 * 2], 16);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        byte[] png = stream.ToArray();

        Assert.Null(StudioSignatureImage.ReadHeightMm(png));
        byte[] nine = StudioSignatureImage.WithHeightMm(png, 9);
        byte[] eleven = StudioSignatureImage.WithHeightMm(nine, 11.5);

        Assert.Equal(9, StudioSignatureImage.ReadHeightMm(nine));
        Assert.Equal(11.5, StudioSignatureImage.ReadHeightMm(eleven));
        Assert.Null(StudioSignatureImage.ReadHeightMm(StudioSignatureImage.WithHeightMm(eleven, null)));
        Assert.Null(StudioSignatureImage.Validate(eleven));
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            new MemoryStream(eleven),
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        Assert.Equal(4, decoder.Frames[0].PixelWidth);
    }

    private static byte[] Png(uint width, uint height)
    {
        byte[] bytes = new byte[40];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }
}
