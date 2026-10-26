using System.Net.Http;

namespace ErkS.Studio;

/// <summary>
/// The project team through the bridge (Erk-S Sync, 2026-10-27: the owner asked to invite a member
/// from outside). Each action calls the same function as Studio's «Хамтын ажил» buttons; the e-mail
/// is only an argument here - it goes to the server by the invitation's own protocol and is written
/// to no log and no Twin.
/// </summary>
internal sealed partial class ShellView
{
    private async Task<StudioBridgeResponse> BridgeTeamListAsync(StudioBridgeRequest request)
    {
        await RefreshProjectTeamAsync();
        IReadOnlyList<StudioProjectRole> roles = await account.ListProjectRolesAsync();
        return StudioBridgeProtocol.Accept(request.Id, new
        {
            project = state.Project.Identity.Code,
            canManage = CanManageProjectTeam(),
            members = participantsList.Items.OfType<MemberRow>()
                .Select(row => new { name = row.Name, email = row.Email, roles = row.RoleCodes ?? [], status = row.Status, invitation = row.IsInvitation })
                .ToList(),
            roles = roles.Select(role => new { code = role.Code, label = role.Label, role.CanManageTeam }).ToList(),
        });
    }

    private async Task<StudioBridgeResponse> BridgeTeamInviteAsync(StudioBridgeRequest request)
    {
        if (request.Arg("project") is { } code && !code.Equals(state.Project.Identity.Code, StringComparison.OrdinalIgnoreCase))
            return StudioBridgeProtocol.Refuse(request.Id, "other_project", "Нээлттэй төсөл өөр байна: эхлээд «open-project code=" + code + "».");
        if (!CanManageProjectTeam())
            return StudioBridgeProtocol.Refuse(request.Id, "not_allowed", "Төслийн баг удирдах role шаардлагатай.");
        if (request.Arg("email") is not { } email || !email.Contains('@'))
            return StudioBridgeProtocol.Refuse(request.Id, "bad_request", "email=<бүртгэлтэй хэрэглэгчийн и-мэйл> заана уу.");
        try
        {
            IReadOnlyList<StudioProjectRole> roles = await account.ListProjectRolesAsync();
            if (StudioBridgeTeam.ResolveRoles(request.Arg("role"), roles) is not { } chosen)
            {
                return StudioBridgeProtocol.Refuse(
                    request.Id,
                    "bad_role",
                    "role=<код[,код]> заана уу: " + string.Join(", ", roles.Select(role => role.Code)));
            }
            // The dialog's «Хайх»: only a registered account is invited.
            StudioCloudAccountLookupResponse found = await account.LookupAccountAsync(email);
            if (!found.Found)
                return StudioBridgeProtocol.Refuse(request.Id, "account_not_found", "Энэ и-мэйлээр бүртгэлтэй хэрэглэгч олдсонгүй.");
            // The dialog's confirmation: an invitation leaves this machine only when asked to.
            if (!string.Equals(request.Arg("confirm"), "true", StringComparison.OrdinalIgnoreCase))
                return StudioBridgeProtocol.Refuse(request.Id, "confirm_required", "Урилга илгээнэ: «confirm=true» гэж баталгаажуулна уу.");
            StudioProjectMembershipInvitation invitation = await SendTeamInvitationAsync(found.Email, chosen);
            return StudioBridgeProtocol.Accept(
                request.Id,
                new { invitation = invitation.InvitationId, roles = invitation.Roles, invitation.Status },
                "Урилга илгээгдлээ — хүлээн авагч «team-accept»-аар (эсвэл Studio-гийн мэдэгдлээс) зөвшөөрнө.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            return StudioBridgeProtocol.Refuse(request.Id, "invite_failed", "Багийн урилга илгээж чадсангүй: " + exception.Message);
        }
    }

    /// <summary>Invitations to this account still waiting - the «Мэдэгдэл» list's project invitations.</summary>
    private async Task<StudioBridgeResponse> BridgeTeamInvitationsAsync(StudioBridgeRequest request)
    {
        try
        {
            StudioProjectMembershipInvitationListResponse invitations = await account.ListMembershipInvitationsAsync();
            return StudioBridgeProtocol.Accept(request.Id, invitations.Received
                .Where(item => item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                .Select(item => new { invitation = item.InvitationId, project = item.ProjectCode, name = item.ProjectName, roles = item.Roles, expires = item.ExpiresAtUtc })
                .ToList());
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            return StudioBridgeProtocol.Refuse(request.Id, "invitations_failed", "Урилгууд уншигдсангүй: " + exception.Message);
        }
    }

    /// <summary>The «Мэдэгдэл» dialog's «Зөвшөөрөх» on a project invitation: this account joins the team.</summary>
    private async Task<StudioBridgeResponse> BridgeTeamAcceptAsync(StudioBridgeRequest request)
    {
        if (request.Arg("invitation") is not { } invitationId)
            return StudioBridgeProtocol.Refuse(request.Id, "bad_request", "invitation=<id> заана уу («team-invitations»).");
        if (!string.Equals(request.Arg("confirm"), "true", StringComparison.OrdinalIgnoreCase))
            return StudioBridgeProtocol.Refuse(request.Id, "confirm_required", "Төсөлд нэгдэнэ: «confirm=true» гэж баталгаажуулна уу.");
        try
        {
            StudioCloudProjectDetail joined = await account.AcceptMembershipInvitationAsync(invitationId);
            await RefreshProjectsAsync();
            return StudioBridgeProtocol.Accept(request.Id, new { project = joined.Project.ProjectCode }, $"{joined.Project.ProjectCode} төсөлд нэгдлээ.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            return StudioBridgeProtocol.Refuse(request.Id, "accept_failed", "Урилгыг зөвшөөрч чадсангүй: " + exception.Message);
        }
    }
}

/// <summary>The bridge's reading of a team action's arguments (kept apart so it is tested).</summary>
internal static class StudioBridgeTeam
{
    /// <summary>The role codes asked for («a,b»), each one the project offers; null when none is given or one is unknown.</summary>
    public static IReadOnlyList<string>? ResolveRoles(string? argument, IReadOnlyList<StudioProjectRole> offered)
    {
        List<string> asked = (argument ?? "")
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (asked.Count == 0)
            return null;
        var codes = new List<string>();
        foreach (string code in asked)
        {
            StudioProjectRole? role = offered.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (role is null)
                return null;
            if (!codes.Contains(role.Code))
                codes.Add(role.Code);
        }
        return codes;
    }
}
