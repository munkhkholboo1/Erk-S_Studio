using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCompanyProfileMapperTests
{
    [Fact]
    public void ProjectRenderProfileCarriesOfficialAndManualFieldsIntoAlbumProfile()
    {
        var cloud = new StudioCloudOrganizationRenderProfile
        {
            OrganizationId = "org-1",
            LegalName = "Албан ёсны компани ХХК",
            DisplayName = "Албан ёсны компани",
            RegistrationNumber = "1234567",
            LegalEntityType = "Хуулийн этгээд",
            LegalForm = "ХХК",
            ActivityDirections = ["Архитектур", "Зураг төсөл"],
            OfficialRepresentativeName = "Б.Бат",
            RegistrySource = "OfficialRegistry",
            RegisteredCity = "Улаанбаатар",
            Address = "Сүхбаатар дүүрэг, 1-р хороо",
            DesignRepresentativeTitle = "Зураг төсөл хариуцсан захирал",
            DesignRepresentativeName = "Э.Мөнххолбоо",
            LicenseScope = "Архитектур",
            LicenseNumber = "ЗТ-001",
        };

        CompanyProfile profile = StudioCompanyProfileMapper.FromRenderProfile(cloud);

        Assert.Equal("Албан ёсны компани ХХК", profile.Name);
        Assert.Equal("OfficialRegistry", profile.RegistrySource);
        Assert.Equal("Улаанбаатар", profile.RegisteredCity);
        Assert.Equal("Сүхбаатар дүүрэг, 1-р хороо", profile.Address);
        Assert.Equal("Б.Бат", profile.OfficialRepresentativeName);
        Assert.Equal("Зураг төсөл хариуцсан захирал", profile.DesignRepresentativeTitle);
        Assert.Equal("Э.Мөнххолбоо", profile.DesignRepresentativeName);
        Assert.Equal("ЗТ-001", profile.LicenseNumber);
        CompanySigner signer = Assert.Single(profile.Signers);
        Assert.Equal(profile.DesignRepresentativeName, signer.FullName);
    }

    [Fact]
    public void UpsertKeepsOfficialRepresentativeSeparateFromDesignRepresentative()
    {
        var profile = new CompanyProfile
        {
            Name = "Компани ХХК",
            OfficialRepresentativeName = "А.Албан",
            DesignRepresentativeTitle = "Зураг төсөл хариуцсан захирал",
            DesignRepresentativeName = "З.Төлөөлөгч",
        };

        StudioCloudOrganizationUpsertRequest request = StudioCompanyProfileMapper.ToUpsertRequest(profile);

        Assert.Equal("А.Албан", request.OfficialRepresentativeName);
        Assert.Equal("З.Төлөөлөгч", request.DesignRepresentativeName);
        Assert.Equal(request.DesignRepresentativeName, request.DirectorName);
    }
}
