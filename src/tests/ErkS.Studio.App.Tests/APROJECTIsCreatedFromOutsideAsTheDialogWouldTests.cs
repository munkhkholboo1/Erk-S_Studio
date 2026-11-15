using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Owner, 2026-11-15: «студио дээр арт констракшн компани дээр энэ төслийг үүсгэ». The bridge's «create-project»
/// reads the «Шинэ төсөл» dialog's fields as arguments and hands them to the button's own function - and that
/// function no longer drops the chosen type, stage and client type (every project had become a building's
/// «Загвар зураг» for a citizen).
/// </summary>
public sealed class APROJECTIsCreatedFromOutsideAsTheDialogWouldTests
{
    private static readonly StudioCloudOrganization Art = new()
    {
        OrganizationId = "org-art",
        LegalName = "\"АРТКОНСТРАКШН\" ХХК",
        OrganizationType = "DesignCompany",
        CanManage = true,
    };

    private static readonly StudioCloudOrganization NotMine = new()
    {
        OrganizationId = "org-other",
        LegalName = "Арт гэр ХХК",
        OrganizationType = "DesignCompany",
        CanManage = false,
    };

    private static Dictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public void ANURBANPartialPlanForAGovernmentClientIsReadAsTheDialogWouldSendIt()
    {
        StudioBridgeProjectCreation.Outcome built = StudioBridgeProjectCreation.Build(
            Args(("org", "Арт констракшн"), ("code", "ART-07/2026"), ("name", "Донгой ХЕТ"),
                ("type", "urban-planning"), ("stage", "Хэсэгчилсэн ерөнхий төлөвлөгөө"),
                ("client-type", "government"), ("client", "Өндөр-Улаан сумын ЗДТГ"), ("address", "Архангай, Өндөр-Улаан")),
            [Art, NotMine]);

        Assert.Null(built.Refusal);
        Assert.Equal("org-art", built.Organization!.OrganizationId);
        ProjectCreationRequest request = built.Request!;
        Assert.Equal("urban-planning", request.ProjectType);
        Assert.Equal("partial-plan", request.InitialStageType);
        Assert.Equal(ProjectClientTypes.GovernmentAuthority, request.ClientType);
        Assert.Equal(ProjectInitiatorTypes.DesignOrganization, request.InitiatorType);
        Assert.Equal("org-art", request.InitiatorOrganizationId);
    }

    [Fact]
    public void ANORGANIZATIONTheAccountDoesNotManageIsNeverChosen()
    {
        StudioBridgeProjectCreation.Outcome built = StudioBridgeProjectCreation.Build(
            Args(("org", "Арт гэр"), ("code", "X-1"), ("name", "x"), ("client", "Бат")),
            [Art, NotMine]);

        Assert.Equal("organization_not_found", built.Refusal!.Code);
    }

    [Fact]
    public void ANUNKNOWNStageIsRefusedWithTheStagesOnOffer()
    {
        StudioBridgeProjectCreation.Outcome built = StudioBridgeProjectCreation.Build(
            Args(("org", "org-art"), ("code", "X-1"), ("name", "x"), ("type", "urban-planning"), ("stage", "Загвар зураг"), ("client", "Бат")),
            [Art]);

        Assert.Equal("bad_stage", built.Refusal!.Code);
        Assert.Contains("partial-plan", built.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void THEAccountIdentityKeepsTheTypeTheStageAndTheClientType()
    {
        string shell = ReadAppSource("ShellView.cs");
        int start = shell.IndexOf("private ProjectCreationRequest AttachAccountIdentity", StringComparison.Ordinal);
        string body = shell[start..shell.IndexOf("};", start, StringComparison.Ordinal)];

        foreach (string field in new[] { "ProjectType = request.ProjectType", "InitialStageType = request.InitialStageType", "InitialStageName = request.InitialStageName", "ClientType = request.ClientType" })
            Assert.Contains(field, body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEButtonAndTheBridgeCreateThroughOneFunction()
    {
        string shell = ReadAppSource("ShellView.cs");
        string bridge = ReadAppSource("ShellView.BridgeProject.cs");

        Assert.Contains("await CreateProjectForOrganizationAsync(", shell, StringComparison.Ordinal);
        Assert.Contains("await CreateProjectForOrganizationAsync(", bridge, StringComparison.Ordinal);
        Assert.NotNull(StudioBridgeActionCatalog.Find("create-project"));
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
