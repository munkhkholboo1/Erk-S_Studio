using PdfSharp.Fonts;

namespace ErkS.Platform.Pdf;

/// <summary>
/// Minimal font resolver for PDFsharp's platform-neutral build: serves the
/// fonts the album composer uses straight from the Windows fonts folder.
/// Call <see cref="Register"/> once before any XFont is created.
/// </summary>
public sealed class WindowsFontResolver : IFontResolver
{
    private static readonly object Sync = new();
    private static bool registered;

    private static readonly string FontsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arial#"] = "arial.ttf",
        ["arial#b"] = "arialbd.ttf",
        ["arial#i"] = "ariali.ttf",
        ["arial#bi"] = "arialbi.ttf",
        ["segoe ui#"] = "segoeui.ttf",
        ["segoe ui#b"] = "segoeuib.ttf",
        ["segoe ui#i"] = "segoeuii.ttf",
        ["segoe ui#bi"] = "segoeuiz.ttf",
    };

    public static void Register()
    {
        lock (Sync)
        {
            if (!registered)
            {
                GlobalFontSettings.FontResolver = new WindowsFontResolver();
                registered = true;
            }
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = MakeFaceKey(familyName, bold, italic);
        if (Files.ContainsKey(face))
        {
            return new FontResolverInfo(face);
        }

        // Unknown family: fall back to Arial with the requested style.
        var fallback = MakeFaceKey("arial", bold, italic);
        return new FontResolverInfo(Files.ContainsKey(fallback) ? fallback : "arial#");
    }

    public byte[]? GetFont(string faceName)
    {
        if (!Files.TryGetValue(faceName, out var fileName))
        {
            return null;
        }

        var path = Path.Combine(FontsDirectory, fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static string MakeFaceKey(string familyName, bool bold, bool italic)
    {
        var style = (bold, italic) switch
        {
            (true, true) => "bi",
            (true, false) => "b",
            (false, true) => "i",
            _ => "",
        };
        return $"{familyName.Trim().ToLowerInvariant()}#{style}";
    }
}
