using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Leaving bot state now clears the local seat FIRST, so an unreachable server
/// can no longer lock a machine in. The price is that the seat can stay
/// occupied server-side with nothing on the device pointing at it - which is the
/// same defect one layer over. These pin the note that keeps it visible.
/// </summary>
public sealed class StudioPendingBotSeatReleaseTests : IDisposable
{
    private readonly string dataRoot = Path.Combine(
        Path.GetTempPath(),
        "erks-pending-bot-seat-release-tests",
        Guid.NewGuid().ToString("N"));
    private readonly string? previousRoot =
        Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT");

    public StudioPendingBotSeatReleaseTests()
    {
        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", dataRoot);
    }

    private static PendingBotSeatRelease Release(string organizationId, string botId) => new()
    {
        OrganizationId = organizationId,
        BotId = botId,
        DisplayName = "Зураг 1",
        DeviceFingerprint = new string('A', 64),
        LeftAtUtc = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
        LastFailure = "No such host is known.",
    };

    [Fact]
    public void AMachineThatHasLeftNothingHasNothingToSay()
    {
        Assert.Empty(StudioPendingBotSeatReleases.Read());
    }

    [Fact]
    public void ASeatLeftWithTheServerUnreachableIsRememberedWithItsReason()
    {
        Assert.True(StudioPendingBotSeatReleases.Record(Release("org_1", "bot_a")));

        PendingBotSeatRelease kept = Assert.Single(StudioPendingBotSeatReleases.Read());
        Assert.Equal("org_1", kept.OrganizationId);
        Assert.Equal("bot_a", kept.BotId);
        // The reason travels with the note: "it did not work" is not something
        // the owner can act on, "no such host" is.
        Assert.Equal("No such host is known.", kept.LastFailure);
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero), kept.LeftAtUtc);
    }

    [Fact]
    public void ReleasingOneSeatDoesNotForgetTheOthers()
    {
        StudioPendingBotSeatReleases.Record(Release("org_1", "bot_a"));
        StudioPendingBotSeatReleases.Record(Release("org_1", "bot_b"));
        StudioPendingBotSeatReleases.Record(Release("org_2", "bot_a"));

        StudioPendingBotSeatReleases.Forget("org_1", "bot_a");

        var left = StudioPendingBotSeatReleases.Read()
            .Select(item => item.OrganizationId + "/" + item.BotId)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        // Same bot id under a different organisation is a different seat, and
        // the same organisation holds more than one.
        Assert.Equal(["org_1/bot_b", "org_2/bot_a"], left);
    }

    [Fact]
    public void LeavingTheSameSeatTwiceLeavesOneNoteWithTheNewerReason()
    {
        StudioPendingBotSeatReleases.Record(Release("org_1", "bot_a"));
        StudioPendingBotSeatReleases.Record(Release("org_1", "bot_a") with
        {
            LastFailure = "503 Service Unavailable",
        });

        PendingBotSeatRelease kept = Assert.Single(StudioPendingBotSeatReleases.Read());
        Assert.Equal("503 Service Unavailable", kept.LastFailure);
    }

    [Fact]
    public void ForgettingASeatThatWasNeverNotedIsNotAnError()
    {
        StudioPendingBotSeatReleases.Forget("org_9", "bot_z");
        Assert.Empty(StudioPendingBotSeatReleases.Read());
    }

    [Fact]
    public void ANoteThatCouldNotBeWrittenSaysSoRatherThanPretending()
    {
        // The caller has already cleared the local seat by this point, so a
        // silent false here would erase the only record that the seat is still
        // held. A directory where the file should be makes the write fail for
        // real rather than by a mock agreeing with the test.
        Directory.CreateDirectory(StudioPendingBotSeatReleases.StorePath);

        Assert.False(StudioPendingBotSeatReleases.Record(Release("org_1", "bot_a")));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", previousRoot);
        try
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
