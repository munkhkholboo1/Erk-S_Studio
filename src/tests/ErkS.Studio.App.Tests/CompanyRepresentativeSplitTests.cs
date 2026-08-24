using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The director and the appointed chief architect are two people.
///
/// They were one value written into two fields for as long as nobody could be
/// appointed. The server can tell them apart now, and these pin the reading
/// side: the moment an architect exists, the director must survive.
/// </summary>
public sealed class CompanyRepresentativeSplitTests
{
    [Fact]
    public void AppointedArchitectDoesNotBecomeTheDirector()
    {
        var cloud = new StudioCloudOrganization
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            DirectorTitle = "Захирал",
            DirectorName = "О.Очир-Эрдэнэ",
            DesignRepresentativeTitle = "Ерөнхий архитектор",
            DesignRepresentativeName = "Г.Энх-Амар",
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromOrganization(cloud);

        Assert.Equal("О.Очир-Эрдэнэ", profile.DirectorName);
        Assert.Equal("Захирал", profile.DirectorTitle);
        Assert.Equal("Г.Энх-Амар", profile.DesignRepresentativeName);
    }

    [Fact]
    public void UnappointedArchitectReadsAsUnappointed()
    {
        var cloud = new StudioCloudOrganization
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            DirectorTitle = "Захирал",
            DirectorName = "О.Очир-Эрдэнэ",
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromOrganization(cloud);

        Assert.Equal("", profile.DesignRepresentativeName);
        Assert.Equal("О.Очир-Эрдэнэ", profile.DirectorName);
    }

    [Fact]
    public void OrganizationWrittenBeforeTheSplitStillFindsItsDirector()
    {
        // The server used to answer with the person in whichever field, and
        // old rows can still carry only the design representative.
        var cloud = new StudioCloudOrganization
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            DesignRepresentativeTitle = "Захирал",
            DesignRepresentativeName = "О.Очир-Эрдэнэ",
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromOrganization(cloud);

        Assert.Equal("О.Очир-Эрдэнэ", profile.DirectorName);
        Assert.Equal("Захирал", profile.DirectorTitle);
    }

    [Fact]
    public void SignerIsTheDirectorRatherThanTheArchitect()
    {
        // The album's corner table falls back to the signer list for the line
        // it labels "Захирал".
        var cloud = new StudioCloudOrganization
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            DirectorTitle = "Захирал",
            DirectorName = "О.Очир-Эрдэнэ",
            DesignRepresentativeTitle = "Ерөнхий архитектор",
            DesignRepresentativeName = "Г.Энх-Амар",
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromOrganization(cloud);

        CompanySigner signer = Assert.Single(profile.Signers);
        Assert.Equal("О.Очир-Эрдэнэ", signer.FullName);
    }

    [Fact]
    public void UpsertStillSendsTheDirectorInBothHalvesUntilTheFlagIsDeclared()
    {
        // supportsSeparateRepresentatives is not declared yet, so the server
        // ignores the design representative half. Sending the architect there
        // would file them as the director on any server that did read it, and
        // sending nothing would blank the director.
        var profile = new CompanyProfile
        {
            OrganizationId = "org-1",
            Name = "Эрк-С ХХК",
            DirectorTitle = "Захирал",
            DirectorName = "О.Очир-Эрдэнэ",
            DesignRepresentativeTitle = "Ерөнхий архитектор",
            DesignRepresentativeName = "Г.Энх-Амар",
        };

        StudioCloudOrganizationUpsertRequest request =
            StudioCompanyProfileMapper.ToUpsertRequest(profile, baseConcurrencyToken: "");

        Assert.Equal("О.Очир-Эрдэнэ", request.DirectorName);
        Assert.Equal("О.Очир-Эрдэнэ", request.DesignRepresentativeName);
    }
}
