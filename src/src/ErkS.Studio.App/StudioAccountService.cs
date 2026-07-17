using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioAccountSession(
    string ServerUrl,
    string Email,
    string DisplayName,
    string ProfileImageUrl,
    string LicenseType,
    DateTimeOffset LicenseExpiresAtUtc,
    DateTimeOffset TokenExpiresAtUtc,
    string AccessToken);

internal sealed class StudioAccountException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public string ErrorCode { get; } = "";

    public StudioAccountException(string message) : base(message)
    {
    }

    public StudioAccountException(string message, System.Net.HttpStatusCode statusCode, string errorCode) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

internal sealed class StudioAccountService : IDisposable
{
    public const string ProductCode = "ErkS.Studio";
    private const string PublicServerUrl = "https://erk-s.mn";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string metadataPath = Path.Combine(ResolveAccountDataRoot(), "account.json");
    private StudioAccountMetadata? metadata;
    private StudioActivationCredential? credential;

    public StudioAccountSession? Current { get; private set; }

    private static string ResolveAccountDataRoot()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT");
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Erk-S Studio")
            : Path.GetFullPath(configuredRoot);
    }

    public bool IsSignedIn => Current is not null;

    public string LastError { get; private set; } = "";

    public string SuggestedServerUrl =>
        NormalizeOptionalServerUrl(Environment.GetEnvironmentVariable("ERKS_STUDIO_SERVER_URL"))
        ?? ReadMetadata()?.ServerUrl
        ?? (StudioReleaseInfo.IsDevelopmentBuild
            ? NormalizeOptionalServerUrl(Environment.GetEnvironmentVariable("ERKS_SERVER_URL"))
            : null)
        ?? (StudioReleaseInfo.IsDevelopmentBuild ? FindLinkedProjectServerUrl() : null)
        ?? PublicServerUrl;

    public string SuggestedEmail => ReadMetadata()?.Email ?? "";

    public event Action? StateChanged;

    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        metadata = ReadMetadata();
        if (metadata is null)
            return false;
        credential = WindowsCredentialVault.Read<StudioActivationCredential>(CredentialTarget(metadata));
        if (credential is null)
            return false;

        try
        {
            await RefreshInternalAsync(metadata, credential, cancellationToken).ConfigureAwait(true);
            LastError = "";
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or StudioAccountException or Win32Exception)
        {
            Current = null;
            LastError = exception.Message;
            StateChanged?.Invoke();
            return false;
        }
    }

    public async Task SignInAsync(string serverUrl, string email, string password, CancellationToken cancellationToken = default)
    {
        string normalizedServer = NormalizeServerUrl(serverUrl);
        string normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !normalizedEmail.Contains('@'))
            throw new StudioAccountException("И-мэйл хаягаа зөв оруулна уу.");
        if (string.IsNullOrWhiteSpace(password))
            throw new StudioAccountException("Нууц үгээ оруулна уу.");

        string fingerprint = StudioDeviceIdentity.Fingerprint;
        var activateRequest = new
        {
            productCode = ProductCode,
            email = normalizedEmail,
            password,
            deviceFingerprint = fingerprint,
            deviceName = Environment.MachineName,
            appVersion = AppVersion,
        };
        StudioLicenseResponse license = await PostAsync<object, StudioLicenseResponse>(
            normalizedServer,
            "/api/license/activate",
            activateRequest,
            cancellationToken).ConfigureAwait(true);
        if (!license.IsValid)
            throw new StudioAccountException(string.IsNullOrWhiteSpace(license.Message)
                ? "Erk-S Studio лиценз идэвхжсэнгүй."
                : license.Message);

        var sessionRequest = new
        {
            email = normalizedEmail,
            password,
            clientName = "Erk-S Studio",
            productCode = ProductCode,
            licenseId = license.LicenseId,
            activationId = license.ActivationId,
            deviceFingerprint = fingerprint,
            deviceName = Environment.MachineName,
            appVersion = AppVersion,
        };
        StudioSessionResponse session = await PostAsync<object, StudioSessionResponse>(
            normalizedServer,
            "/api/studio/session",
            sessionRequest,
            cancellationToken).ConfigureAwait(true);

        metadata = new StudioAccountMetadata
        {
            ServerUrl = normalizedServer,
            Email = normalizedEmail,
            DisplayName = session.DisplayName,
            ProfileImageUrl = session.ProfileImageUrl,
            LicenseType = session.LicenseType,
            LicenseExpiresAtUtc = session.LicenseExpiresAtUtc,
        };
        credential = new StudioActivationCredential
        {
            LicenseId = session.LicenseId,
            ActivationId = session.ActivationId,
            DeviceFingerprint = fingerprint,
        };
        WindowsCredentialVault.Write(CredentialTarget(metadata), normalizedEmail, credential);
        WriteMetadata(metadata);
        SetCurrent(metadata, session);
        LastError = "";
    }

    public async Task<IReadOnlyList<StudioCloudProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudProjectListResponse response = await GetAuthorizedAsync<StudioCloudProjectListResponse>(
            "/api/cloud-era/v1/projects",
            cancellationToken).ConfigureAwait(true);
        return response.Projects;
    }

    public async Task<StudioCloudProjectDetail> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioCloudProjectDetail>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId),
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudAccountLookupResponse> LookupAccountAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioCloudAccountLookupResponse>(
            "/api/cloud-era/v1/accounts/lookup?email=" + Uri.EscapeDataString(NormalizeEmail(email)),
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<StudioProjectRole>> ListProjectRolesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioProjectRoleListResponse response = await GetAuthorizedAsync<StudioProjectRoleListResponse>(
            "/api/cloud-era/v1/project-roles",
            cancellationToken).ConfigureAwait(true);
        return response.Roles;
    }

    public async Task<StudioProjectMembershipInvitationListResponse> ListMembershipInvitationsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioProjectMembershipInvitationListResponse>(
            "/api/cloud-era/v1/project-membership-invitations",
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioProjectMembershipInvitation> InviteProjectMemberAsync(
        string projectId,
        string targetEmail,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioProjectMembershipInvitationCreateRequest, StudioProjectMembershipInvitation>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/membership-invitations",
            new StudioProjectMembershipInvitationCreateRequest
            {
                TargetEmail = NormalizeEmail(targetEmail),
                Roles = roles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            },
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> AcceptMembershipInvitationAsync(
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudProjectDetail>(
            "/api/cloud-era/v1/project-membership-invitations/" + Uri.EscapeDataString(invitationId) + "/accept",
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task DeclineMembershipInvitationAsync(
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        await SendAuthorizedNoContentAsync(
            HttpMethod.Post,
            "/api/cloud-era/v1/project-membership-invitations/" + Uri.EscapeDataString(invitationId) + "/decline",
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task RevokeMembershipInvitationAsync(
        string projectId,
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        await SendAuthorizedNoContentAsync(
            HttpMethod.Delete,
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/membership-invitations/" + Uri.EscapeDataString(invitationId),
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task DeactivateParticipantAsync(
        string projectId,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        await SendAuthorizedNoContentAsync(
            HttpMethod.Delete,
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/participants/" + Uri.EscapeDataString(participantId),
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<StudioProjectMembershipExitRequestListResponse> ListMembershipExitRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioProjectMembershipExitRequestListResponse>(
            "/api/cloud-era/v1/project-membership-exit-requests",
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioProjectMembershipExitRequest> RequestProjectExitAsync(
        string projectId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioProjectMembershipExitRequestCreateRequest, StudioProjectMembershipExitRequest>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/membership-exit-requests",
            new StudioProjectMembershipExitRequestCreateRequest { Reason = reason?.Trim() ?? "" },
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<StudioProjectMembershipExitRequest> DecideProjectExitAsync(
        string requestId,
        bool approve,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        string decision = approve ? "approve" : "decline";
        return await PostAuthorizedAsync<StudioProjectMembershipExitRequest>(
            "/api/cloud-era/v1/project-membership-exit-requests/" + Uri.EscapeDataString(requestId) + "/" + decision,
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<StudioCloudSourcePackage> AssignSourceCustodianAsync(
        string projectId,
        string sourceKey,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PutAuthorizedAsync<StudioCloudSourceCustodianAssignRequest, StudioCloudSourcePackage>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/sources/" + Uri.EscapeDataString(sourceKey) + "/custodian",
            new StudioCloudSourceCustodianAssignRequest { ParticipantId = participantId },
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<StudioProjectCreationGrantListResponse> ListProjectCreationGrantsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioProjectCreationGrantListResponse>(
            "/api/cloud-era/v1/project-creation-grants",
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioProjectCreationGrant> CreateProjectCreationGrantAsync(
        string organizationId,
        string targetEmail,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioProjectCreationGrantCreateRequest, StudioProjectCreationGrant>(
            "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId) + "/project-creation-grants",
            new StudioProjectCreationGrantCreateRequest { TargetEmail = NormalizeEmail(targetEmail) },
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task RevokeProjectCreationGrantAsync(
        string grantId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        await SendAuthorizedNoContentAsync(
            HttpMethod.Delete,
            "/api/cloud-era/v1/project-creation-grants/" + Uri.EscapeDataString(grantId),
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> CreateProjectFromGrantAsync(
        string grantId,
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudProjectCreateRequest, StudioCloudProjectDetail>(
            "/api/cloud-era/v1/project-creation-grants/" + Uri.EscapeDataString(grantId) + "/projects",
            request,
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<StudioCloudOrganization>> ListOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudOrganizationListResponse response = await GetAuthorizedAsync<StudioCloudOrganizationListResponse>(
            "/api/cloud-era/v1/organizations",
            cancellationToken).ConfigureAwait(true);
        return response.Organizations;
    }

    public async Task<StudioCloudOrganization> CreateOrganizationAsync(
        StudioCloudOrganizationUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudOrganizationUpsertRequest, StudioCloudOrganization>(
            "/api/cloud-era/v1/organizations",
            request,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudOrganization> UpdateOrganizationAsync(
        string organizationId,
        StudioCloudOrganizationUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PutAuthorizedAsync<StudioCloudOrganizationUpsertRequest, StudioCloudOrganization>(
            "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId),
            request,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudOrganization> UploadOrganizationLogoAsync(
        string organizationId,
        string logoPath,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        if (!File.Exists(logoPath))
            throw new StudioAccountException("Сонгосон лого файл олдсонгүй.");
        var info = new FileInfo(logoPath);
        if (info.Length > 5L * 1024L * 1024L)
            throw new StudioAccountException("Лого 5 MB-аас ихгүй байна.");
        string contentType = Path.GetExtension(logoPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new StudioAccountException("Лого PNG эсвэл JPEG зураг байна."),
        };

        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using FileStream source = File.OpenRead(logoPath);
        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(source);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", Path.GetFileName(logoPath));
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            BuildUri(session.ServerUrl, "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId) + "/logo"))
        {
            Content = form,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudOrganization>(response, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudOrganization> DeleteOrganizationLogoAsync(
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            BuildUri(session.ServerUrl, "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId) + "/logo"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudOrganization>(response, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioDownloadedImage?> GetOrganizationLogoAsync(
        string logoUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(logoUrl))
            return null;
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, logoUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return await ReadResponseAsync<StudioDownloadedImage?>(response, cancellationToken).ConfigureAwait(true);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType is not "image/png" and not "image/jpeg")
            throw new StudioAccountException("Компанийн лого PNG эсвэл JPEG зураг биш байна.");
        if (response.Content.Headers.ContentLength > 5L * 1024L * 1024L)
            throw new StudioAccountException("Компанийн лого 5 MB-аас их байна.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
        if (bytes.Length > 5L * 1024L * 1024L)
            throw new StudioAccountException("Компанийн лого 5 MB-аас их байна.");
        return new StudioDownloadedImage(bytes, contentType);
    }

    public async Task<IReadOnlyList<StudioCloudAlbum>> ListAlbumsAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<List<StudioCloudAlbum>>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/albums",
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<StudioCloudDesignPackage>> ListDesignPackagesAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<List<StudioCloudDesignPackage>>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/design-packages",
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudAlbum> EnsureConceptAlbumAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            return await PostAuthorizedAsync<StudioCloudAlbum>(
                "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/albums/ensure",
                cancellationToken).ConfigureAwait(true);
        }
        catch (StudioAccountException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            IReadOnlyList<StudioCloudAlbum> albums = await ListAlbumsAsync(projectId, cancellationToken).ConfigureAwait(true);
            return albums.FirstOrDefault(item =>
                    item.AlbumType.Equals(ProjectWorkspace.BuildingArchitectureConcept, StringComparison.OrdinalIgnoreCase))
                ?? albums.SingleOrDefault()
                ?? throw new StudioAccountException(
                    "Cloud ERA server empty template album үүсгэх шинэчлэлгүй байна. Server update хийнэ үү.");
        }
    }

    public async Task<StudioCloudSourcePackage> RegisterSourcePackageAsync(
        string projectId,
        StudioCloudSourcePackageCreateRequest value,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudSourcePackageCreateRequest, StudioCloudSourcePackage>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/source-packages",
            value,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudAlbumRevision> UploadAlbumRevisionAsync(
        string projectId,
        string albumId,
        string pdfPath,
        int pageCount,
        string pageSizeSummary,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pdfPath))
            throw new StudioAccountException("Синк хийх альбумын PDF олдсонгүй.");
        if (pageCount < 1)
            throw new StudioAccountException("Альбумын хуудасны тоо буруу байна.");

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(pageCount.ToString(CultureInfo.InvariantCulture)), "pageCount");
        content.Add(new StringContent(pageSizeSummary ?? ""), "pageSizeSummary");
        await using FileStream stream = File.OpenRead(pdfPath);
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", Path.GetFileName(pdfPath));

        string path = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/albums/" + Uri.EscapeDataString(albumId) + "/revisions";
        using HttpRequestMessage request = new(HttpMethod.Post, BuildUri(session.ServerUrl, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudAlbumRevision>(response, cancellationToken).ConfigureAwait(true);
    }

    public async Task<byte[]?> GetProfileImageAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        string profileImagePath = string.IsNullOrWhiteSpace(session.ProfileImageUrl)
            ? "/api/studio/profile/photo"
            : session.ProfileImageUrl;

        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, profileImagePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return await ReadResponseAsync<byte[]?>(response, cancellationToken).ConfigureAwait(true);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new StudioAccountException("Profile зураг image content type биш байна.");
        if (response.Content.Headers.ContentLength > 10L * 1024L * 1024L)
            throw new StudioAccountException("Profile зураг хэт том байна.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
        return bytes.Length > 10L * 1024L * 1024L
            ? throw new StudioAccountException("Profile зураг хэт том байна.")
            : bytes;
    }

    public async Task<StudioCloudProjectDetail> CreateProjectAsync(
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudProjectCreateRequest, StudioCloudProjectDetail>(
            "/api/cloud-era/v1/projects",
            request,
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> AssignDesignOrganizationAsync(
        string projectId,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PutAuthorizedAsync<StudioCloudDesignOrganizationAssignmentRequest, StudioCloudProjectDetail>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/design-organization",
            new StudioCloudDesignOrganizationAssignmentRequest { OrganizationId = organizationId },
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
    }

    public void OpenAccountRegistration()
    {
        string server = Current?.ServerUrl ?? SuggestedServerUrl;
        Process.Start(new ProcessStartInfo(server.TrimEnd('/') + "/register") { UseShellExecute = true });
    }

    public void SignOut()
    {
        StudioAccountMetadata? saved = metadata ?? ReadMetadata();
        if (saved is not null)
            WindowsCredentialVault.Delete(CredentialTarget(saved));
        try
        {
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
        }
        catch (IOException)
        {
        }
        Current = null;
        metadata = null;
        credential = null;
        LastError = "";
        StateChanged?.Invoke();
    }

    public void Dispose() => httpClient.Dispose();

    private async Task EnsureFreshSessionAsync(CancellationToken cancellationToken)
    {
        if (Current is not null && Current.TokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            return;
        metadata ??= ReadMetadata();
        if (metadata is null)
            throw new StudioAccountException("Erk-S Studio бүртгэлээр нэвтэрнэ үү.");
        credential ??= WindowsCredentialVault.Read<StudioActivationCredential>(CredentialTarget(metadata));
        if (credential is null)
            throw new StudioAccountException("Studio лицензийн төхөөрөмжийн activation олдсонгүй. Дахин нэвтэрнэ үү.");
        await RefreshInternalAsync(metadata, credential, cancellationToken).ConfigureAwait(true);
    }

    private async Task RefreshInternalAsync(
        StudioAccountMetadata savedMetadata,
        StudioActivationCredential savedCredential,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            email = savedMetadata.Email,
            productCode = ProductCode,
            licenseId = savedCredential.LicenseId,
            activationId = savedCredential.ActivationId,
            deviceFingerprint = savedCredential.DeviceFingerprint,
            deviceName = Environment.MachineName,
            appVersion = AppVersion,
        };
        StudioSessionResponse session = await PostAsync<object, StudioSessionResponse>(
            savedMetadata.ServerUrl,
            "/api/studio/session/refresh",
            request,
            cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(session.LicenseId) &&
            !string.IsNullOrWhiteSpace(session.ActivationId) &&
            (!savedCredential.LicenseId.Equals(session.LicenseId, StringComparison.Ordinal) ||
             !savedCredential.ActivationId.Equals(session.ActivationId, StringComparison.Ordinal)))
        {
            savedCredential.LicenseId = session.LicenseId;
            savedCredential.ActivationId = session.ActivationId;
            credential = savedCredential;
            WindowsCredentialVault.Write(CredentialTarget(savedMetadata), savedMetadata.Email, savedCredential);
        }
        savedMetadata.LicenseType = session.LicenseType;
        savedMetadata.LicenseExpiresAtUtc = session.LicenseExpiresAtUtc;
        savedMetadata.DisplayName = session.DisplayName;
        savedMetadata.ProfileImageUrl = session.ProfileImageUrl;
        WriteMetadata(savedMetadata);
        SetCurrent(savedMetadata, session);
    }

    private void SetCurrent(StudioAccountMetadata savedMetadata, StudioSessionResponse session)
    {
        if (string.IsNullOrWhiteSpace(session.AccessToken))
            throw new StudioAccountException("Cloud ERA session token хоосон байна.");
        string displayName = session.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName) &&
            !string.IsNullOrWhiteSpace(savedMetadata.DisplayName) &&
            !savedMetadata.DisplayName.Equals(session.AccountEmail, StringComparison.OrdinalIgnoreCase))
        {
            displayName = savedMetadata.DisplayName.Trim();
        }
        Current = new StudioAccountSession(
            savedMetadata.ServerUrl,
            session.AccountEmail,
            displayName,
            session.ProfileImageUrl,
            session.LicenseType,
            session.LicenseExpiresAtUtc,
            session.ExpiresAtUtc,
            session.AccessToken);
        StateChanged?.Invoke();
    }

    private async Task<TResponse> GetAuthorizedAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    private async Task<TResponse> PostAuthorizedAsync<TRequest, TResponse>(
        string path,
        TRequest value,
        CancellationToken cancellationToken,
        bool relationshipBoundaryAcknowledged = false)
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(HttpMethod.Post, BuildUri(session.ServerUrl, path))
        {
            Content = JsonContent.Create(value, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        AddRelationshipBoundaryAcknowledgement(request, relationshipBoundaryAcknowledged);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    private async Task<TResponse> PostAuthorizedAsync<TResponse>(
        string path,
        CancellationToken cancellationToken,
        bool relationshipBoundaryAcknowledged = false)
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(HttpMethod.Post, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        AddRelationshipBoundaryAcknowledgement(request, relationshipBoundaryAcknowledged);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    private async Task<TResponse> PutAuthorizedAsync<TRequest, TResponse>(
        string path,
        TRequest value,
        CancellationToken cancellationToken,
        bool relationshipBoundaryAcknowledged = false)
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(HttpMethod.Put, BuildUri(session.ServerUrl, path))
        {
            Content = JsonContent.Create(value, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        AddRelationshipBoundaryAcknowledgement(request, relationshipBoundaryAcknowledged);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    private async Task SendAuthorizedNoContentAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        bool relationshipBoundaryAcknowledged = false)
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio account is required.");
        using HttpRequestMessage request = new(method, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        AddRelationshipBoundaryAcknowledgement(request, relationshipBoundaryAcknowledged);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        if (response.IsSuccessStatusCode)
            return;
        await ReadResponseAsync<object>(response, cancellationToken).ConfigureAwait(true);
    }

    private static void AddRelationshipBoundaryAcknowledgement(
        HttpRequestMessage request,
        bool acknowledged)
    {
        if (acknowledged)
        {
            request.Headers.TryAddWithoutValidation(
                StudioRelationshipBoundary.HeaderName,
                StudioRelationshipBoundary.PolicyVersion);
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string serverUrl,
        string path,
        TRequest value,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            BuildUri(serverUrl, path),
            value,
            JsonOptions,
            cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    private static async Task<TResponse> ReadResponseAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            StudioCloudApiError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<StudioCloudApiError>(JsonOptions, cancellationToken).ConfigureAwait(true);
            }
            catch (JsonException)
            {
            }
            string? message = error?.Message;
            if (string.IsNullOrWhiteSpace(message))
                message = $"Cloud ERA server алдаа: {(int)response.StatusCode} {response.ReasonPhrase}";
            throw new StudioAccountException(message, response.StatusCode, error?.Code ?? "");
        }

        TResponse? value = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken).ConfigureAwait(true);
        return value ?? throw new StudioAccountException("Cloud ERA server хоосон хариу өглөө.");
    }

    private StudioAccountMetadata? ReadMetadata()
    {
        if (!File.Exists(metadataPath))
            return null;
        try
        {
            StudioAccountMetadata? value = JsonSerializer.Deserialize<StudioAccountMetadata>(File.ReadAllText(metadataPath), JsonOptions);
            if (value is null || ShouldIgnoreSavedServer(value.ServerUrl))
                return null;
            return value;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ShouldIgnoreSavedServer(string serverUrl)
    {
        if (StudioReleaseInfo.IsDevelopmentBuild ||
            !Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? savedUri) ||
            !savedUri.IsLoopback)
        {
            return false;
        }

        string? configuredServer = NormalizeOptionalServerUrl(
            Environment.GetEnvironmentVariable("ERKS_STUDIO_SERVER_URL"));
        return string.IsNullOrWhiteSpace(configuredServer) ||
            !configuredServer.Equals(
                savedUri.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase);
    }

    private void WriteMetadata(StudioAccountMetadata value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        string temporaryPath = metadataPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, metadataPath, true);
    }

    private static string CredentialTarget(StudioAccountMetadata value)
    {
        byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes(value.ServerUrl + "|" + value.Email));
        return "Erk-S Studio/Cloud ERA/" + Convert.ToHexString(identity.AsSpan(0, 12));
    }

    private static Uri BuildUri(string serverUrl, string path) => new(new Uri(serverUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static string NormalizeServerUrl(string value)
    {
        string text = (value ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new StudioAccountException("Server URL нь http эсвэл https хаяг байх ёстой.");
        }
        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string? NormalizeOptionalServerUrl(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? null : NormalizeServerUrl(value);
        }
        catch (StudioAccountException)
        {
            return null;
        }
    }

    private static string NormalizeEmail(string value) => (value ?? "").Trim().ToLowerInvariant();

    private static string? FindLinkedProjectServerUrl()
    {
        try
        {
            if (!Directory.Exists(ProjectWorkspacePaths.DefaultRoot))
                return null;
            foreach (string path in Directory.EnumerateFiles(
                         ProjectWorkspacePaths.DefaultRoot,
                         "*" + ProjectWorkspace.FileExtension,
                         SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                string? value = NormalizeOptionalServerUrl(ProjectWorkspaceStore.Load(path).Cloud.ServerUrl);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }
        return null;
    }

    private static string AppVersion => typeof(StudioAccountService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(StudioAccountService).Assembly.GetName().Version?.ToString()
        ?? "dev";

    private sealed class StudioAccountMetadata
    {
        public string ServerUrl { get; set; } = "";
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ProfileImageUrl { get; set; } = "";
        public string LicenseType { get; set; } = "";
        public DateTimeOffset LicenseExpiresAtUtc { get; set; }
    }

    private sealed class StudioActivationCredential
    {
        public string LicenseId { get; set; } = "";
        public string ActivationId { get; set; } = "";
        public string DeviceFingerprint { get; set; } = "";
    }
}

internal static class StudioDeviceIdentity
{
    public static string Fingerprint { get; } = BuildFingerprint();

    private static string BuildFingerprint()
    {
        string machineGuid = "";
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            machineGuid = key?.GetValue("MachineGuid")?.ToString() ?? "";
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
        }

        string sid = "";
        try
        {
            sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        }
        catch (SecurityException)
        {
        }
        string material = string.Join("|", "Erk-S Studio device v1", Environment.MachineName, Environment.UserName, machineGuid, sid);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}

internal static class WindowsCredentialVault
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Write<T>(string target, string userName, T value)
    {
        string secret = JsonSerializer.Serialize(value, JsonOptions);
        IntPtr blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(secret),
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = userName,
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager-д Studio activation хадгалж чадсангүй.");
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public static T? Read<T>(string target) where T : class
    {
        if (!CredRead(target, GenericCredential, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
                return null;
            throw new Win32Exception(error, "Windows Credential Manager-аас Studio activation уншиж чадсангүй.");
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;
            string json = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? "";
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static void Delete(string target)
    {
        if (!CredDelete(target, GenericCredential, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager-аас Studio activation устгаж чадсангүй.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);
}
