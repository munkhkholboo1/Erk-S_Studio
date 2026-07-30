using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCompanySnapshotRefreshPolicyTests
{
    [Fact]
    public void PassiveCatalogHydrationWithTransportOnlyChangesDoesNotCreateAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.LogoPath = "project-assets/logo.png";
        previous.RegistrationCertificateDocuments =
        [
            Document(
                "certificate",
                "same-document-sha",
                "project-assets/certificate.pdf",
                isAvailable: true),
        ];
        CompanyProfile current = previous.Clone();
        current.LogoPath = "company-cache/logo.png";
        current.RegistrationCertificateDocuments =
        [
            Document(
                "certificate",
                "same-document-sha",
                "company-cache/certificate.pdf",
                isAvailable: true),
        ];

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "same-logo-sha");

        Assert.False(renderChanged);
        Assert.False(
            StudioCompanySnapshotRefreshPolicy.ShouldMarkAlbumDirty(
                snapshotChanged: true,
                StudioCompanySnapshotRefreshOrigin.PassiveCatalogHydration,
                renderChanged));
    }

    [Fact]
    public void PassiveCatalogHydrationWithCompanyTextChangeCreatesAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        CompanyProfile current = previous.Clone();
        current.DisplayName = "Updated Design Company";

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "");

        Assert.True(renderChanged);
        Assert.True(
            StudioCompanySnapshotRefreshPolicy.ShouldMarkAlbumDirty(
                snapshotChanged: true,
                StudioCompanySnapshotRefreshOrigin.PassiveCatalogHydration,
                renderChanged));
    }

    [Fact]
    public void PassiveCatalogHydrationWithNonRenderedCompanyChangesDoesNotCreateAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        CompanyProfile current = previous.Clone();
        current.OrganizationId = "updated-organization";
        current.LegalEntityType = "Updated legal entity type";
        current.ActivityDirections = ["Updated activity"];
        current.RegisteredAtUtc = DateTimeOffset.Parse("2026-07-30T00:00:00Z");
        current.OfficialRepresentativeName = "Updated official representative";
        current.OrganizationType = "Updated organization type";
        current.RegisteredCity = "Updated city";
        current.Address = "Updated address";
        current.PhoneNumbers = ["75555555"];
        current.Phone = "75555555";
        current.Email = "updated@example.com";
        current.WebSite = "https://updated.example.com";
        current.LicenseScope = "Updated scope";
        current.LicenseNumber = "UPDATED-001";

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "");

        Assert.False(renderChanged);
        Assert.False(
            StudioCompanySnapshotRefreshPolicy.ShouldMarkAlbumDirty(
                snapshotChanged: true,
                StudioCompanySnapshotRefreshOrigin.PassiveCatalogHydration,
                renderChanged));
    }

    [Fact]
    public void RegistrationNumberChangeWithDocumentPlaceholdersCreatesAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        CompanyProfile current = previous.Clone();
        current.RegistrationNumber = "7654321";

        Assert.True(
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => ""));
    }

    [Fact]
    public void RegistrationNumberChangeWithBothDocumentKindsAvailableDoesNotCreateAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.RegistrationCertificateDocuments =
        [
            Document("certificate", "certificate-sha", "certificate.pdf", isAvailable: true),
        ];
        previous.DesignLicenseDocuments =
        [
            Document("license", "license-sha", "license.pdf", isAvailable: true),
        ];
        CompanyProfile current = previous.Clone();
        current.RegistrationNumber = "7654321";

        Assert.False(
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => ""));
    }

    [Fact]
    public void LegalFormChangeCreatesAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        CompanyProfile current = previous.Clone();
        current.LegalForm = "ХХК";

        Assert.True(
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => ""));
    }

    [Fact]
    public void PassiveCatalogHydrationWithDocumentContentChangeCreatesAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.DesignLicenseDocuments =
        [
            Document("license", "old-sha", "old.pdf", isAvailable: true),
        ];
        CompanyProfile current = previous.Clone();
        current.DesignLicenseDocuments =
        [
            Document("license", "new-sha", "new.pdf", isAvailable: true),
        ];

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "");

        Assert.True(renderChanged);
        Assert.True(
            StudioCompanySnapshotRefreshPolicy.ShouldMarkAlbumDirty(
                snapshotChanged: true,
                StudioCompanySnapshotRefreshOrigin.PassiveCatalogHydration,
                renderChanged));
    }

    [Fact]
    public void PassiveCatalogHydrationWithLogoPlacementChangeCreatesAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.LogoPath = "logo-a.png";
        CompanyProfile current = previous.Clone();
        current.LogoPath = "logo-b.png";
        current.LogoOffsetX = 0.25d;

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "same-logo-sha");

        Assert.True(renderChanged);
    }

    [Fact]
    public void PassiveCatalogHydrationWithOverwrittenStableLogoPathUsesCapturedPreviousContent()
    {
        CompanyProfile previous = Profile();
        previous.LogoPath = "company-cache/logo.png";
        CompanyProfile current = previous.Clone();

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "new-logo-sha",
                previousLogoContentIdentity: "old-logo-sha");

        Assert.True(renderChanged);
        Assert.True(
            StudioCompanySnapshotRefreshPolicy.ShouldMarkAlbumDirty(
                snapshotChanged: true,
                StudioCompanySnapshotRefreshOrigin.PassiveCatalogHydration,
                renderChanged));
    }

    [Fact]
    public void CapturedRenderIdentitySurvivesLaterStablePathOverwrite()
    {
        CompanyProfile previous = Profile();
        previous.LogoPath = "company-cache/logo.png";
        CompanyProfile current = previous.Clone();
        string currentLogoIdentity = "old-logo-sha";
        string previousIdentity =
            StudioCompanySnapshotRefreshPolicy.CaptureAlbumRenderIdentity(
                previous,
                _ => currentLogoIdentity);

        currentLogoIdentity = "new-logo-sha";

        Assert.True(
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previousIdentity,
                current,
                _ => currentLogoIdentity));
    }

    [Fact]
    public void DocumentTitleChangeDoesNotCreateAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.DesignLicenseDocuments =
        [
            Document("license", "same-sha", "license.pdf", isAvailable: true),
        ];
        CompanyProfile current = previous.Clone();
        current.DesignLicenseDocuments[0].Title = "Updated internal title";

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "");

        Assert.False(renderChanged);
    }

    [Fact]
    public void DocumentAvailabilityChangeCreatesAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.DesignLicenseDocuments =
        [
            Document("license", "same-sha", "license.pdf", isAvailable: true),
        ];
        CompanyProfile current = previous.Clone();
        current.DesignLicenseDocuments[0].IsAvailable = false;

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "");

        Assert.True(renderChanged);
    }

    [Fact]
    public void SinglePageDocumentOriginalFileNameChangeCreatesAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.DesignLicenseDocuments =
        [
            Document(
                "license",
                "same-sha",
                "license.pdf",
                isAvailable: true,
                originalFileName: "license-old.pdf",
                pageCount: 1),
        ];
        CompanyProfile current = previous.Clone();
        current.DesignLicenseDocuments[0].OriginalFileName = "license-new.pdf";

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "");

        Assert.True(renderChanged);
    }

    [Fact]
    public void MultiPageDocumentOriginalFileNameChangeDoesNotCreateAlbumDirtyWork()
    {
        CompanyProfile previous = Profile();
        previous.DesignLicenseDocuments =
        [
            Document(
                "license",
                "same-sha",
                "license.pdf",
                isAvailable: true,
                originalFileName: "license-old.pdf",
                pageCount: 2),
        ];
        CompanyProfile current = previous.Clone();
        current.DesignLicenseDocuments[0].OriginalFileName = "license-new.pdf";

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ => "");

        Assert.False(renderChanged);
    }

    [Fact]
    public void CapturedRenderIdentityDetectsContentOverwrittenAtStablePaths()
    {
        CompanyProfile previous = Profile();
        previous.LogoPath = "company-cache/logo.png";
        previous.DesignLicenseDocuments =
        [
            Document("license", "", "company-cache/license.pdf", isAvailable: true),
        ];
        CompanyProfile current = previous.Clone();
        string previousIdentity =
            StudioCompanySnapshotRefreshPolicy.CaptureAlbumRenderIdentity(
                previous,
                _ => "old-content-sha");

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previousIdentity,
                current,
                _ => "new-content-sha");

        Assert.True(renderChanged);
    }

    [Fact]
    public void StoredDocumentShaAvoidsHashingDocumentPaths()
    {
        CompanyProfile previous = Profile();
        previous.DesignLicenseDocuments =
        [
            Document("license", "same-sha", "license.pdf", isAvailable: true),
        ];
        CompanyProfile current = previous.Clone();
        int hashRequests = 0;

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ =>
                {
                    hashRequests++;
                    return "unexpected-hash";
                });

        Assert.False(renderChanged);
        Assert.Equal(0, hashRequests);
    }

    [Fact]
    public void StoredServerRevisionAvoidsHashingDocumentPaths()
    {
        CompanyProfile previous = Profile();
        previous.DesignLicenseDocuments =
        [
            Document("license", "", "license.pdf", isAvailable: true),
        ];
        previous.DesignLicenseDocuments[0].ServerFileRevisionId =
            "server-revision";
        CompanyProfile current = previous.Clone();
        int hashRequests = 0;

        bool renderChanged =
            StudioCompanySnapshotRefreshPolicy.HasAlbumRenderChanges(
                previous,
                current,
                _ =>
                {
                    hashRequests++;
                    return "unexpected-hash";
                });

        Assert.False(renderChanged);
        Assert.Equal(0, hashRequests);
    }

    [Fact]
    public void LocalAssetReconciliationMarksChangedSnapshotDirty()
    {
        Assert.True(
            StudioCompanySnapshotRefreshPolicy.ShouldMarkAlbumDirty(
                snapshotChanged: true,
                StudioCompanySnapshotRefreshOrigin.LocalAssetReconciliation,
                albumRenderChanged: false));
    }

    [Fact]
    public void UnchangedSnapshotNeverCreatesAlbumDirtyWork()
    {
        Assert.False(
            StudioCompanySnapshotRefreshPolicy.ShouldMarkAlbumDirty(
                snapshotChanged: false,
                StudioCompanySnapshotRefreshOrigin.LocalAssetReconciliation,
                albumRenderChanged: true));
    }

    private static CompanyProfile Profile() => new()
    {
        OrganizationId = "organization",
        Name = "Design Company LLC",
        DisplayName = "Design Company",
        ShortName = "DC",
        RegistrationNumber = "1234567",
        LegalForm = "LLC",
        PhoneNumbers = ["70000000"],
        LicenseScope = "Architecture",
        LicenseNumber = "AR-001",
        DesignRepresentativeTitle = "Director",
        DesignRepresentativeName = "Representative",
    };

    private static ProjectFileReference Document(
        string category,
        string sha256,
        string path,
        bool isAvailable,
        string? originalFileName = null,
        int pageCount = 2) => new()
    {
        Category = category,
        Title = category,
        Sha256 = sha256,
        OriginalFileName = originalFileName ?? Path.GetFileName(path),
        PageCount = pageCount,
        RelativePath = path,
        LinkedSourcePath = path,
        IsAvailable = isAvailable,
    };
}
