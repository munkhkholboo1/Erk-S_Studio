namespace ErkS.Studio.App.Tests;

public sealed class ProjectChatConversationResolverTests
{
    [Fact]
    public void UsesServerConversationsWhenAvailable()
    {
        var expected = new StudioProjectChatConversation
        {
            PeerEmail = "member@erks.local",
            DisplayName = "Member",
            UnreadCount = 2,
        };
        var response = new StudioProjectChatResponse
        {
            CurrentUserEmail = "owner@erks.local",
            Conversations = [expected],
            Participants =
            [
                new StudioProjectChatParticipant
                {
                    Email = "legacy@erks.local",
                    DisplayName = "Legacy",
                },
            ],
        };

        IReadOnlyList<StudioProjectChatConversation> result =
            ShellView.ResolveProjectChatConversations(response);

        Assert.Same(expected, Assert.Single(result));
    }

    [Fact]
    public void FallsBackToLegacyParticipantsWithoutTreatingPresenceAsMembership()
    {
        var response = new StudioProjectChatResponse
        {
            CurrentUserEmail = "owner@erks.local",
            Participants =
            [
                new StudioProjectChatParticipant
                {
                    Email = "owner@erks.local",
                    DisplayName = "Owner",
                },
                new StudioProjectChatParticipant
                {
                    Email = " offline.member@erks.local ",
                    DisplayName = "Offline member",
                    Initials = "OM",
                    RoleLabel = "Architect",
                    ProfileImageUrl = "/photo",
                },
                new StudioProjectChatParticipant
                {
                    Email = "OFFLINE.MEMBER@ERKS.LOCAL",
                    DisplayName = "Duplicate",
                },
            ],
        };

        StudioProjectChatConversation result = Assert.Single(
            ShellView.ResolveProjectChatConversations(response));

        Assert.Equal("offline.member@erks.local", result.PeerEmail);
        Assert.Equal("Offline member", result.DisplayName);
        Assert.Equal("OM", result.Initials);
        Assert.Equal("Architect", result.RoleLabel);
        Assert.Equal("/photo", result.ProfileImageUrl);
    }
}
