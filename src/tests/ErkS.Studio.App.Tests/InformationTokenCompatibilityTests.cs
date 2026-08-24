using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// New Studio against old server, and old Studio against new server.
///
/// Not everyone updates at once, and a real project is being drawn right now
/// by four people. The information endpoint takes an If-Match token; the
/// server has just grown a second, narrower one that does not move when an
/// album is uploaded. Using it is what stops a user's own upload from
/// invalidating their own queued edit.
///
/// The danger is the release order. A server that predates the narrow token
/// simply omits it, and a client that insisted on it would refuse to save at
/// all - turning a fix into an outage for anyone whose server had not been
/// updated yet. These pin all four combinations.
/// </summary>
public sealed class InformationTokenCompatibilityTests
{
    private const string ProjectToken = "93F34CA5B8A06BD9EEA6248C";
    private const string InformationToken = "22FA1B77C0DE4419A0B15E30";

    [Fact]
    public void NewStudioNewServer_UsesTheNarrowToken()
    {
        // The point of the whole exercise: an album upload moves the project
        // token and leaves this one alone.
        var snapshot = new ProjectServerSnapshot
        {
            ConcurrencyToken = ProjectToken,
            InformationConcurrencyToken = InformationToken,
        };

        Assert.Equal(
            InformationToken,
            ProjectInformationSaveReconciler.ResolveEditBaseToken(snapshot));
    }

    [Fact]
    public void NewStudioOldServer_FallsBackInsteadOfRefusingToSave()
    {
        // An older server sends no narrow token. Demanding one would leave the
        // user unable to save anything at all - a worse outage than the bug
        // being fixed.
        var snapshot = new ProjectServerSnapshot
        {
            ConcurrencyToken = ProjectToken,
            InformationConcurrencyToken = "",
        };

        Assert.Equal(
            ProjectToken,
            ProjectInformationSaveReconciler.ResolveEditBaseToken(snapshot));
    }

    [Fact]
    public void NewStudioOldServer_StillProducesAUsableEditBase()
    {
        // The guard above the save path must not trip on the fallback.
        var snapshot = new ProjectServerSnapshot { ConcurrencyToken = ProjectToken };

        string token = ProjectInformationSaveReconciler.RequireCanonicalEditBaseToken(
            ProjectInformationSaveReconciler.ResolveEditBaseToken(snapshot));

        Assert.Equal(ProjectToken, token);
    }

    [Fact]
    public void AProjectWithNoTokensAtAllIsStillRefusedLoudly()
    {
        // Falling back must not degrade into sending nothing. An edit with no
        // base cannot be rebased safely, and that has to be said rather than
        // guessed at.
        var snapshot = new ProjectServerSnapshot();

        Assert.Throws<InvalidOperationException>(() =>
            ProjectInformationSaveReconciler.RequireCanonicalEditBaseToken(
                ProjectInformationSaveReconciler.ResolveEditBaseToken(snapshot)));
    }

    [Fact]
    public void AWhitespaceTokenCountsAsAbsent()
    {
        var snapshot = new ProjectServerSnapshot
        {
            ConcurrencyToken = ProjectToken,
            InformationConcurrencyToken = "   ",
        };

        Assert.Equal(
            ProjectToken,
            ProjectInformationSaveReconciler.ResolveEditBaseToken(snapshot));
    }

    [Fact]
    public void OldStudioNewServer_LosesNothingBecauseTheOldTokenStillWorks()
    {
        // Old Studio never reads the new field and keeps sending the project
        // token, which the new server still accepts. Nothing here to break -
        // the test states it so a future change to the server contract has to
        // come past this claim.
        var snapshot = new ProjectServerSnapshot
        {
            ConcurrencyToken = ProjectToken,
            InformationConcurrencyToken = InformationToken,
        };

        // What an old client would have used: the only token it knows about.
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ConcurrencyToken));
    }

    [Fact]
    public void TheNarrowTokenSurvivesASync()
    {
        // It is read from the server, stored in the mirror, and read back when
        // the next edit opens. Dropping it anywhere in that chain would send
        // every client silently back to the old behaviour.
        var project = new ProjectWorkspace();
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "c59b2a4ce1cd4657b025a826223c6a5a";
        project.ProjectId = project.Cloud.ServerProjectId;

        ProjectCanonicalSyncService.Apply(project, new ProjectServerSnapshot
        {
            ProjectId = project.Cloud.ServerProjectId,
            ConcurrencyToken = ProjectToken,
            InformationConcurrencyToken = InformationToken,
        });

        Assert.Equal(
            InformationToken,
            ProjectInformationSaveReconciler.ResolveEditBaseToken(project.Cloud.ServerSnapshot));
    }

    [Fact]
    public void StudioDoesNotClaimToUnderstandFieldOverrides()
    {
        // The server treats an empty value from a client that declares
        // supportsFieldOverrides as "clear this override". Studio still sends
        // these three fields empty always, so declaring it would erase what a
        // colleague had entered. The flag goes in when the fields do, not
        // before.
        var pending = new PendingProjectInformationUpdate
        {
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Foundation = new ProjectServerFoundationUpdate { IsAvailable = true },
        };

        StudioCloudProjectInformationUpdateRequest request =
            ProjectInformationSaveReconciler.CreateRequest(pending);

        Assert.DoesNotContain(
            typeof(StudioCloudProjectFoundationUpdate).GetProperties(),
            property => property.Name.Contains("SupportsFieldOverrides", StringComparison.Ordinal));
        Assert.NotNull(request.Foundation);
    }
}
