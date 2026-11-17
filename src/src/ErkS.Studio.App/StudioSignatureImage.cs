using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

/// <summary>
/// The signed-in person's prepared signature: a PNG kept with their account on
/// the server, and a copy on this device so an album built offline is signed
/// too (owner, 2026-11-18: «бэлдсэн гарын үсэг миний бүртгэлд хадгалагдана.
/// хийсэн альбум дээр миний нэртэй хэсэг байвал гарын үсэг байршдаг болгоорой»).
/// </summary>
/// <remarks>
/// The device copy is encrypted for this Windows user (DPAPI) and named by a
/// hash of the account, never by the address itself. A signature is placed only
/// beside its holder's own name, only in the album shown on the holder's device.
/// </remarks>
internal static class StudioSignatureImage
{
    public const long MaxBytes = 1024L * 1024L;
    public const int MaxSidePixels = 4000;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ErkS.Studio.Signature.v1");

    /// <summary>The reason a file is refused, or null when it is a signature PNG we keep.</summary>
    public static string? Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return "Гарын үсгийн файл хоосон байна.";
        if (bytes.Length > MaxBytes)
            return "Гарын үсгийн PNG 1 MB-аас их байна.";
        ReadOnlySpan<byte> magic = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(magic) ||
            bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
        {
            return "Гарын үсэг PNG файл байх ёстой.";
        }
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        if (width == 0 || height == 0 || width > MaxSidePixels || height > MaxSidePixels)
            return $"Гарын үсгийн зургийн хэмжээ 1–{MaxSidePixels} пиксел байх ёстой.";
        return null;
    }

    /// <summary>
    /// The PNG as Studio keeps it: 8-bit RGBA, not interlaced (the form the PDF
    /// stamper reads the transparency of), a white scan made transparent, the
    /// empty margin cut away so the stroke fills the cell, at most 1200 px wide.
    /// </summary>
    public static byte[] Normalize(byte[] source)
    {
        BitmapSource decoded;
        using (var stream = new MemoryStream(source, writable: false))
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            decoded = decoder.Frames[0];
        }
        var bgra = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        // A scan or a photo of paper has no transparency: its paper becomes clear.
        bool opaque = true;
        for (int i = 3; i < pixels.Length && opaque; i += 4)
            opaque = pixels[i] == 255;
        if (opaque)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int luminance = (pixels[i] * 114 + pixels[i + 1] * 587 + pixels[i + 2] * 299) / 1000;
                if (luminance >= 235)
                    pixels[i + 3] = 0;
                else if (luminance > 180)
                    pixels[i + 3] = (byte)(255 * (235 - luminance) / 55);
            }
        }

        // The margin with no ink is cut away.
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * stride + x * 4 + 3] <= 8)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < left || bottom < top)
            throw new InvalidDataException("Гарын үсгийн зурагт зураас олдсонгүй (бүхэлдээ хоосон эсвэл цагаан).");
        int pad = 2;
        left = Math.Max(0, left - pad);
        top = Math.Max(0, top - pad);
        right = Math.Min(width - 1, right + pad);
        bottom = Math.Min(height - 1, bottom + pad);

        BitmapSource result = new CroppedBitmap(
            BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride),
            new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        if (result.PixelWidth > 1200)
        {
            double scale = 1200d / result.PixelWidth;
            result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
        }
        result = new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0);

        var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(result));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>
    /// The names a person's signature signs: their own name as a corner table
    /// prints it («Э.Мөнххолбоо»), and written out in full.
    /// </summary>
    public static IReadOnlyList<string> NamesOf(string? familyName, string? givenName, string? displayName)
    {
        var names = new List<string>();
        void Add(string? name)
        {
            string value = (name ?? "").Trim();
            if (value.Length > 0 && !value.Contains('@') &&
                !names.Any(existing => TitleBlockSignature.Normalize(existing) == TitleBlockSignature.Normalize(value)))
            {
                names.Add(value);
            }
        }
        if (!string.IsNullOrWhiteSpace(familyName) && !string.IsNullOrWhiteSpace(givenName))
        {
            Add(MongolianPersonNameFormatter.ForDocument(familyName, givenName));
            Add(MongolianPersonNameFormatter.ForDisplay(familyName, givenName));
        }
        else
        {
            // A one-word name signs nothing: «Мөнххолбоо» alone could be anybody.
            string document = MongolianPersonNameFormatter.ForDocument(displayName);
            if (document.Contains('.') || document.Contains(' '))
                Add(document);
        }
        return names;
    }

    public static string CachePath(string folder, string email) =>
        Path.Combine(folder, Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes((email ?? "").Trim().ToLowerInvariant())))[..24].ToLowerInvariant() + ".sig");

    public static void WriteCache(string path, byte[]? png)
    {
        if (png is null || png.Length == 0)
        {
            TryDelete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] protectedBytes = ProtectedData.Protect(png, Entropy, DataProtectionScope.CurrentUser);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, path, overwrite: true);
    }

    public static byte[]? ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            byte[] png = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Validate(png) is null ? png : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
