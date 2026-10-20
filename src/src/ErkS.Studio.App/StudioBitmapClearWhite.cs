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

    public static BitmapSource Apply(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] >= WhiteThreshold &&
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
