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
    private static readonly Dictionary<string, byte[]> FontData =
        new(StringComparer.OrdinalIgnoreCase);
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
        // ISOCPEUR MON IS SHIPPED WITH STUDIO ON PURPOSE, AND MUST STAY THAT WAY.
        //
        // Windows' own ISOCPEUR - the obvious "why do we carry a font?"
        // replacement - has no glyph for Ө or Ү, the two letters that are
        // Mongolian and nothing else. Measured 2026-09-06 by reading the cmap
        // of all three candidates: this file and Arial cover them, stock
        // ISOCPEUR does not.
        //
        // Swapping to the system font would not fail. It would print every
        // other word correctly and drop those two letters, which is the worst
        // shape a fault can take: complete breakage is noticed at once,
        // partial breakage is noticed after it has been printed and signed.
        ["isocpeur mon#"] = "Fonts/isocpeu_mon_3.ttf",
        ["isocpeur mon#b"] = "Fonts/isocpeu_mon_3.ttf",
        ["isocpeur mon#i"] = "Fonts/isocpeui_mon_3.ttf",
        ["isocpeur mon#bi"] = "Fonts/isocpeui_mon_3.ttf",
    };

    public static void Register()
    {
        lock (Sync)
        {
            if (!registered)
            {
                ValidateRequiredFonts(FontsDirectory);
                GlobalFontSettings.FontResolver = new WindowsFontResolver();
                registered = true;
            }
        }
    }

    public static void ValidateRequiredFonts(string fontsDirectory)
    {
        if (string.IsNullOrWhiteSpace(fontsDirectory))
            throw new InvalidOperationException("Windows fonts directory is unavailable; Arial is required for Studio PDF output.");

        string root = Path.GetFullPath(fontsDirectory);
        string[] requiredFiles = Files
            .Where(item => item.Key.StartsWith("arial#", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] missing = requiredFiles
            .Where(fileName => !File.Exists(Path.Combine(root, fileName)))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Arial font files required for Studio PDF output are missing: {string.Join(", ", missing)}. " +
                $"Expected Windows fonts folder: {root}");
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = MakeFaceKey(familyName, bold, italic);
        if (Files.ContainsKey(face))
        {
            return new FontResolverInfo(face);
        }

        // An unmapped family used to become Arial without a word, and the only
        // trace was that the text looked different. Adding a font name in the
        // drawing code and forgetting the map above is an ordinary slip; having
        // it silently print in another face is not an ordinary consequence.
        //
        // The families asked for here are all named in this file, so an unknown
        // one means the map and the caller have drifted apart. That is a fault
        // to report, not to paper over.
        throw new InvalidOperationException(
            $"PDF font family '{familyName}' is not registered in {nameof(WindowsFontResolver)}. " +
            "Add it to the file map instead of letting it fall back to another face - " +
            "a substituted font changes how the sheet reads and reports nothing.");
    }

    public byte[]? GetFont(string faceName)
    {
        if (!Files.TryGetValue(faceName, out var fileName))
        {
            return null;
        }

        lock (Sync)
        {
            if (FontData.TryGetValue(faceName, out byte[]? cached))
                return cached;

            string? fontPath = ResolveFontPath(fileName);
            if (fontPath is null)
            {
                throw new FileNotFoundException(
                    $"PDF font asset '{fileName}' was not found next to the loaded PDF renderer or in the Windows fonts folder.");
            }

            byte[] data = File.ReadAllBytes(fontPath);
            FontData[faceName] = data;
            return data;
        }
    }

    internal static string? ResolveFontPath(string fileName)
    {
        string normalized = fileName.Replace('/', Path.DirectorySeparatorChar);
        string assemblyDirectory = Path.GetDirectoryName(
            typeof(WindowsFontResolver).Assembly.Location) ?? "";
        string[] candidates =
        [
            Path.Combine(assemblyDirectory, normalized),
            Path.Combine(AppContext.BaseDirectory, normalized),
            Path.Combine(FontsDirectory, normalized),
        ];
        return candidates.FirstOrDefault(File.Exists);
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
