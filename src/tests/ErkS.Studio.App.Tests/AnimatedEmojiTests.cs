namespace ErkS.Studio.App.Tests;

public sealed class AnimatedEmojiTests
{
    [Fact]
    public void CanonicalPickerChoicesResolveToPackagedAssets()
    {
        Assert.Equal(21, StudioEmojiCatalog.Choices.Count);
        Assert.Equal("\U0001F525", StudioEmojiCatalog.Choices[0]);
        Assert.Equal(":erks:", StudioEmojiCatalog.Choices[1]);

        foreach (string emoji in StudioEmojiCatalog.Choices.Where(item => item != ":erks:"))
        {
            Assert.True(StudioEmojiCatalog.TryGetAssetPath(emoji, out string path));
            Assert.True(File.Exists(path), $"Missing animated emoji asset: {path}");
        }
    }

    [Fact]
    public void TokenizerPreservesTextAndFindsEmbeddedEmoji()
    {
        IReadOnlyList<StudioEmojiSegment> segments =
            StudioEmojiCatalog.Tokenize("OK \U0001F525 :erks: done");

        Assert.Collection(
            segments,
            item =>
            {
                Assert.False(item.IsEmoji);
                Assert.Equal("OK ", item.Text);
            },
            item =>
            {
                Assert.True(item.IsEmoji);
                Assert.Equal("\U0001F525", item.Text);
            },
            item =>
            {
                Assert.False(item.IsEmoji);
                Assert.Equal(" ", item.Text);
            },
            item =>
            {
                Assert.True(item.IsEmoji);
                Assert.Equal(":erks:", item.Text);
            },
            item =>
            {
                Assert.False(item.IsEmoji);
                Assert.Equal(" done", item.Text);
            });
    }

    [Fact]
    public void FluentFireAssetDecodesAsAnimation()
    {
        Assert.True(
            StudioEmojiCatalog.TryGetAssetPath("\U0001F525", out string path));

        StudioAnimatedPngSequence sequence =
            StudioAnimatedPngDecoder.DecodeFile(path)
            ?? throw new InvalidOperationException("Animated fire asset was not decoded.");

        Assert.True(sequence.Frames.Count > 1);
        Assert.Equal(sequence.Frames.Count, sequence.Delays.Count);
        Assert.All(sequence.Frames, frame =>
        {
            Assert.Equal(96, frame.PixelWidth);
            Assert.Equal(96, frame.PixelHeight);
        });
        Assert.All(sequence.Delays, delay => Assert.True(delay > TimeSpan.Zero));
    }
}
