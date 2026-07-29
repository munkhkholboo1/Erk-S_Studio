using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSourceRefreshAlbumPolicyTests
{
    [Fact]
    public void MissingKnownCloudSheetsWithoutCanonicalCache_DefersSourceRefreshBuild()
    {
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                hasCachedCanonicalAlbum: false,
                albumSheetKeys:
                [
                    "local-source|sheet-1",
                    "collaborator-source|sheet-2",
                ],
                verifiedSheetKeys: ["local-source|sheet-1"],
                authorizedLocalSourceIds: ["local-source"],
                knownCloudSourceIdentities:
                [
                    "local-source",
                    "collaborator-source",
                ],
                buildIssues:
                [
                    "Album sheet 'collaborator-source|sheet-2' is missing or unverified.",
                ]);

        Assert.True(resolution.ShouldDefer);
        Assert.Equal(
            ["collaborator-source|sheet-2"],
            resolution.UnavailableCloudSheetKeys);
    }

    [Fact]
    public void MissingAuthorizedLocalSheet_IsNeverDeferred()
    {
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                hasCachedCanonicalAlbum: true,
                albumSheetKeys: ["local-source|sheet-1"],
                verifiedSheetKeys: [],
                authorizedLocalSourceIds: ["local-source"],
                knownCloudSourceIdentities: ["local-source"],
                buildIssues:
                [
                    "Album sheet 'local-source|sheet-1' is missing or unverified.",
                ]);

        Assert.False(resolution.ShouldDefer);
    }

    [Fact]
    public void UnknownMissingSheet_IsNeverAssumedToBeCloudOnly()
    {
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                hasCachedCanonicalAlbum: true,
                albumSheetKeys: ["unknown-source|sheet-1"],
                verifiedSheetKeys: [],
                authorizedLocalSourceIds: [],
                knownCloudSourceIdentities: ["collaborator-source"],
                buildIssues:
                [
                    "Album sheet 'unknown-source|sheet-1' is missing or unverified.",
                ]);

        Assert.False(resolution.ShouldDefer);
    }

    [Fact]
    public void NonMissingSheetBuildFailure_IsNeverDeferred()
    {
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                hasCachedCanonicalAlbum: false,
                albumSheetKeys: ["collaborator-source|sheet-1"],
                verifiedSheetKeys: [],
                authorizedLocalSourceIds: [],
                knownCloudSourceIdentities: ["collaborator-source"],
                buildIssues: ["Local PDF hash changed after intake."]);

        Assert.False(resolution.ShouldDefer);
    }

    [Fact]
    public void ExistingCanonicalPdfWithUnusableManifest_DefersKnownCloudOnlySheets()
    {
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                StudioWorkspaceOperation.SourceRefresh,
                isCloudLinked: true,
                hasCachedCanonicalAlbum: true,
                albumSheetKeys: ["collaborator-source|sheet-1"],
                verifiedSheetKeys: [],
                authorizedLocalSourceIds: [],
                knownCloudSourceIdentities: ["collaborator-source"],
                buildIssues:
                [
                    "Album sheet 'collaborator-source|sheet-1' is missing or unverified.",
                ]);

        Assert.True(resolution.ShouldDefer);
        Assert.Equal(
            ["collaborator-source|sheet-1"],
            resolution.UnavailableCloudSheetKeys);
    }

    [Fact]
    public void ExplicitAlbumEdit_DoesNotHideMissingCloudSheet()
    {
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                StudioWorkspaceOperation.ExplicitAlbumEdit,
                isCloudLinked: true,
                hasCachedCanonicalAlbum: false,
                albumSheetKeys: ["collaborator-source|sheet-1"],
                verifiedSheetKeys: [],
                authorizedLocalSourceIds: [],
                knownCloudSourceIdentities: ["collaborator-source"],
                buildIssues:
                [
                    "Album sheet 'collaborator-source|sheet-1' is missing or unverified.",
                ]);

        Assert.False(resolution.ShouldDefer);
    }

    [Fact]
    public void LocalPdfPageEdit_WithoutUsableCanonicalManifestDefersOnlyKnownCloudSheets()
    {
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                StudioWorkspaceOperation.LocalPdfPageEdit,
                isCloudLinked: true,
                hasCachedCanonicalAlbum: false,
                albumSheetKeys:
                [
                    "local-source|sheet-1",
                    "collaborator-source|sheet-2",
                ],
                verifiedSheetKeys: ["local-source|sheet-1"],
                authorizedLocalSourceIds: ["local-source"],
                knownCloudSourceIdentities:
                [
                    "local-source",
                    "collaborator-source",
                ],
                buildIssues:
                [
                    "Album sheet 'collaborator-source|sheet-2' is missing or unverified.",
                ]);

        Assert.True(resolution.ShouldDefer);
        Assert.Equal(
            "pdf_page_edit_cloud_album_deferred",
            resolution.ReasonCode);
        Assert.Equal(
            ["collaborator-source|sheet-2"],
            resolution.UnavailableCloudSheetKeys);
    }
}
