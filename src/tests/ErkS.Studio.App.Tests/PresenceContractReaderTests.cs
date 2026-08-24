using System.Text.Json;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The server's actual JSON, pasted from a live response, read by this side's
/// types.
/// </summary>
/// <remarks>
/// Four times in one day a field the server sent was dropped because this side
/// had no reader for it, and every unit test stayed green throughout - they
/// test logic, not contracts. These parse the other side's real output instead
/// of a shape invented here, which is the only way that class of fault shows
/// up before a user finds it.
/// </remarks>
public sealed class PresenceContractReaderTests
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void TheParticipantRecordFromTheServerIsReadWhole()
    {
        // Copied from GET /api/cloud-era/v1/projects/{id}, 2026-08-25.
        const string json = """
        {
          "participantId": "2802675bd9a34169971336da6900a52f",
          "accountEmail": "anna@erks.local",
          "displayName": "anna@erks.local",
          "familyName": "",
          "givenName": "",
          "organizationId": "07cc63cfb8b64c3a842c15ab96dc9a7a",
          "roles": ["ProjectAdmin", "DesignCompanyAdmin"],
          "stageScopes": ["4329d8a2dc5c43048c700d0eff87ac7c"],
          "lastSeenAtUtc": "2026-08-24T18:46:47.4992906+00:00",
          "profileImageUrl": "",
          "initials": "A",
          "scopes": ["project.read", "project.delete", "team.manage"],
          "status": "Active"
        }
        """;

        StudioCloudParticipant? participant =
            JsonSerializer.Deserialize<StudioCloudParticipant>(json, Options);

        Assert.NotNull(participant);
        Assert.Equal("anna@erks.local", participant!.AccountEmail);
        Assert.Equal("Active", participant.Status);
        Assert.NotNull(participant.LastSeenAtUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 24, 18, 46, 47, TimeSpan.Zero),
            participant.LastSeenAtUtc!.Value.ToUniversalTime(),
            TimeSpan.FromSeconds(1));
        Assert.Equal("A", participant.Initials);
        Assert.Equal("", participant.ProfileImageUrl);
    }

    [Fact]
    public void AParticipantTheServerHasNeverHeardFromReadsAsNull()
    {
        const string json = """
        { "accountEmail": "b@erks.local", "status": "Active", "lastSeenAtUtc": null }
        """;

        StudioCloudParticipant? participant =
            JsonSerializer.Deserialize<StudioCloudParticipant>(json, Options);

        Assert.NotNull(participant);
        Assert.Null(participant!.LastSeenAtUtc);
        Assert.Equal(
            MemberPresenceState.Unknown,
            MemberPresence.Resolve(participant.LastSeenAtUtc, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AParticipantFromAServerThatDoesNotSendPresenceStillReads()
    {
        // The field is simply absent, which is what an older server returns.
        // It must not fail to parse, and must not invent a timestamp.
        const string json = """
        { "accountEmail": "c@erks.local", "status": "Active" }
        """;

        StudioCloudParticipant? participant =
            JsonSerializer.Deserialize<StudioCloudParticipant>(json, Options);

        Assert.NotNull(participant);
        Assert.Null(participant!.LastSeenAtUtc);
    }

    [Fact]
    public void ThePresenceRuleFromCapabilitiesIsRead()
    {
        // Copied from GET /api/cloud-era/v1/capabilities, 2026-08-25.
        const string json = """
        { "rules": [ { "id": "presence", "version": 1,
          "values": { "onlineWithinSeconds": 180, "heartbeatIntervalSeconds": 60 } } ] }
        """;

        StudioServerRulesResponse? response =
            JsonSerializer.Deserialize<StudioServerRulesResponse>(json, Options);

        StudioServerRule rule = Assert.Single(response!.Rules);
        Assert.Equal("presence", rule.Id);
        Assert.Equal(1, rule.Version);
        Assert.Equal(180, rule.Values["onlineWithinSeconds"]);
        Assert.Equal(60, rule.Values["heartbeatIntervalSeconds"]);
    }

    [Fact]
    public void ARuleThisBuildDoesNotRecogniseIsIgnoredRatherThanFatal()
    {
        // The channel exists so the server can add rules without waiting for
        // anyone to update. A new one has to be harmless here.
        const string json = """
        { "rules": [
          { "id": "presence", "version": 1, "values": { "onlineWithinSeconds": 180 } },
          { "id": "something-new", "version": 4, "values": { "whatever": 9 } } ] }
        """;

        StudioServerRulesResponse? response =
            JsonSerializer.Deserialize<StudioServerRulesResponse>(json, Options);

        Assert.Equal(2, response!.Rules.Count);
        StudioServerRule presence = response.Rules.First(rule => rule.Id == "presence");
        Assert.Equal(180, presence.Values["onlineWithinSeconds"]);
    }

    [Fact]
    public void AServerWithNoRulesAtAllLeavesTheDefaultStanding()
    {
        StudioServerRulesResponse? response =
            JsonSerializer.Deserialize<StudioServerRulesResponse>("{}", Options);

        Assert.NotNull(response);
        Assert.Empty(response!.Rules);
    }

    [Fact]
    public void TheServersWindowChangesTheAnswer()
    {
        // 180 seconds is what the server actually sends today; someone seen
        // four minutes ago is outside it.
        DateTimeOffset now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            MemberPresenceState.Offline,
            MemberPresence.Resolve(now.AddMinutes(-4), now, TimeSpan.FromSeconds(180)));
        Assert.Equal(
            MemberPresenceState.Online,
            MemberPresence.Resolve(now.AddMinutes(-2), now, TimeSpan.FromSeconds(180)));
    }
}
