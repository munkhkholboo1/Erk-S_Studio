using System.Net.Http;
using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes;

namespace ErkS.Studio;

/// <summary>
/// «Шинэ төсөл» through the bridge (owner, 2026-11-15: «студио дээр арт констракшн компани дээр энэ төслийг үүсгэ»).
/// The dialog's fields are arguments; the creation itself is the button's own function
/// (<see cref="CreateProjectForOrganizationAsync"/>), and confirm=true stands for the relationship dialog.
/// </summary>
internal sealed partial class ShellView
{
    private async Task<StudioBridgeResponse> BridgeCreateProjectAsync(StudioBridgeRequest request)
    {
        if (!EnsureWorkspaceLifecycleChangeAllowed())
            return StudioBridgeProtocol.Refuse(request.Id, "busy", statusText.Text);
        if (account.Current is null)
            return StudioBridgeProtocol.Refuse(request.Id, "not_signed_in", "Studio-д нэвтэрсэн бүртгэл алга.");
        long accountEpoch = account.SessionEpoch;

        IReadOnlyList<StudioCloudOrganization> organizations;
        try
        {
            organizations = await account.ListOrganizationsAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            return StudioBridgeProtocol.Refuse(request.Id, "organizations_failed", "Байгууллагын бүртгэл уншигдсангүй: " + exception.Message);
        }

        StudioBridgeProjectCreation.Outcome built = StudioBridgeProjectCreation.Build(request.Args, organizations);
        if (built.Refusal is { } refusal)
            return StudioBridgeProtocol.Refuse(request.Id, refusal.Code, refusal.Message);
        if (!string.Equals(request.Arg("confirm"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return StudioBridgeProtocol.Refuse(
                request.Id,
                "confirm_required",
                $"«{built.Organization!.LegalName}» байгууллагад «{built.Request!.Code}» төсөл үүсгэнэ: «confirm=true» гэж баталгаажуулна уу.");
        }

        projectOpenInProgress = true;
        string? code;
        try
        {
            code = await CreateProjectForOrganizationAsync(built.Request!, built.Organization!, accountEpoch, _ => true);
        }
        finally
        {
            projectOpenInProgress = false;
        }
        return code is null
            ? StudioBridgeProtocol.Refuse(request.Id, "create_failed", statusText.Text)
            : StudioBridgeProtocol.Accept(
                request.Id,
                new { code, path = state.ProjectPath, organization = built.Organization!.LegalName, type = built.Request!.ProjectType, stage = built.Request.InitialStageName },
                statusText.Text);
    }
}

/// <summary>The bridge's reading of «create-project»'s arguments into the dialog's request (kept apart so it is tested).</summary>
internal static class StudioBridgeProjectCreation
{
    internal sealed record Refused(string Code, string Message);

    internal sealed record Outcome(ProjectCreationRequest? Request, StudioCloudOrganization? Organization, Refused? Refusal);

    public static Outcome Build(IReadOnlyDictionary<string, string> args, IReadOnlyList<StudioCloudOrganization> organizations)
    {
        string Arg(string name) => args.TryGetValue(name, out string? value) ? (value ?? "").Trim() : "";
        Outcome No(string code, string message) => new(null, null, new Refused(code, message));

        // The dialog offers only the organizations this account may create a design project for.
        StudioCloudOrganization[] allowed = organizations
            .Where(StudioOrganizationAccessPolicy.CanCreateDesignProject)
            .GroupBy(item => item.OrganizationId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (allowed.Length == 0)
            return No("no_organization", "Таны бүртгэлд админ эрхтэй зураг төслийн байгууллага алга.");
        string offered = string.Join(", ", allowed.Select(Label));
        string asked = Arg("org");
        if (asked.Length == 0)
            return No("bad_request", "org=<байгууллагын нэр эсвэл id> заана уу: " + offered);
        StudioCloudOrganization[] matched = allowed.Where(item => Matches(item, asked)).ToArray();
        if (matched.Length != 1)
        {
            return No(
                matched.Length == 0 ? "organization_not_found" : "organization_ambiguous",
                $"«{asked}» байгууллага {(matched.Length == 0 ? "олдсонгүй" : "олон таарлаа")}: " + offered);
        }

        string code = Arg("code"), name = Arg("name");
        if (code.Length == 0 || name.Length == 0)
            return No("bad_request", "code=<төслийн код> ба name=<төслийн нэр> заана уу.");

        IStudioProjectTypeDefinition? type = Arg("type") is { Length: > 0 } typeText
            ? StudioProjectTypeRegistry.All.FirstOrDefault(item =>
                item.Id.Equals(typeText, StringComparison.OrdinalIgnoreCase) ||
                item.Label.Equals(typeText, StringComparison.CurrentCultureIgnoreCase))
            : StudioProjectTypeRegistry.Resolve(BuildingDesignProjectType.TypeId);
        if (type is null)
            return No("bad_type", "type=: " + string.Join(", ", StudioProjectTypeRegistry.All.Select(item => $"{item.Id} ({item.Label})")));

        StudioProjectStageDefinition[] stages = type.Stages.Where(item => item.EnabledForNewProject).ToArray();
        StudioProjectStageDefinition? stage = Arg("stage") is { Length: > 0 } stageText
            ? stages.FirstOrDefault(item =>
                item.Id.Equals(stageText, StringComparison.OrdinalIgnoreCase) ||
                item.Label.Equals(stageText, StringComparison.CurrentCultureIgnoreCase))
            : type.Id.Equals(BuildingDesignProjectType.TypeId, StringComparison.OrdinalIgnoreCase)
                ? stages.FirstOrDefault(item => item.Id.Equals("model-design", StringComparison.OrdinalIgnoreCase))
                : stages.FirstOrDefault();
        if (stage is null)
            return No("bad_stage", "stage=: " + string.Join(", ", stages.Select(item => $"{item.Id} ({item.Label})")));

        string clientType = Arg("client-type").ToLowerInvariant() switch
        {
            "" or "citizen" or "иргэн" => ProjectClientTypes.Citizen,
            "organization" or "байгууллага" => ProjectClientTypes.Organization,
            "government" or "governmentauthority" or "төрийн байгууллага" => ProjectClientTypes.GovernmentAuthority,
            _ => "",
        };
        if (clientType.Length == 0)
            return No("bad_client_type", "client-type=citizen|organization|government");
        string client = Arg("client");
        if (client.Length == 0)
            return No("bad_request", "client=<" + ProjectClientTypes.ClientNameFieldLabel(clientType) + "> заана уу.");

        StudioCloudOrganization organization = matched[0];
        return new Outcome(
            new ProjectCreationRequest
            {
                Code = code,
                Name = name,
                Description = Arg("description"),
                ProjectType = type.Id,
                InitialStageType = stage.Id,
                InitialStageName = stage.Label,
                Channel = ProjectCreationChannels.Studio,
                InitiatorType = ProjectInitiatorTypes.DesignOrganization,
                InitiatorOrganizationId = organization.OrganizationId,
                InitiatorOrganizationName = organization.LegalName,
                ClientType = clientType,
                ClientName = client,
                ClientEmail = Arg("client-email"),
                SiteAddress = Arg("address"),
            },
            organization,
            null);
    }

    private static string Label(StudioCloudOrganization organization) =>
        new[] { organization.DisplayName, organization.LegalName, organization.ShortName }
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))?.Trim() ?? organization.OrganizationId;

    /// <summary>The id, or any of the names - ignoring case, quotes and spaces («Арт констракшн» finds «"АРТКОНСТРАКШН" ХХК»).</summary>
    private static bool Matches(StudioCloudOrganization organization, string asked)
    {
        if (organization.OrganizationId.Equals(asked, StringComparison.OrdinalIgnoreCase))
            return true;
        string key = Squeeze(asked);
        return key.Length > 0 && new[] { organization.DisplayName, organization.LegalName, organization.ShortName }
            .Any(text => Squeeze(text).Contains(key, StringComparison.Ordinal));
    }

    private static string Squeeze(string? text) =>
        new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
