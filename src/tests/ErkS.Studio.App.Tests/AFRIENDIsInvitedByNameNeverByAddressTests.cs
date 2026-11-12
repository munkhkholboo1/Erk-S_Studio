using System.Text;
using System.Text.Json;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Owner, 2026-11-12 (through Мастер, SRV ea75fe6): «бидний бүртгэлд найзууд болдог функц нэмээрэй. Ингэж найзууд
/// болсон бүртгэлийг платформ дээр урьдаг хэсгүүд дээр найзуудаа хялбархан урьдаг болгоорой». Studio picks a friend
/// in the team invitation («Найзаас сонгох»), shows friend requests among the notifications, and manages friends in
/// «Найзууд». A friend is a key and a name: no e-mail address is ever sent for one, nor shown.
/// </summary>
public sealed class AFRIENDIsInvitedByNameNeverByAddressTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [Fact]
    public void THEServersFriendListIsRead()
    {
        const string json = """
            {"friends":[{"friendKey":"k-1","name":"Бат"}],
             "incoming":[{"friendKey":"k-2","name":"Сараа"}],
             "outgoing":[{"friendKey":"k-3","name":"Дорж"}]}
            """;

        StudioFriendsResponse friends = JsonSerializer.Deserialize<StudioFriendsResponse>(json, Web)!;

        Assert.Equal("k-1", friends.Friends.Single().FriendKey);
        Assert.Equal("Сараа", friends.Incoming.Single().Name);
        Assert.Equal("Дорж", friends.Outgoing.Single().Name);
    }

    [Fact]
    public void AFRIENDInvitationSendsTheKeyAndNoAddress()
    {
        string json = JsonSerializer.Serialize(new StudioProjectMembershipInvitationCreateRequest
        {
            FriendKey = "k-1",
            Roles = ["ProjectViewer"],
        }, Web);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal("k-1", document.RootElement.GetProperty("friendKey").GetString());
        Assert.Equal("", document.RootElement.GetProperty("targetEmail").GetString());
    }

    [Fact]
    public void APICKEDFriendIsNamedNotAddressed()
    {
        var draft = new ProjectInvitationDraft("", ["ProjectViewer"], "k-1", "Бат");

        Assert.Equal("Бат", draft.Counterparty);
        Assert.Equal("a@b.mn", new ProjectInvitationDraft("a@b.mn", ["ProjectViewer"]).Counterparty);
    }

    [Fact]
    public void REQUESTSWaitingOnThePersonComeFirst()
    {
        var friends = new StudioFriendsResponse
        {
            Friends = [new StudioFriend { FriendKey = "f2", Name = "Цэцэг" }, new StudioFriend { FriendKey = "f1", Name = "Ариун" }],
            Incoming = [new StudioFriend { FriendKey = "i1", Name = "Сараа" }],
            Outgoing = [new StudioFriend { FriendKey = "o1", Name = "Дорж" }],
        };

        List<StudioFriendsDialog.FriendRow> rows = StudioFriendsDialog.FriendRows(friends);

        Assert.Equal(["Сараа", "Ариун", "Цэцэг", "Дорж"], rows.Select(row => row.Name));
        Assert.Equal(StudioFriendsDialog.FriendRowKind.Incoming, rows[0].Kind);
        Assert.Equal(StudioFriendsDialog.FriendRowKind.Outgoing, rows[^1].Kind);
    }

    [Fact]
    public void THEStatusAfterAFriendInviteSaysTheName()
    {
        string collaboration = ReadAppSource("ShellView.Collaboration.cs");
        int start = collaboration.IndexOf("private async Task SendTeamInvitationToFriendAsync", StringComparison.Ordinal);
        Assert.True(start > 0);
        string body = collaboration[start..collaboration.IndexOf("private async Task EditSelectedTeamMemberRolesAsync", start, StringComparison.Ordinal)];

        Assert.Contains("InviteProjectFriendAsync", body, StringComparison.Ordinal);
        Assert.Contains("friendName", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetEmail", body, StringComparison.Ordinal);
    }

    [Fact]
    public void FRIENDRequestsCountAndAnOlderServerOrABotSeatStopNothing()
    {
        string collaboration = ReadAppSource("ShellView.Collaboration.cs");

        Assert.Contains("notificationFriends.Incoming.Count", collaboration, StringComparison.Ordinal);
        Assert.Contains("System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden", collaboration, StringComparison.Ordinal);
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(fileName);
    }
}
