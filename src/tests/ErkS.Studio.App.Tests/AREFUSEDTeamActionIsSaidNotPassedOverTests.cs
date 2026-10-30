using System.Net;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The owner sent an invitation, the server refused it (403 membership_invitation_forbidden) and
/// the owner understood it was sent (Мастер, 2026-10-31): the refusal went to the status line
/// and was overwritten. A refused team action is named in words, in a window.
/// </summary>
public sealed class AREFUSEDTeamActionIsSaidNotPassedOverTests
{
    [Fact]
    public void AForbiddenInvitationNamesTheMissingRight()
    {
        string text = StudioWriteRefusal.Describe(
            "Урилга илгээгдсэнгүй",
            new StudioAccountException("forbidden", HttpStatusCode.Forbidden, "membership_invitation_forbidden"));

        Assert.StartsWith("Урилга илгээгдсэнгүй", text, StringComparison.Ordinal);
        Assert.Contains("төслийн админ эрх", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AConflictAndANetworkFailureAreToldApart()
    {
        Assert.Contains("өөр хэрэглэгч", StudioWriteRefusal.Describe(
            "Хадгалагдсангүй",
            new StudioAccountException("conflict", HttpStatusCode.Conflict, "x")), StringComparison.Ordinal);
        Assert.Contains("холбогдож", StudioWriteRefusal.Describe(
            "Хадгалагдсангүй",
            new HttpRequestException("down")), StringComparison.Ordinal);
    }
}
