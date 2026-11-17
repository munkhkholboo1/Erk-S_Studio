using System.Buffers.Binary;
using System.IO.Compression;

namespace ErkS.Platform.Pdf;

/// <summary>
/// The transparency of a PNG, read by hand: PDFsharp's core build imports a
/// PNG's colours and drops its alpha channel. Only the forms Studio keeps a
/// signature in - 8-bit, not interlaced, with alpha (RGBA or grey + alpha) -
/// are read; anything else answers null and the picture stays opaque.
/// </summary>
internal static class PngAlpha
{
    public static (int Width, int Height)? Size(byte[]? png)
    {
        if (png is null || png.Length < 33 || png[12] != (byte)'I' || png[13] != (byte)'H')
            return null;
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    public static (int Width, int Height, byte[] Values)? Read(byte[]? png)
    {
        if (Size(png) is not { } size)
            return null;
        byte bitDepth = png![24];
        byte colorType = png[25];
        byte interlace = png[28];
        int channels = colorType switch { 6 => 4, 4 => 2, _ => 0 };
        if (bitDepth != 8 || interlace != 0 || channels == 0 || size.Width <= 0 || size.Height <= 0)
            return null;

        using var idat = new MemoryStream();
        int offset = 8;
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (length < 0 || offset + 12 + length > png.Length)
                return null;
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
                idat.Write(png, offset + 8, length);
            if (type == "IEND")
                break;
            offset += 12 + length;
        }

        byte[] raw;
        try
        {
            idat.Position = 0;
            using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflate.CopyTo(output);
            raw = output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }

        int stride = size.Width * channels;
        if (raw.Length < (stride + 1) * size.Height)
            return null;
        byte[] previous = new byte[stride];
        byte[] current = new byte[stride];
        byte[] alpha = new byte[size.Width * size.Height];
        for (int y = 0; y < size.Height; y++)
        {
            int row = y * (stride + 1);
            byte filter = raw[row];
            Array.Copy(raw, row + 1, current, 0, stride);
            for (int x = 0; x < stride; x++)
            {
                int left = x >= channels ? current[x - channels] : 0;
                int up = previous[x];
                int upLeft = x >= channels ? previous[x - channels] : 0;
                int value = filter switch
                {
                    0 => current[x],
                    1 => current[x] + left,
                    2 => current[x] + up,
                    3 => current[x] + ((left + up) >> 1),
                    4 => current[x] + Paeth(left, up, upLeft),
                    _ => -1,
                };
                if (value < 0)
                    return null;
                current[x] = (byte)value;
            }
            for (int x = 0; x < size.Width; x++)
                alpha[y * size.Width + x] = current[x * channels + channels - 1];
            (previous, current) = (current, previous);
        }
        return (size.Width, size.Height, alpha);
    }

    public static byte[] Deflate(byte[] values)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(values);
        return output.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
