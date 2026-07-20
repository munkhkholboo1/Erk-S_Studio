namespace ErkS.Studio.App.Tests;

public sealed class StudioOrganizationAccessPolicyTests
{
    [Fact]
    public void ServerCanManageFlagAllowsProjectCreationWithoutRoleLabelDependency()
    {
        StudioCloudOrganization organization = DesignOrganization(
            canManage: true,
            currentUserRole: "Localized administrator");

        Assert.True(StudioOrganizationAccessPolicy.CanCreateDesignProject(organization));
    }

    [Theory]
    [InlineData("Organization Owner")]
    [InlineData("Organization Admin")]
    [InlineData("Design Company Admin")]
    [InlineData("DesignCompanyAdmin")]
    public void LegacyManagementRolesRemainCompatible(string role)
    {
        StudioCloudOrganization organization = DesignOrganization(
            canManage: false,
            currentUserRole: role);

        Assert.True(StudioOrganizationAccessPolicy.CanCreateDesignProject(organization));
    }

    [Fact]
    public void NonManagingDesignMemberCannotCreateProjectForOrganization()
    {
        StudioCloudOrganization organization = DesignOrganization(
            canManage: false,
            currentUserRole: "Architect");

        Assert.False(StudioOrganizationAccessPolicy.CanCreateDesignProject(organization));
    }

    [Fact]
    public void ManagingNonDesignOrganizationCannotCreateDesignProject()
    {
        StudioCloudOrganization organization = DesignOrganization(
            canManage: true,
            currentUserRole: "Organization Admin");
        organization.OrganizationType = "ClientOrganization";

        Assert.False(StudioOrganizationAccessPolicy.CanCreateDesignProject(organization));
    }

    [Fact]
    public void OrganizationMustHaveServerIdentity()
    {
        StudioCloudOrganization organization = DesignOrganization(
            canManage: true,
            currentUserRole: "Organization Admin");
        organization.OrganizationId = "";

        Assert.False(StudioOrganizationAccessPolicy.CanCreateDesignProject(organization));
    }

    private static StudioCloudOrganization DesignOrganization(bool canManage, string currentUserRole) => new()
    {
        OrganizationId = "org-erks-design-company-test",
        OrganizationType = "DesignCompany",
        CanManage = canManage,
        CurrentUserRole = currentUserRole,
    };
}
