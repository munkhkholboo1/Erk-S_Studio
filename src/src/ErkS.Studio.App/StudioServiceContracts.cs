using ErkS.CloudEra.Client;

using ErkS.Platform.Core;

namespace ErkS.Studio;

internal interface IStudioSessionClient
{
    StudioAccountSession? Current { get; }
    CloudEraCapabilitiesSnapshot? CurrentCapabilities { get; }
    bool IsSignedIn { get; }
    string LastError { get; }
    string SuggestedServerUrl { get; }
    string SuggestedEmail { get; }
    event Action? StateChanged;
    Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default);
    void SignOut();
}

internal interface ILicenseClient
{
    Task SignInAsync(
        string serverUrl,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    void OpenAccountRegistration();
}

internal interface IProjectsClient
{
    Task<IReadOnlyList<StudioCloudProjectSummary>> ListProjectsAsync(
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> GetProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectRefreshResult> GetProjectChangesAsync(
        string projectId,
        string knownConcurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> UpdateProjectInformationAsync(
        string projectId,
        StudioCloudProjectInformationUpdateRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> UpdateBuildingCompositionAsync(
        string projectId,
        StudioCloudBuildingCompositionUpdateRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> UploadProjectClientLogoAsync(
        string projectId,
        string logoPath,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> DeleteProjectClientLogoAsync(
        string projectId,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> CreateProjectAsync(
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> AssignDesignOrganizationAsync(
        string projectId,
        string organizationId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudStageAdvanceResponse> AdvanceProjectStageAsync(
        string projectId,
        StudioCloudStageAdvanceRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteProjectAsync(
        string projectId,
        string confirmProjectCode,
        string reason,
        CancellationToken cancellationToken = default);
}

internal interface IOrganizationsClient
{
    Task<IReadOnlyList<StudioCloudOrganization>> ListOrganizationsAsync(
        CancellationToken cancellationToken = default);

    Task<StudioCloudOrganization> CreateOrganizationAsync(
        StudioCloudOrganizationUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioCloudOrganization> UpdateOrganizationAsync(
        string organizationId,
        StudioCloudOrganizationUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioOrganizationRegistryImportResponse> BeginOrganizationRegistryImportAsync(
        string organizationId,
        string registrationNumber,
        string baseConcurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioOrganizationRegistryImportResponse> GetOrganizationRegistryImportAsync(
        string organizationId,
        string importId,
        CancellationToken cancellationToken = default);

    Task DeleteOrganizationAsync(
        string organizationId,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioCloudOrganization> UploadOrganizationLogoAsync(
        string organizationId,
        string logoPath,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioCloudOrganization> DeleteOrganizationLogoAsync(
        string organizationId,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    Task<StudioDownloadedImage?> GetOrganizationLogoAsync(
        string logoUrl,
        CancellationToken cancellationToken = default);
}

internal interface ICollaborationClient
{
    Task<StudioCloudAccountLookupResponse> LookupAccountAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioProjectRole>> ListProjectRolesAsync(
        CancellationToken cancellationToken = default);

    Task<StudioProjectMembershipInvitationListResponse> ListMembershipInvitationsAsync(
        CancellationToken cancellationToken = default);

    Task<StudioProjectMembershipInvitation> InviteProjectMemberAsync(
        string projectId,
        string targetEmail,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> AcceptMembershipInvitationAsync(
        string invitationId,
        CancellationToken cancellationToken = default);

    Task DeclineMembershipInvitationAsync(
        string invitationId,
        CancellationToken cancellationToken = default);

    Task RevokeMembershipInvitationAsync(
        string projectId,
        string invitationId,
        CancellationToken cancellationToken = default);

    Task DeactivateParticipantAsync(
        string projectId,
        string participantId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> UpdateParticipantRolesAsync(
        string projectId,
        string participantId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> AssignConceptArchitectAsync(
        string projectId,
        string participantId,
        CancellationToken cancellationToken = default);

    Task<StudioProjectMembershipExitRequestListResponse> ListMembershipExitRequestsAsync(
        CancellationToken cancellationToken = default);

    Task<StudioProjectMembershipExitRequest> RequestProjectExitAsync(
        string projectId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<StudioProjectMembershipExitRequest> DecideProjectExitAsync(
        string requestId,
        bool approve,
        CancellationToken cancellationToken = default);

    Task<StudioProjectCreationGrantListResponse> ListProjectCreationGrantsAsync(
        CancellationToken cancellationToken = default);

    Task<StudioProjectCreationGrant> CreateProjectCreationGrantAsync(
        string organizationId,
        string targetEmail,
        CancellationToken cancellationToken = default);

    Task RevokeProjectCreationGrantAsync(
        string grantId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> CreateProjectFromGrantAsync(
        string grantId,
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default);
}

internal interface ISourcePackagesClient
{
    Task<StudioCloudSourcePackage> RegisterSourcePackageAsync(
        string projectId,
        StudioCloudSourcePackageCreateRequest value,
        CancellationToken cancellationToken = default);

    Task<StudioCloudSourcePackage> AssignSourceCustodianAsync(
        string projectId,
        string sourceId,
        string participantId,
        string projectConcurrencyToken,
        string expectedSourceId,
        CancellationToken cancellationToken = default);
}

internal interface IControlledDocumentsClient
{
    Task<IReadOnlyList<StudioCloudControlledDocument>> ListControlledDocumentsAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudControlledDocument> ReplaceControlledDocumentFilesAsync(
        string projectId,
        string documentId,
        int expectedDocumentVersion,
        string projectConcurrencyToken,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);

    Task DownloadControlledFileAsync(
        StudioCloudFile file,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

internal interface IAlbumsClient
{
    Task<IReadOnlyList<StudioCloudAlbum>> ListAlbumsAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioCloudDesignPackage>> ListDesignPackagesAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudAlbum> EnsureConceptAlbumAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudAlbumRevision> UploadAlbumRevisionAsync(
        string projectId,
        string albumId,
        string pdfPath,
        int pageCount,
        string pageSizeSummary,
        string projectConcurrencyToken,
        string? expectedBaseRevisionId = null,
        bool inheritComponentManifest = false,
        IReadOnlyList<StudioCloudAlbumSection>? componentManifest = null,
        CancellationToken cancellationToken = default);

    Task<StudioCloudAlbumRevision> SetAlbumComponentManifestAsync(
        string projectId,
        string albumId,
        string revisionId,
        string projectConcurrencyToken,
        string expectedBaseRevisionId,
        IReadOnlyList<StudioCloudAlbumSection> components,
        CancellationToken cancellationToken = default);

    Task<StudioCloudAlbumRevision> MergeAlbumComponentsAsync(
        string projectId,
        string albumId,
        string expectedRevisionId,
        string projectConcurrencyToken,
        IReadOnlyList<StudioAlbumComponentUpload> components,
        CancellationToken cancellationToken = default);

    Task DownloadAlbumRevisionPdfAsync(
        StudioCloudAlbumRevision revision,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

internal interface IProfileImageClient
{
    Task<byte[]?> GetProfileImageAsync(CancellationToken cancellationToken = default);
}

internal interface IProjectChatClient
{
    Task<StudioProjectChatResponse> GetProjectChatAsync(
        string projectId,
        int take = 100,
        string? peerEmail = null,
        CancellationToken cancellationToken = default);

    Task<StudioProjectChatResponse> SendProjectChatMessageAsync(
        string projectId,
        string message,
        string? attachmentPath = null,
        string? peerEmail = null,
        CancellationToken cancellationToken = default);

    Task<StudioProjectChatResponse> ReactToProjectChatMessageAsync(
        string projectId,
        string messageId,
        string reaction,
        string? peerEmail = null,
        CancellationToken cancellationToken = default);

    Task<byte[]?> DownloadProjectChatAssetAsync(
        string assetPath,
        CancellationToken cancellationToken = default);
}

internal interface IUpdatesClient
{
    Task<StudioUpdateLatestResponse> CheckAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadAsync(
        StudioUpdateLatestResponse update,
        IProgress<StudioUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task VerifyAndLaunchInstallerAsync(
        string installerPath,
        StudioUpdateLatestResponse update,
        CancellationToken cancellationToken = default);
}

internal interface ICredentialStore
{
    T? Read<T>(string target) where T : class;
    void Write<T>(string target, string userName, T value);
    void Delete(string target);
}

internal readonly record struct CloudEraClientContext(string ServerUrl, string AccessToken);

/// <summary>
/// What one collection brought back.
/// </summary>
/// <param name="SinceAccepted">
/// Whether the cursor Studio sent was understood and applied.
///
/// 🔴 IT EXISTS TO MAKE A SILENT COST VISIBLE. An unrecognised cursor makes the
/// server return everything - correct, and indistinguishable from an ordinary first
/// read. If the cursor format ever drifted, every collection would quietly re-download
/// the whole consultation forever: still working, still green, only slower and larger
/// each time. Sent a cursor and got false back means it was refused.
///
/// ⚠ False alone is not a fault: it is also what a first collection sees, because
/// there was no cursor to accept. Only the CALLER knows which case it is, which is why
/// the flag is reported rather than interpreted here.
/// </param>
internal readonly record struct StudioCitizenSurveyFetch(
    CitizenSurveyResponseDocument Document,
    bool SinceAccepted);

internal interface ICloudEraContractClient
{


    /// <summary>
    /// The citizens' answers collected for one survey since a watermark.
    ///
    /// 🔴 THE DOCUMENT COMES BACK IN Core's SHAPE, NOT THE WIRE'S. The route answers
    /// in camelCase and the file on disk is PascalCase; the conversion happens here, once,
    /// so nothing downstream has to know there are two spellings. Reusing the file reader
    /// on the wire body would have produced a document of nulls: no error, HTTP 200, an
    /// empty list, and a consultation that looked ignored.
    /// </summary>
    Task<StudioCitizenSurveyFetch> FetchCitizenSurveyResponsesAsync(
        CloudEraClientContext context,
        string projectId,
        string surveyId,
        string? since,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectListResponse> ListProjectsAsync(
        CloudEraClientContext context,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> GetProjectAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> CreateProjectAsync(
        CloudEraClientContext context,
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> AssignDesignOrganizationAsync(
        CloudEraClientContext context,
        string projectId,
        StudioCloudDesignOrganizationAssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioCloudStageAdvanceResponse> AdvanceProjectStageAsync(
        CloudEraClientContext context,
        string projectId,
        StudioCloudStageAdvanceRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> UpdateParticipantRolesAsync(
        CloudEraClientContext context,
        string projectId,
        string participantId,
        StudioParticipantRoleUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioCloudProjectDetail> AssignConceptArchitectAsync(
        CloudEraClientContext context,
        string projectId,
        StudioConceptArchitectAssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioCloudDesignPackage>> ListDesignPackagesAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioCloudAlbum>> ListAlbumsAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudAlbum> EnsureConceptAlbumAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default);

    Task<StudioCloudSourcePackage> RegisterSourcePackageAsync(
        CloudEraClientContext context,
        string projectId,
        StudioCloudSourcePackageCreateRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class WindowsCredentialStore : ICredentialStore
{
    public T? Read<T>(string target) where T : class => WindowsCredentialVault.Read<T>(target);

    public void Write<T>(string target, string userName, T value) =>
        WindowsCredentialVault.Write(target, userName, value);

    public void Delete(string target) => WindowsCredentialVault.Delete(target);
}
