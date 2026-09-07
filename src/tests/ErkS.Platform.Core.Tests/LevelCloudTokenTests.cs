namespace ErkS.Platform.Core.Tests;

/// <summary>
/// «The cloud has moved» must mean «somebody ELSE moved it».
///
/// 🔴 THE TOKEN MOVES ON EVERY PROJECT WRITE - INCLUDING THIS DEVICE'S OWN.
/// That is the server's stated behaviour and it is the right one, but it makes
/// the obvious client rule wrong: comparing the server's token with the newest
/// token this device has SEEN turns the indicator "others are ahead" the moment
/// the user uploads their own work.
///
/// So there is a second token, stamped only at the moments this device is
/// genuinely level with the cloud. These tests pin which moments those are -
/// and, more importantly, which one is not.
/// </summary>
public sealed class LevelCloudTokenTests
{
    private static ProjectWorkspace Project() => new();

    [Fact]
    public void ACompletedSyncSTAMPSTheLevelToken()
    {
        ProjectWorkspace project = Project();

        ProjectCloudSyncMetadata.MarkSynced(project, "sha", "rev-1", "token-A", DateTimeOffset.UtcNow);

        Assert.Equal("token-A", project.Cloud.LastLevelCloudToken);
    }

    [Fact]
    public void AFullRefreshSTAMPSItToo()
    {
        // A refresh takes the cloud project in whole, which leaves this device
        // in the same standing a completed sync does.
        ProjectWorkspace project = Project();

        ProjectCloudSyncMetadata.MarkCloudRefreshed(project, "token-B", DateTimeOffset.UtcNow);

        Assert.Equal("token-B", project.Cloud.LastLevelCloudToken);
    }

    [Fact]
    public void ACONFLICTMustNOTStampIt()
    {
        // 🔴 THE ONE THAT MATTERS. A conflict is the opposite of being level:
        // the server moved, this device's edit did not go in, the work is still
        // waiting. The server's current token IS recorded - the next guarded
        // write needs it - and stamping the LEVEL token from it would tell the
        // indicator "nothing new in the cloud" at the single moment there is
        // something unresolved.
        ProjectWorkspace project = Project();
        ProjectCloudSyncMetadata.MarkSynced(project, "sha", "rev-1", "token-A", DateTimeOffset.UtcNow);

        ProjectCloudSyncMetadata.MarkConflict(
            project,
            new PendingProjectInformationUpdate(),
            "token-C",
            "conflict");

        // The newest token seen moved...
        Assert.Equal("token-C", project.Cloud.LastServerConcurrencyToken);
        // ...and the level token did not, so the cloud still reads as ahead.
        Assert.Equal("token-A", project.Cloud.LastLevelCloudToken);
        Assert.NotEqual(project.Cloud.LastServerConcurrencyToken, project.Cloud.LastLevelCloudToken);
    }

    [Fact]
    public void THETwoTokensAreGENUINELYDifferentFields()
    {
        // The positive control for the test above: if both names resolved to
        // one value, every assertion here would be about the same field and the
        // conflict case would look fine while being broken.
        ProjectWorkspace project = Project();

        project.Cloud.LastServerConcurrencyToken = "seen";
        project.Cloud.LastLevelCloudToken = "level";

        Assert.Equal("seen", project.Cloud.LastServerConcurrencyToken);
        Assert.Equal("level", project.Cloud.LastLevelCloudToken);
    }

    [Fact]
    public void AFRESHProjectHasNOLevelTokenRatherThanAnEmptyMatch()
    {
        // Before the first sync there is nothing to compare against. An empty
        // level token must read as "not known" - which the probe turns into the
        // grey state - and never as "the same as the server's".
        Assert.Equal("", Project().Cloud.LastLevelCloudToken);
    }
}
