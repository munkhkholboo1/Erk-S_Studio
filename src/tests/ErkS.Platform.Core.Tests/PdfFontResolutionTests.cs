using System.Text;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Two ways the wrong letters can reach a printed sheet without anything
/// saying so.
///
/// The first is substitution: an unmapped family name used to resolve to Arial
/// silently, so adding a font in the drawing code and forgetting the map here
/// changed how the sheet read and reported nothing.
///
/// The second is subtler and is why Studio carries a font at all. Windows' own
/// ISOCPEUR has no glyph for Ө or Ү - the two letters that are Mongolian and
/// nothing else - so "simplify this, use the system font" would print every
/// other word correctly and drop those two. Complete breakage is noticed at
/// once; partial breakage is noticed after the album has been printed.
/// </summary>
public sealed class PdfFontResolutionTests
{
    [Theory]
    [InlineData("Arial")]
    [InlineData("Segoe UI")]
    [InlineData("ISOCPEUR MON")]
    public void EveryFamilyTheWriterUsesResolves(string family)
    {
        var resolver = new WindowsFontResolver();

        Assert.NotNull(resolver.ResolveTypeface(family, bold: false, italic: false));
        Assert.NotNull(resolver.ResolveTypeface(family, bold: true, italic: false));
    }

    [Fact]
    public void AnUnmappedFamilyIsREFUSED_NotQuietlyReplaced()
    {
        // The whole point: the failure must name itself. Falling back leaves a
        // sheet that is wrong in a way only a reader who knows the intended
        // font could ever spot.
        var resolver = new WindowsFontResolver();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => resolver.ResolveTypeface("Comic Sans MS", bold: false, italic: false));

        Assert.Contains("Comic Sans MS", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSHIPPEDTechnicalFontCoversTheMongolianOnlyLetters()
    {
        // Guards the swap that looks like a cleanup. If someone repoints this
        // entry at Windows' ISOCPEUR, Ө and Ү stop existing and everything else
        // keeps working.
        // Asked through the RESOLVER, not through a path written here: a first
        // attempt read the shipped file directly, so repointing the map at
        // Windows' ISOCPEUR left the test green while the writer loaded a font
        // without Ө. The bytes checked have to be the bytes drawn with.
        var resolver = new WindowsFontResolver();
        byte[]? font = resolver.GetFont("isocpeur mon#");
        Assert.True(font is { Length: > 0 }, "the technical font could not be loaded through the resolver");

        Assert.All(
            new[] { 'Ө', 'Ү', 'Ж', 'Ц', 'Б', 'Г', 'Д' },
            letter => Assert.True(
                HasGlyph(font!, letter),
                $"'{letter}' is missing from the font the writer would use"));
    }

    /// <summary>
    /// Reads the font's own character map. Asking the font rather than trusting
    /// its name is the difference between this test and a comment.
    /// </summary>
    private static bool HasGlyph(byte[] data, char character)
    {
        int tableCount = ReadUInt16(data, 4);
        int cmapOffset = -1;
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (Encoding.ASCII.GetString(data, record, 4) == "cmap")
                cmapOffset = (int)ReadUInt32(data, record + 8);
        }

        Assert.True(cmapOffset > 0, "the font has no character map at all");

        int subtableCount = ReadUInt16(data, cmapOffset + 2);
        int unicodeSubtable = -1;
        for (int i = 0; i < subtableCount; i++)
        {
            int entry = cmapOffset + 4 + i * 8;
            int platform = ReadUInt16(data, entry);
            int encoding = ReadUInt16(data, entry + 2);
            if ((platform == 3 && (encoding == 1 || encoding == 10)) ||
                (platform == 0 && (encoding == 3 || encoding == 4)))
            {
                unicodeSubtable = cmapOffset + (int)ReadUInt32(data, entry + 4);
            }
        }

        Assert.True(unicodeSubtable > 0, "the font has no Unicode character map");
        Assert.Equal(4, ReadUInt16(data, unicodeSubtable));

        int segments = ReadUInt16(data, unicodeSubtable + 6) / 2;
        int endsAt = unicodeSubtable + 14;
        int startsAt = endsAt + segments * 2 + 2;
        int deltasAt = startsAt + segments * 2;
        int rangeOffsetsAt = deltasAt + segments * 2;
        for (int i = 0; i < segments; i++)
        {
            int end = ReadUInt16(data, endsAt + i * 2);
            int start = ReadUInt16(data, startsAt + i * 2);
            if (character < start || character > end)
                continue;

            int rangeOffset = ReadUInt16(data, rangeOffsetsAt + i * 2);
            if (rangeOffset == 0)
                return ((character + ReadInt16(data, deltasAt + i * 2)) & 0xFFFF) != 0;

            int glyphAt = rangeOffsetsAt + i * 2 + rangeOffset + (character - start) * 2;
            return ReadUInt16(data, glyphAt) != 0;
        }

        return false;
    }

    private static int ReadUInt16(byte[] data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static short ReadInt16(byte[] data, int offset) =>
        (short)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];
}
