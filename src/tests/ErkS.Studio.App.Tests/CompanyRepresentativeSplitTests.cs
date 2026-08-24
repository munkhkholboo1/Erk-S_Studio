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
    public void AProfileNeverReadFromTheServerSendsNoArchitectAndDoesNotDeclare()
    {
        // Every cached profile on every machine still holds the director's
        // name in the architect's field. Declaring the flag here would appoint
        // the director as their own company's chief architect, in the cloud,
        // for every colleague on the project.
        var profile = new CompanyProfile
        {
            OrganizationId = "org-1",
            Name = "Эрк-С ХХК",
            DirectorTitle = "Захирал",
            DirectorName = "О.Очир-Эрдэнэ",
            DesignRepresentativeTitle = "Захирал",
            DesignRepresentativeName = "О.Очир-Эрдэнэ",
            DesignRepresentativeKnown = false,
        };

        StudioCloudOrganizationUpsertRequest request =
            StudioCompanyProfileMapper.ToUpsertRequest(profile, baseConcurrencyToken: "");

        Assert.False(request.SupportsSeparateRepresentatives);
        Assert.Equal("О.Очир-Эрдэнэ", request.DirectorName);
        // Both halves carry the director, exactly as before the split, so an
        // undeclared request behaves on the wire the way it always did.
        Assert.Equal("О.Очир-Эрдэнэ", request.DesignRepresentativeName);
    }

    [Fact]
    public void AKnownArchitectIsSentAsItselfAndDeclared()
    {
        var profile = new CompanyProfile
        {
            OrganizationId = "org-1",
            Name = "Эрк-С ХХК",
            DirectorTitle = "Захирал",
            DirectorName = "О.Очир-Эрдэнэ",
            DesignRepresentativeTitle = "Ерөнхий архитектор",
            DesignRepresentativeName = "Г.Энх-Амар",
            DesignRepresentativeKnown = true,
        };

        StudioCloudOrganizationUpsertRequest request =
            StudioCompanyProfileMapper.ToUpsertRequest(profile, baseConcurrencyToken: "");

        Assert.True(request.SupportsSeparateRepresentatives);
        Assert.Equal("О.Очир-Эрдэнэ", request.DirectorName);
        Assert.Equal("Г.Энх-Амар", request.DesignRepresentativeName);
    }

    [Fact]
    public void KnowingThatNobodyIsAppointedIsAlsoKnowledge()
    {
        // Read from the server, which said the role is vacant. Sending that
        // empty value under the flag is how a vacancy is recorded, and it must
        // not fall back to the director.
        var cloud = new StudioCloudOrganization
        {
            OrganizationId = "org-1",
            LegalName = "Эрк-С ХХК",
            DirectorTitle = "Захирал",
            DirectorName = "О.Очир-Эрдэнэ",
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromOrganization(cloud);
        StudioCloudOrganizationUpsertRequest request =
            StudioCompanyProfileMapper.ToUpsertRequest(profile, baseConcurrencyToken: "");

        Assert.True(profile.DesignRepresentativeKnown);
        Assert.True(request.SupportsSeparateRepresentatives);
        Assert.Equal("", request.DesignRepresentativeName);
        Assert.Equal("О.Очир-Эрдэнэ", request.DirectorName);
    }

    [Fact]
    public void TheKnownMarkerSurvivesCloning()
    {
        // The profile is cloned on its way into the project snapshot and back;
        // losing the marker there would silently re-arm the residue.
        var profile = new CompanyProfile
        {
            OrganizationId = "org-1",
            DesignRepresentativeName = "Г.Энх-Амар",
            DesignRepresentativeKnown = true,
        };

        CompanyProfile copy = profile.Clone();

        Assert.True(copy.DesignRepresentativeKnown);
    }
}
