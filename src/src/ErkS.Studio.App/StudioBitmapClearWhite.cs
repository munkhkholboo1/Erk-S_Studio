using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ErkS.Studio;

/// <summary>
/// Makes a rendered page's white paper clear, so what is drawn on it can be
/// laid over another page - the layout editor shows the front matter over the
/// sheet it will print on.
/// </summary>
internal static class StudioBitmapClearWhite
{
    public const byte WhiteThreshold = 246;

    /// <param name="keepOpaque">
    /// Areas, as fractions of the page (0..1), whose white stays paper - the
    /// front-matter blocks, which cover what is under them in the print.
    /// </param>
    public static BitmapSource Apply(BitmapSource source, IEnumerable<System.Windows.Rect>? keepOpaque = null)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        var keep = new bool[width * height];
        foreach (System.Windows.Rect area in keepOpaque ?? [])
        {
            int x0 = Math.Clamp((int)Math.Floor(area.X * width), 0, width);
            int y0 = Math.Clamp((int)Math.Floor(area.Y * height), 0, height);
            int x1 = Math.Clamp((int)Math.Ceiling(area.Right * width), 0, width);
            int y1 = Math.Clamp((int)Math.Ceiling(area.Bottom * height), 0, height);
            for (int y = y0; y < y1; y++)
                Array.Fill(keep, true, y * width + x0, Math.Max(0, x1 - x0));
        }
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (!keep[index / 4] &&
                pixels[index] >= WhiteThreshold &&
                pixels[index + 1] >= WhiteThreshold &&
                pixels[index + 2] >= WhiteThreshold)
            {
                pixels[index + 3] = 0;
            }
        }
        BitmapSource result = BitmapSource.Create(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
