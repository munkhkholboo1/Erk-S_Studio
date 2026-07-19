using System.Text.Json.Nodes;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class CompanyLibraryTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-company-library-tests",
        Guid.NewGuid().ToString("N"));

    public CompanyLibraryTests() => Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void LegacyProjectWithoutLogoTransformLoadsCenteredAtOneToOne()
    {
        string path = Path.Combine(workDirectory, "legacy.erksalbum");
        var project = new AlbumProject
        {
            Name = "Legacy",
            Company = new CompanyProfile { Name = "Company", Phone = "70000000" },
        };
        AlbumProjectStore.Save(project, path);
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject company = root["company"]!.AsObject();
        company.Remove("logoScale");
        company.Remove("logoOffsetX");
        company.Remove("logoOffsetY");
        company.Remove("phoneNumbers");
        company["logoOriginalFileName"] = null;
        File.WriteAllText(path, root.ToJsonString());

        AlbumProject loaded = AlbumProjectStore.Load(path);

        Assert.Equal(1d, loaded.Company.LogoScale);
        Assert.Equal(0d, loaded.Company.LogoOffsetX);
        Assert.Equal(0d, loaded.Company.LogoOffsetY);
        Assert.Equal("", loaded.Company.LogoOriginalFileName);
        Assert.Equal(["70000000"], loaded.Company.PhoneNumbers);
    }

    [Fact]
    public void AccountCachePreservesCompanyAndLogoPlacement()
    {
        var store = new CompanyLibraryStore(
            Path.Combine(workDirectory, "companies.json"),
            Path.Combine(workDirectory, "logos"));
        var entry = new CompanyCatalogEntry
        {
            CanManage = true,
            CurrentUserRole = "Organization Admin",
            DocumentsPendingCloudSync = true,
            Profile = new CompanyProfile
            {
                OrganizationId = "org-1",
                Name = "Erk-S Design",
                LogoOriginalFileName = "erk-s-logo.png",
                LogoScale = 1.75,
                LogoOffsetX = 0.2,
                LogoOffsetY = -0.15,
                RegistrationCertificateDocuments =
                [
                    new ProjectFileReference
                    {
                        Category = ProjectDocumentCategories.CompanyRegistrationCertificate,
                        OriginalFileName = "registration.pdf",
                        RelativePath = @"C:\cache\registration.pdf",
                        ContentType = "application/pdf",
                        PageCount = 2,
                        Sha256 = "registration-hash",
                    },
                ],
                DesignLicenseDocuments =
                [
                    new ProjectFileReference
                    {
                        Category = ProjectDocumentCategories.CompanyDesignLicense,
                        OriginalFileName = "license.jpg",
                        RelativePath = @"C:\cache\license.jpg",
                        ContentType = "image/jpeg",
                        PageCount = 1,
                        Sha256 = "license-hash",
                    },
                ],
            },
        };

        store.Save([entry]);
        CompanyCatalogEntry loaded = Assert.Single(store.Load());

        Assert.True(loaded.CanManage);
        Assert.Equal("org-1", loaded.Profile.OrganizationId);
        Assert.Equal("erk-s-logo.png", loaded.Profile.LogoOriginalFileName);
        Assert.Equal(1.75, loaded.Profile.LogoScale);
        Assert.Equal(0.2, loaded.Profile.LogoOffsetX);
        Assert.Equal(-0.15, loaded.Profile.LogoOffsetY);
        Assert.True(loaded.DocumentsPendingCloudSync);
        Assert.Equal(2, Assert.Single(loaded.Profile.RegistrationCertificateDocuments).PageCount);
        Assert.Equal("license.jpg", Assert.Single(loaded.Profile.DesignLicenseDocuments).OriginalFileName);
    }

    [Fact]
    public void LogoIsCopiedIntoTheAccountAssetFolder()
    {
        var store = new CompanyLibraryStore(
            Path.Combine(workDirectory, "companies.json"),
            Path.Combine(workDirectory, "logos"));
        string source = Path.Combine(workDirectory, "brand.png");
        File.WriteAllBytes(source, [0x89, 0x50, 0x4e, 0x47]);

        string stored = store.StoreLogo("org-1", source);

        Assert.True(File.Exists(stored));
        Assert.StartsWith(Path.Combine(workDirectory, "logos"), stored, StringComparison.OrdinalIgnoreCase);
    }
}
