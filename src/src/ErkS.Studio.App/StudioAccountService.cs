using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
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
using ErkS.CloudEra.Client;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioAccountSession(
    string ServerUrl,
    string Email,
    string DisplayName,
    string FamilyName,
    string GivenName,
    string ProfileImageUrl,
    string LicenseType,
    DateTimeOffset LicenseExpiresAtUtc,
    DateTimeOffset TokenExpiresAtUtc,
    string AccessToken);

internal sealed class StudioAccountException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public string ErrorCode { get; } = "";
    public string TraceId { get; } = "";
    public string CurrentSourceId { get; } = "";
    public string CurrentRevisionId { get; } = "";
    public string CurrentOrganizationConcurrencyToken { get; } = "";
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public StudioAccountException(string message) : base(message)
    {
    }

    public StudioAccountException(
        string message,
        System.Net.HttpStatusCode statusCode,
        string errorCode,
        string traceId = "",
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        string currentSourceId = "",
        string currentRevisionId = "",
        string currentOrganizationConcurrencyToken = "") : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        TraceId = traceId?.Trim() ?? "";
        CurrentSourceId = currentSourceId?.Trim() ?? "";
        CurrentRevisionId = currentRevisionId?.Trim() ?? "";
        CurrentOrganizationConcurrencyToken =
            currentOrganizationConcurrencyToken?.Trim() ?? "";
        var details = fieldErrors is null
            ? new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string[]>(
                fieldErrors,
                StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(CurrentSourceId))
            details["currentSourceId"] = [CurrentSourceId];
        if (!string.IsNullOrWhiteSpace(CurrentRevisionId))
            details["currentRevisionId"] = [CurrentRevisionId];
        FieldErrors = details;
    }
}

internal sealed class StudioAccountService :
    IStudioSessionClient,
    ILicenseClient,
    IProjectsClient,
    IOrganizationsClient,
    ICollaborationClient,
    ISourcePackagesClient,
    IControlledDocumentsClient,
    IAlbumsClient,
    IProfileImageClient,
    IProjectChatClient,
    IDisposable
{
    public const string ProductCode = "ErkS.Studio";
    private const string PublicServerUrl = "https://erk-s.mn";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;
    private readonly ICredentialStore credentialStore;
    private readonly ICloudEraContractClient cloudEraClient;
    private readonly SemaphoreSlim sessionRefreshGate = new(1, 1);
    private readonly StudioSessionEpochGate sessionTransitions = new();
    private readonly Dictionary<string, StudioRegisteredPersonName> registeredPersonNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string metadataPath = Path.Combine(ResolveAccountDataRoot(), "account.json");
    private StudioAccountMetadata? metadata;
    private StudioActivationCredential? credential;

    public StudioAccountSession? Current { get; private set; }
    public CloudEraCapabilitiesSnapshot? CurrentCapabilities { get; private set; }

    /// <summary>How much the server last said about this account's entitlements.</summary>
    public StudioCompanionServerAnswer CompanionAnswer { get; private set; } =
        StudioCompanionServerAnswer.NotContacted;

    /// <summary>The entitlement the server stated this session, when it stated one.</summary>
    public StudioCompanionEntitlement? StatedCompanionEntitlement { get; private set; }

    /// <summary>The last entitlement confirmed on this device, read from the credential store.</summary>
    public StudioCompanionEntitlement? CachedCompanionEntitlement { get; private set; }

    public bool OrganizationRegistryImportConfigured { get; private set; }
    public string OrganizationRegistryImportMessage { get; private set; } = "ДАН холболтын төлөв тодорхойгүй байна.";

    /// <summary>Where per-account and per-device state lives on this machine.</summary>
    public static string AccountDataRoot => ResolveAccountDataRoot();

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

    public long SessionEpoch => sessionTransitions.Epoch;

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

    private StudioSessionTransition BeginSessionTransition() =>
        sessionTransitions.Begin();

    private StudioSessionTransition CaptureSessionTransition() =>
        sessionTransitions.Capture();

    private bool IsSessionEpochCurrent(long expectedEpoch) =>
        sessionTransitions.IsCurrent(expectedEpoch);

    private void RequireSessionEpoch(long expectedEpoch)
        => sessionTransitions.Require(expectedEpoch);

    public StudioAccountService()
        : this(new WindowsCredentialStore())
    {
    }

    internal StudioAccountService(ICredentialStore credentialStore)
        : this(credentialStore, cloudEraClient: null)
    {
    }

    internal StudioAccountService(
        ICredentialStore credentialStore,
        ICloudEraContractClient? cloudEraClient)
    {
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        this.cloudEraClient = cloudEraClient ?? new CloudEraGeneratedContractClient(httpClient);
    }

    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        StudioSessionTransition transition = BeginSessionTransition();
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                transition.CancellationToken);
        StudioAccountMetadata? savedMetadata = ReadMetadata();
        if (savedMetadata is null)
            return false;
        StudioActivationCredential? savedCredential =
            credentialStore.Read<StudioActivationCredential>(
                CredentialTarget(savedMetadata));
        if (savedCredential is null)
            return false;
        // Load the stored grant before reaching for the network, so a device
        // that cannot reach the server still knows what it last confirmed.
        LoadCachedCompanionEntitlement(savedCredential);

        try
        {
            await sessionRefreshGate.WaitAsync(linked.Token).ConfigureAwait(true);
            try
            {
                await RefreshInternalAsync(
                    savedMetadata,
                    savedCredential,
                    transition.Epoch,
                    linked.Token).ConfigureAwait(true);
            }
            finally
            {
                sessionRefreshGate.Release();
            }
            LastError = "";
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or StudioAccountException or Win32Exception)
        {
            if (IsSessionEpochCurrent(transition.Epoch))
            {
                Current = null;
                CurrentCapabilities = null;
                LastError = exception.Message;
                StateChanged?.Invoke();
            }
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

        StudioSessionTransition transition = BeginSessionTransition();
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                transition.CancellationToken);
        // The canonical fingerprint is the device identity going forward. The
        // legacy alias lets the server find this Studio activation without
        // consuming another device slot during the salt migration.
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.Fingerprints;
        var activateRequest = new StudioLicenseActivateRequest
        {
            ProductCode = ProductCode,
            Email = normalizedEmail,
            Password = password,
            DeviceFingerprint = fingerprints.Canonical,
            LegacyDeviceFingerprint = fingerprints.Legacy,
            DeviceName = Environment.MachineName,
            AppVersion = AppVersion,
        };
        StudioLicenseResponse license = await PostAsync<StudioLicenseActivateRequest, StudioLicenseResponse>(
            normalizedServer,
            "/api/license/activate",
            activateRequest,
            linked.Token).ConfigureAwait(true);
        RequireSessionEpoch(transition.Epoch);
        if (!license.IsValid)
            throw new StudioAccountException(string.IsNullOrWhiteSpace(license.Message)
                ? "Erk-S Studio лиценз идэвхжсэнгүй."
                : license.Message);

        var sessionRequest = new StudioSessionRequest
        {
            Email = normalizedEmail,
            Password = password,
            ClientName = "Erk-S Studio",
            ProductCode = ProductCode,
            LicenseId = license.LicenseId,
            ActivationId = license.ActivationId,
            DeviceFingerprint = fingerprints.Canonical,
            LegacyDeviceFingerprint = fingerprints.Legacy,
            DeviceName = Environment.MachineName,
            AppVersion = AppVersion,
        };
        StudioSessionResponse session = await PostAsync<StudioSessionRequest, StudioSessionResponse>(
            normalizedServer,
            "/api/studio/session",
            sessionRequest,
            linked.Token).ConfigureAwait(true);
        RequireSessionEpoch(transition.Epoch);
        CloudEraCapabilitiesSnapshot capabilities =
            await LoadCapabilitiesAsync(
                normalizedServer,
                linked.Token).ConfigureAwait(true);
        RequireSessionEpoch(transition.Epoch);

        var signedInMetadata = new StudioAccountMetadata
        {
            ServerUrl = normalizedServer,
            Email = normalizedEmail,
            DisplayName = session.DisplayName,
            FamilyName = session.FamilyName,
            GivenName = session.GivenName,
            ProfileImageUrl = session.ProfileImageUrl,
            LicenseType = session.LicenseType,
            LicenseExpiresAtUtc = session.LicenseExpiresAtUtc,
        };
        var signedInCredential = new StudioActivationCredential
        {
            LicenseId = session.LicenseId,
            ActivationId = session.ActivationId,
            DeviceFingerprint = fingerprints.Canonical,
        };
        CommitSession(
            transition.Epoch,
            signedInMetadata,
            signedInCredential,
            session,
            capabilities,
            persistCredential: true,
            // Activation answers first and may be the only response carrying
            // entitlements on a server that has not extended the session route.
            entitlements: license.Entitlements);
    }

    public async Task<IReadOnlyList<StudioCloudProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudProjectListResponse response = await cloudEraClient.ListProjectsAsync(
            CurrentCloudEraContext(),
            cancellationToken).ConfigureAwait(true);
        return response.Projects;
    }

    public async Task<StudioCloudProjectDetail> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await cloudEraClient.GetProjectAsync(
            CurrentCloudEraContext(),
            projectId,
            cancellationToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectRefreshResult> GetProjectChangesAsync(
        string projectId,
        string knownConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        string token = (knownConcurrencyToken ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(token))
        {
            return new StudioCloudProjectRefreshResult(
                true,
                await GetProjectAsync(projectId, cancellationToken).ConfigureAwait(true));
        }

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireCapability(CloudEraFeatures.AlbumRevisions);
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        string path = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId);
        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue('"' + token + '"'));
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new StudioCloudProjectRefreshResult(false, null);

        StudioCloudProjectDetail project = await ReadResponseAsync<StudioCloudProjectDetail>(
            response,
            cancellationToken).ConfigureAwait(true);
        project = await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
        return new StudioCloudProjectRefreshResult(true, project);
    }

    public async Task<StudioCloudProjectDetail> UpdateProjectInformationAsync(
        string projectId,
        StudioCloudProjectInformationUpdateRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        if (string.IsNullOrWhiteSpace(concurrencyToken))
            throw new StudioAccountException("Cloud project concurrency token хоосон байна. Төслийг Refresh хийнэ үү.");
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await PutAuthorizedAsync<StudioCloudProjectInformationUpdateRequest, StudioCloudProjectDetail>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/information",
            request,
            cancellationToken,
            ifMatchToken: concurrencyToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> UpdateBuildingCompositionAsync(
        string projectId,
        StudioCloudBuildingCompositionUpdateRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        if (string.IsNullOrWhiteSpace(concurrencyToken))
        {
            throw new StudioAccountException(
                "Cloud project concurrency token хоосон байна. Төслийг Cloud-оос шинэчилнэ үү.");
        }

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project =
            await PutAuthorizedAsync<StudioCloudBuildingCompositionUpdateRequest, StudioCloudProjectDetail>(
                "/api/cloud-era/v1/projects/" +
                Uri.EscapeDataString(projectId) +
                "/building-composition",
                request,
                cancellationToken,
                ifMatchToken: concurrencyToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> UploadProjectClientLogoAsync(
        string projectId,
        string logoPath,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        if (string.IsNullOrWhiteSpace(concurrencyToken))
            throw new StudioAccountException("Cloud project concurrency token Ñ…Ð¾Ð¾ÑÐ¾Ð½ Ð±Ð°Ð¹Ð½Ð°. Ð¢Ó©ÑÐ»Ð¸Ð¹Ð³ Refresh Ñ…Ð¸Ð¹Ð½Ñ Ò¯Ò¯.");
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        if (!File.Exists(logoPath))
            throw new StudioAccountException("Ð¡Ð¾Ð½Ð³Ð¾ÑÐ¾Ð½ Ð·Ð°Ñ…Ð¸Ð°Ð»Ð°Ð³Ñ‡Ð¸Ð¹Ð½ Ð»Ð¾Ð³Ð¾ Ñ„Ð°Ð¹Ð» Ð¾Ð»Ð´ÑÐ¾Ð½Ð³Ò¯Ð¹.");
        FileInfo info = new(logoPath);
        if (info.Length > 5L * 1024L * 1024L)
            throw new StudioAccountException("Ð—Ð°Ñ…Ð¸Ð°Ð»Ð°Ð³Ñ‡Ð¸Ð¹Ð½ Ð»Ð¾Ð³Ð¾ 5 MB-Ð°Ð°Ñ Ð¸Ñ…Ð³Ò¯Ð¹ Ð±Ð°Ð¹Ð½Ð°.");
        string contentType = Path.GetExtension(logoPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new StudioAccountException("Ð—Ð°Ñ…Ð¸Ð°Ð»Ð°Ð³Ñ‡Ð¸Ð¹Ð½ Ð»Ð¾Ð³Ð¾ PNG ÑÑÐ²ÑÐ» JPEG Ð·ÑƒÑ€Ð°Ð³ Ð±Ð°Ð¹Ð½Ð°."),
        };

        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio Ð±Ò¯Ñ€Ñ‚Ð³ÑÐ»ÑÑÑ€ Ð½ÑÐ²Ñ‚ÑÑ€Ð½Ñ Ò¯Ò¯.");
        using FileStream source = File.OpenRead(logoPath);
        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(source);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", Path.GetFileName(logoPath));
        using HttpRequestMessage httpRequest = new(
            HttpMethod.Post,
            BuildUri(session.ServerUrl, "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/foundation/client-logo"))
        {
            Content = form,
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        httpRequest.Headers.IfMatch.Add(new EntityTagHeaderValue('"' + concurrencyToken.Trim().Trim('"') + '"'));
        using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await ReadResponseAsync<StudioCloudProjectDetail>(response, cancellationToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> DeleteProjectClientLogoAsync(
        string projectId,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        if (string.IsNullOrWhiteSpace(concurrencyToken))
            throw new StudioAccountException("Cloud project concurrency token Ñ…Ð¾Ð¾ÑÐ¾Ð½ Ð±Ð°Ð¹Ð½Ð°. Ð¢Ó©ÑÐ»Ð¸Ð¹Ð³ Refresh Ñ…Ð¸Ð¹Ð½Ñ Ò¯Ò¯.");
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio Ð±Ò¯Ñ€Ñ‚Ð³ÑÐ»ÑÑÑ€ Ð½ÑÐ²Ñ‚ÑÑ€Ð½Ñ Ò¯Ò¯.");
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            BuildUri(session.ServerUrl, "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/foundation/client-logo"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.IfMatch.Add(new EntityTagHeaderValue('"' + concurrencyToken.Trim().Trim('"') + '"'));
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await ReadResponseAsync<StudioCloudProjectDetail>(response, cancellationToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task DeleteProjectAsync(
        string projectId,
        string confirmProjectCode,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            BuildUri(
                session.ServerUrl,
                "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId)))
        {
            Content = JsonContent.Create(
                new StudioCloudProjectDeleteRequest
                {
                    ConfirmProjectCode = confirmProjectCode?.Trim() ?? "",
                    Reason = reason?.Trim() ?? "",
                },
                options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        AddRelationshipBoundaryAcknowledgement(request, acknowledged: true);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        if (response.IsSuccessStatusCode)
            return;
        await ReadResponseAsync<object>(response, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudAccountLookupResponse> LookupAccountAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await LookupAccountCoreAsync(email, cancellationToken).ConfigureAwait(true);
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
        StudioCloudProjectDetail project = await PostAuthorizedAsync<StudioCloudProjectDetail>(
            "/api/cloud-era/v1/project-membership-invitations/" + Uri.EscapeDataString(invitationId) + "/accept",
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
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

    public async Task<StudioCloudProjectDetail> UpdateParticipantRolesAsync(
        string projectId,
        string participantId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        await EnsureCapabilityAsync(
            CloudEraFeatures.ParticipantRoleManagement,
            cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await cloudEraClient.UpdateParticipantRolesAsync(
            CurrentCloudEraContext(),
            projectId,
            participantId,
            new StudioParticipantRoleUpdateRequest
            {
                Roles = roles
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            },
            cancellationToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> AssignConceptArchitectAsync(
        string projectId,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCapabilityAsync(
            CloudEraFeatures.ConceptArchitectAssignment,
            cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await cloudEraClient.AssignConceptArchitectAsync(
            CurrentCloudEraContext(),
            projectId,
            new StudioConceptArchitectAssignmentRequest
            {
                ParticipantId = participantId?.Trim() ?? "",
            },
            cancellationToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
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
        string sourceId,
        string participantId,
        string projectConcurrencyToken,
        string expectedSourceId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireExactConcurrencyToken(
            projectConcurrencyToken,
            "Source custody transfer requires the current project concurrency token.");
        RequireExactConcurrencyToken(
            expectedSourceId,
            "Source custody transfer requires the exact current source id.");
        return await PutAuthorizedAsync<StudioCloudSourceCustodianAssignRequest, StudioCloudSourcePackage>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/sources/" + Uri.EscapeDataString(sourceId) + "/custodian",
            new StudioCloudSourceCustodianAssignRequest
            {
                ParticipantId = participantId,
                ProjectConcurrencyToken = projectConcurrencyToken.Trim(),
                ExpectedSourceId = expectedSourceId.Trim(),
            },
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
        StudioCloudProjectDetail project = await PostAuthorizedAsync<StudioCloudProjectCreateRequest, StudioCloudProjectDetail>(
            "/api/cloud-era/v1/project-creation-grants/" + Uri.EscapeDataString(grantId) + "/projects",
            request,
            cancellationToken,
            relationshipBoundaryAcknowledged: true).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<StudioCloudOrganization>> ListOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        long expectedSessionEpoch = SessionEpoch;
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireSessionEpoch(expectedSessionEpoch);
        StudioCloudOrganizationListResponse response = await GetAuthorizedAsync<StudioCloudOrganizationListResponse>(
            "/api/cloud-era/v1/organizations",
            cancellationToken).ConfigureAwait(true);
        RequireSessionEpoch(expectedSessionEpoch);
        OrganizationRegistryImportConfigured = response.OrganizationRegistryImportConfigured;
        OrganizationRegistryImportMessage = string.IsNullOrWhiteSpace(response.OrganizationRegistryImportMessage)
            ? response.OrganizationRegistryImportConfigured
                ? "ДАН байгууллагын мэдээлэл татах холбоос бэлэн байна."
                : "ДАН холболт Server дээр тохируулагдаагүй байна."
            : response.OrganizationRegistryImportMessage.Trim();
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
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        if (string.IsNullOrWhiteSpace(request?.BaseConcurrencyToken))
        {
            throw new StudioAccountException(
                "Organization update requires its original canonical concurrency token.");
        }
        return await PutAuthorizedAsync<StudioCloudOrganizationUpsertRequest, StudioCloudOrganization>(
            "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId),
            request,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioOrganizationRegistryImportResponse> BeginOrganizationRegistryImportAsync(
        string organizationId,
        string registrationNumber,
        string baseConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        // Without the gate, a server with no ДАН connection answers this with
        // a raw 503 instead of a controlled feature-unavailable message.
        await EnsureCapabilityAsync(
            CloudEraFeatures.DanOrganizationRegistryImportV1,
            cancellationToken).ConfigureAwait(true);
        RequireExactConcurrencyToken(
            baseConcurrencyToken,
            "Organization registry import requires the original canonical concurrency token.");
        return await PostAuthorizedAsync<StudioOrganizationRegistryImportRequest, StudioOrganizationRegistryImportResponse>(
            "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId) + "/registry-imports",
            new StudioOrganizationRegistryImportRequest
            {
                RegistrationNumber = registrationNumber,
                BaseConcurrencyToken = baseConcurrencyToken.Trim(),
            },
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioOrganizationRegistryImportResponse> GetOrganizationRegistryImportAsync(
        string organizationId,
        string importId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        await EnsureCapabilityAsync(
            CloudEraFeatures.DanOrganizationRegistryImportV1,
            cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioOrganizationRegistryImportResponse>(
            "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId) +
            "/registry-imports/" + Uri.EscapeDataString(importId),
            cancellationToken).ConfigureAwait(true);
    }

    public async Task DeleteOrganizationAsync(
        string organizationId,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireExactConcurrencyToken(
            concurrencyToken,
            "Organization deletion requires the current canonical concurrency token.");
        await SendAuthorizedNoContentAsync(
            HttpMethod.Delete,
            "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId),
            cancellationToken,
            ifMatchToken: concurrencyToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudOrganization> UploadOrganizationLogoAsync(
        string organizationId,
        string logoPath,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireExactConcurrencyToken(
            concurrencyToken,
            "Organization logo update requires the current canonical concurrency token.");
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
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(
            '"' + concurrencyToken.Trim().Trim('"') + '"'));
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudOrganization>(response, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudOrganization> DeleteOrganizationLogoAsync(
        string organizationId,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireExactConcurrencyToken(
            concurrencyToken,
            "Organization logo deletion requires the current canonical concurrency token.");
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            BuildUri(session.ServerUrl, "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId) + "/logo"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(
            '"' + concurrencyToken.Trim().Trim('"') + '"'));
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
        if (!TryBuildSameOriginUri(session.ServerUrl, logoUrl, out Uri logoUri))
            return null;
        using HttpRequestMessage request = new(HttpMethod.Get, logoUri);
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

    /// <summary>
    /// Fetches one of the organisation's scans - a registration certificate or
    /// a design licence - by the content URL the server gave for it.
    ///
    /// The path goes through the project, so being on the project is enough;
    /// somebody printing the album need not also be a member of the
    /// organisation that owns the papers.
    ///
    /// The content type is read from the response rather than guessed from the
    /// file name, and anything that is not a document the album can draw is
    /// refused rather than saved for the renderer to fail on later.
    /// </summary>
    public async Task<StudioDownloadedDocument?> GetOrganizationDocumentAsync(
        string contentUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentUrl))
            return null;
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        if (!TryBuildSameOriginUri(session.ServerUrl, contentUrl, out Uri documentUri))
            return null;

        using HttpRequestMessage request = new(HttpMethod.Get, documentUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return await ReadResponseAsync<StudioDownloadedDocument?>(response, cancellationToken).ConfigureAwait(true);

        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!StudioOrganizationDocumentFormats.CanDraw(contentType))
        {
            throw new StudioAccountException(
                $"Байгууллагын баримт '{contentType}' төрөлтэй байна. PDF, PNG эсвэл JPEG байх ёстой.");
        }

        const long limit = 20L * 1024L * 1024L;
        if (response.Content.Headers.ContentLength > limit)
            throw new StudioAccountException("Байгууллагын баримт 20 MB-аас их байна.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
        if (bytes.Length > limit)
            throw new StudioAccountException("Байгууллагын баримт 20 MB-аас их байна.");
        return new StudioDownloadedDocument(bytes, contentType);
    }

    /// <summary>
    /// Sends one of this device's scans up to the organisation that owns it.
    ///
    /// The title goes as its own part rather than in the query string: it is
    /// Mongolian, and a certificate that arrives called «Ð“ÑÑ€Ñ‡Ð¸Ð»Ð³ÑÑ» is a
    /// certificate somebody has to fix by hand.
    /// </summary>
    public async Task<StudioCloudOrganizationDocument> UploadOrganizationDocumentAsync(
        string organizationId,
        string category,
        string title,
        string fileName,
        string contentType,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new StudioAccountException("Байгууллагын баримт хоосон байна.");
        if (!StudioOrganizationDocumentFormats.CanDraw(contentType))
        {
            throw new StudioAccountException(
                $"'{contentType}' төрлийг байгууллагын баримт болгож илгээхгүй. PDF, PNG эсвэл JPEG байх ёстой.");
        }

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(category, Encoding.UTF8), "category");
        form.Add(new StringContent(title ?? "", Encoding.UTF8), "title");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", string.IsNullOrWhiteSpace(fileName) ? "document" : fileName);

        string path = "/api/cloud-era/v1/organizations/" +
            Uri.EscapeDataString(organizationId) + "/documents";
        using HttpRequestMessage request = new(HttpMethod.Post, BuildUri(session.ServerUrl, path))
        {
            Content = form,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudOrganizationDocument>(response, cancellationToken)
            .ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер байгууллагын баримтын хариу буцаасангүй.");
    }

    public async Task<IReadOnlyList<StudioCloudControlledDocument>> ListControlledDocumentsAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireCapability(CloudEraFeatures.AlbumRevisions);
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio account is not signed in.");
        string path = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/documents";
        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<List<StudioCloudControlledDocument>>(
            response,
            cancellationToken).ConfigureAwait(true) ?? [];
    }

    public async Task<StudioCloudControlledDocument> ReplaceControlledDocumentFilesAsync(
        string projectId,
        string documentId,
        int expectedDocumentVersion,
        string projectConcurrencyToken,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        if (expectedDocumentVersion < 1)
            throw new StudioAccountException("Cloud controlled document version is missing.");
        if (string.IsNullOrWhiteSpace(projectConcurrencyToken))
            throw new StudioAccountException("Canonical project version is missing. Refresh and try again.");

        List<string> files = (filePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Any(path => !File.Exists(path)))
            throw new StudioAccountException("One or more controlled document files are unavailable.");

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio account is not signed in.");
        using var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(expectedDocumentVersion.ToString(CultureInfo.InvariantCulture)),
            "expectedDocumentVersion");
        content.Add(new StringContent(projectConcurrencyToken.Trim()), "projectConcurrencyToken");
        content.Add(new StringContent((files.Count == 0).ToString().ToLowerInvariant()), "clearCurrent");
        foreach (string path in files)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue(DocumentContentType(path));
            content.Add(file, "files", Path.GetFileName(path));
        }

        string endpoint = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/documents/" + Uri.EscapeDataString(documentId) + "/current-files";
        using HttpRequestMessage request = new(HttpMethod.Put, BuildUri(session.ServerUrl, endpoint))
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudControlledDocument>(response, cancellationToken).ConfigureAwait(true);
    }

    public async Task DownloadControlledFileAsync(
        StudioCloudFile file,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (string.IsNullOrWhiteSpace(file.FileId))
            throw new StudioAccountException("Cloud controlled file ID is missing.");
        if (file.SizeBytes > 25L * 1024L * 1024L)
            throw new StudioAccountException("Cloud controlled document file is larger than 25 MB.");

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio account is not signed in.");
        string path = "/api/cloud-era/v1/files/" + Uri.EscapeDataString(file.FileId);
        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        if (!response.IsSuccessStatusCode)
        {
            await ReadResponseAsync<object>(response, cancellationToken).ConfigureAwait(true);
            return;
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
        await ReplaceDownloadedFileAsync(
            source,
            destinationPath,
            cancellationToken,
            file.Sha256).ConfigureAwait(true);
    }

    private static string DocumentContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    public async Task<IReadOnlyList<StudioCloudAlbum>> ListAlbumsAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await cloudEraClient.ListAlbumsAsync(
            CurrentCloudEraContext(),
            projectId,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<StudioCloudDesignPackage>> ListDesignPackagesAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await cloudEraClient.ListDesignPackagesAsync(
            CurrentCloudEraContext(),
            projectId,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudAlbum> EnsureConceptAlbumAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireCapability(CloudEraFeatures.AlbumRevisions);
        return await cloudEraClient.EnsureConceptAlbumAsync(
            CurrentCloudEraContext(),
            projectId,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudSourcePackage> RegisterSourcePackageAsync(
        string projectId,
        StudioCloudSourcePackageCreateRequest value,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        RequireCapability(CloudEraFeatures.SourcePackagesV4);
        RequireCapability(CloudEraFeatures.SourcePackageCasV1);
        return await cloudEraClient.RegisterSourcePackageAsync(
            CurrentCloudEraContext(),
            projectId,
            value,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudSourcePackage> RetireSourcePackageAsync(
        string projectId,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        RequireCapability(CloudEraFeatures.SourcePackagesV4);
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ??
            throw new StudioAccountException("Studio account is not signed in.");
        string path = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/source-packages/" + Uri.EscapeDataString(sourceId);
        using HttpRequestMessage request = new(HttpMethod.Delete, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            '"' + sourceId.Trim() + '"');
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudSourcePackage>(
            response,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudAlbumRevision> UploadAlbumRevisionAsync(
        string projectId,
        string albumId,
        string pdfPath,
        int pageCount,
        string pageSizeSummary,
        string projectConcurrencyToken,
        string? expectedBaseRevisionId = null,
        bool inheritComponentManifest = false,
        IReadOnlyList<StudioCloudAlbumSection>? componentManifest = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pdfPath))
            throw new StudioAccountException("Синк хийх альбумын PDF олдсонгүй.");
        if (pageCount < 1)
            throw new StudioAccountException("Альбумын хуудасны тоо буруу байна.");

        if (string.IsNullOrWhiteSpace(projectConcurrencyToken))
            throw new StudioAccountException("Canonical project version is missing. Start Sync again.");

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        FileInfo pdf = new(pdfPath);
        bool supportsChunkedUpload = CurrentCapabilities is { } capabilities &&
            CloudEraCapabilityPolicy.Supports(capabilities, CloudEraFeatures.ChunkedAlbumUploadsV1);
        if (supportsChunkedUpload)
        {
            return await CloudEraChunkedAlbumUploader.UploadAsync(
                httpClient,
                session.ServerUrl,
                session.AccessToken,
                projectId,
                albumId,
                pdfPath,
                pageCount,
                pageSizeSummary,
                projectConcurrencyToken,
                expectedBaseRevisionId,
                inheritComponentManifest,
                componentManifest,
                cancellationToken).ConfigureAwait(true);
        }
        if (pdf.Length > 20L * 1024L * 1024L)
        {
            throw new StudioAccountException(
                "Альбумын PDF 20 MB-аас том байна. Cloud ERA server-ийн chunk upload шинэчлэлт шаардлагатай; PDF-ийн вектор чанарыг бууруулахгүйгээр server-ээ шинэчилнэ үү.");
        }

        using var content = new MultipartFormDataContent();
        AddAlbumRevisionUploadFields(
            content,
            pageCount,
            pageSizeSummary,
            projectConcurrencyToken,
            expectedBaseRevisionId,
            inheritComponentManifest,
            componentManifest);
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

    internal static void AddAlbumRevisionUploadFields(
        MultipartFormDataContent content,
        int pageCount,
        string pageSizeSummary,
        string projectConcurrencyToken,
        string? expectedBaseRevisionId,
        bool inheritComponentManifest,
        IReadOnlyList<StudioCloudAlbumSection>? componentManifest)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.Add(new StringContent(pageCount.ToString(CultureInfo.InvariantCulture)), "pageCount");
        content.Add(new StringContent(pageSizeSummary ?? ""), "pageSizeSummary");
        content.Add(new StringContent(projectConcurrencyToken.Trim()), "projectConcurrencyToken");
        if (!string.IsNullOrWhiteSpace(expectedBaseRevisionId))
        {
            content.Add(
                new StringContent(expectedBaseRevisionId.Trim()),
                "expectedBaseRevisionId");
        }
        if (inheritComponentManifest)
            content.Add(new StringContent("true"), "inheritComponentManifest");
        if (componentManifest is not null)
        {
            content.Add(
                new StringContent(JsonSerializer.Serialize(componentManifest, JsonOptions)),
                "componentManifest");
        }
    }

    public async Task<StudioCloudAlbumRevision> SetAlbumComponentManifestAsync(
        string projectId,
        string albumId,
        string revisionId,
        string projectConcurrencyToken,
        string expectedBaseRevisionId,
        IReadOnlyList<StudioCloudAlbumSection> components,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio account is not signed in.");
        string path = "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
            "/albums/" + Uri.EscapeDataString(albumId) +
            "/revisions/" + Uri.EscapeDataString(revisionId) +
            "/component-manifest";
        StudioCloudAlbumComponentManifestUpdateRequest payload =
            CreateAlbumComponentManifestUpdateRequest(
                projectConcurrencyToken,
                expectedBaseRevisionId,
                components);
        using HttpRequestMessage request = new(HttpMethod.Put, BuildUri(session.ServerUrl, path))
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudAlbumRevision>(response, cancellationToken).ConfigureAwait(true);
    }

    internal static StudioCloudAlbumComponentManifestUpdateRequest
        CreateAlbumComponentManifestUpdateRequest(
            string projectConcurrencyToken,
            string expectedBaseRevisionId,
            IReadOnlyList<StudioCloudAlbumSection> components)
    {
        string token = (projectConcurrencyToken ?? "").Trim();
        string revisionId = (expectedBaseRevisionId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new StudioAccountException(
                "Album component manifest update requires the canonical " +
                "project concurrency token.");
        }
        if (string.IsNullOrWhiteSpace(revisionId))
        {
            throw new StudioAccountException(
                "Album component manifest update requires the exact base revision.");
        }

        return new StudioCloudAlbumComponentManifestUpdateRequest
        {
            ProjectConcurrencyToken = token,
            ExpectedBaseRevisionId = revisionId,
            Components = (components ?? []).ToList(),
        };
    }

    public async Task<StudioCloudAlbumRevision> MergeAlbumComponentsAsync(
        string projectId,
        string albumId,
        string expectedRevisionId,
        string projectConcurrencyToken,
        IReadOnlyList<StudioAlbumComponentUpload> components,
        CancellationToken cancellationToken = default)
    {
        RequireCapability(CloudEraFeatures.AlbumComponentMergeV1);
        RequireCapability(CloudEraFeatures.ContributorOwnedComponentsV1);
        RequireCapability(CloudEraFeatures.OptimisticConcurrency);
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio account is not signed in.");
        return await CloudEraAlbumComponentUploader.MergeAsync(
            httpClient,
            session.ServerUrl,
            session.AccessToken,
            projectId,
            albumId,
            expectedRevisionId,
            projectConcurrencyToken,
            components,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task DownloadAlbumRevisionPdfAsync(
        StudioCloudAlbumRevision revision,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (string.IsNullOrWhiteSpace(revision.PdfFileId))
            throw new StudioAccountException("Cloud ERA album revision PDF file ID is missing.");

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio Ð±Ò¯Ñ€Ñ‚Ð³ÑÐ»ÑÑÑ€ Ð½ÑÐ²Ñ‚ÑÑ€Ð½Ñ Ò¯Ò¯.");
        string path = "/api/cloud-era/v1/files/" + Uri.EscapeDataString(revision.PdfFileId);
        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        if (!response.IsSuccessStatusCode)
        {
            await ReadResponseAsync<object>(response, cancellationToken).ConfigureAwait(true);
            return;
        }

        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            throw new StudioAccountException("Cloud ERA album revision PDF биш content type буцаалаа.");
        if (response.Content.Headers.ContentLength > 250L * 1024L * 1024L)
            throw new StudioAccountException("Cloud ERA album revision PDF 250 MB-аас том байна.");

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
        await ReplaceDownloadedFileAsync(
            source,
            destinationPath,
            cancellationToken,
            revision.PdfSha256).ConfigureAwait(true);
    }

    internal static async Task ReplaceDownloadedFileAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken = default,
        string expectedSha256 = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string? destinationFolder = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationFolder))
            Directory.CreateDirectory(destinationFolder);

        string temporaryPath = destinationPath + ".download";
        try
        {
            await using (FileStream destination = new(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string expected = (expectedSha256 ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(expected))
            {
                await using FileStream downloaded = File.OpenRead(temporaryPath);
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(
                        downloaded,
                        cancellationToken).ConfigureAwait(false))
                    .ToLowerInvariant();
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Downloaded Cloud ERA album PDF hash did not match the server revision.");
                }
            }

            File.Move(temporaryPath, destinationPath, true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    public async Task<byte[]?> GetProfileImageAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        string profileImagePath = string.IsNullOrWhiteSpace(session.ProfileImageUrl)
            ? "/api/studio/profile/photo"
            : session.ProfileImageUrl;
        if (!TryBuildSameOriginUri(session.ServerUrl, profileImagePath, out Uri profileImageUri))
            profileImageUri = BuildUri(session.ServerUrl, "/api/studio/profile/photo");

        using HttpRequestMessage request = new(HttpMethod.Get, profileImageUri);
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

    public async Task<StudioProjectChatResponse> GetProjectChatAsync(
        string projectId,
        int take = 100,
        string? peerEmail = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        string path = $"/api/cloud-era/v1/projects/{Uri.EscapeDataString(projectId)}/chat?take={Math.Clamp(take, 1, 200)}";
        if (!string.IsNullOrWhiteSpace(peerEmail))
            path += "&peerEmail=" + Uri.EscapeDataString(peerEmail.Trim());
        return await GetAuthorizedAsync<StudioProjectChatResponse>(path, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioProjectChatResponse> SendProjectChatMessageAsync(
        string projectId,
        string message,
        string? attachmentPath = null,
        string? peerEmail = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(message ?? "", Encoding.UTF8), "message");
        content.Add(new StringContent(peerEmail?.Trim() ?? "", Encoding.UTF8), "peerEmail");

        FileStream? attachmentStream = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(attachmentPath))
            {
                StudioProjectChatAttachmentValidation validation = StudioProjectChatRules.ValidateAttachment(attachmentPath);
                if (!validation.IsValid)
                    throw new StudioAccountException(validation.Message);
                attachmentStream = File.Open(attachmentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var fileContent = new StreamContent(attachmentStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(validation.ContentType);
                content.Add(fileContent, "attachment", Path.GetFileName(attachmentPath));
            }

            string path = $"/api/cloud-era/v1/projects/{Uri.EscapeDataString(projectId)}/chat/messages";
            using HttpRequestMessage request = new(HttpMethod.Post, BuildUri(session.ServerUrl, path))
            {
                Content = content,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(true);
            return await ReadResponseAsync<StudioProjectChatResponse>(response, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            attachmentStream?.Dispose();
        }
    }

    public async Task<StudioProjectChatResponse> ReactToProjectChatMessageAsync(
        string projectId,
        string messageId,
        string reaction,
        string? peerEmail = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        string path = $"/api/cloud-era/v1/projects/{Uri.EscapeDataString(projectId)}/chat/messages/{Uri.EscapeDataString(messageId)}/reactions";
        return await PostAuthorizedAsync<StudioProjectChatReactionRequest, StudioProjectChatResponse>(
            path,
            new StudioProjectChatReactionRequest
            {
                Reaction = reaction,
                PeerEmail = peerEmail?.Trim() ?? "",
            },
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<byte[]?> DownloadProjectChatAssetAsync(
        string assetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetPath) ||
            !assetPath.TrimStart('/').StartsWith("api/cloud-era/v1/projects/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(HttpMethod.Get, BuildUri(session.ServerUrl, assetPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(true);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return await ReadResponseAsync<byte[]?>(response, cancellationToken).ConfigureAwait(true);
        if (response.Content.Headers.ContentLength > StudioProjectChatRules.MaxAttachmentBytes)
            throw new StudioAccountException("Чатын файл 15 MB-аас их байна.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
        return bytes.Length > StudioProjectChatRules.MaxAttachmentBytes
            ? throw new StudioAccountException("Чатын файл 15 MB-аас их байна.")
            : bytes;
    }

    public async Task<StudioCloudProjectDetail> CreateProjectAsync(
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await cloudEraClient.CreateProjectAsync(
            CurrentCloudEraContext(),
            request,
            cancellationToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudProjectDetail> AssignDesignOrganizationAsync(
        string projectId,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudProjectDetail project = await cloudEraClient.AssignDesignOrganizationAsync(
            CurrentCloudEraContext(),
            projectId,
            new StudioCloudDesignOrganizationAssignmentRequest { OrganizationId = organizationId },
            cancellationToken).ConfigureAwait(true);
        return await ResolveProjectParticipantNamesAsync(project, cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioCloudStageAdvanceResponse> AdvanceProjectStageAsync(
        string projectId,
        StudioCloudStageAdvanceRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioCloudStageAdvanceResponse response = await cloudEraClient.AdvanceProjectStageAsync(
            CurrentCloudEraContext(),
            projectId,
            request,
            cancellationToken).ConfigureAwait(true);
        response.Project = await ResolveProjectParticipantNamesAsync(response.Project, cancellationToken).ConfigureAwait(true);
        return response;
    }

    public void OpenAccountRegistration()
    {
        string server = Current?.ServerUrl ?? SuggestedServerUrl;
        Process.Start(new ProcessStartInfo(server.TrimEnd('/') + "/register") { UseShellExecute = true });
    }

    public void SignOut()
    {
        StudioSessionTransition transition = BeginSessionTransition();
        sessionTransitions.Commit(transition.Epoch, () =>
        {
            StudioAccountMetadata? saved = metadata ?? ReadMetadata();
            if (saved is not null)
                credentialStore.Delete(CredentialTarget(saved));
            try
            {
                if (File.Exists(metadataPath))
                    File.Delete(metadataPath);
            }
            catch (IOException)
            {
            }
            Current = null;
            CurrentCapabilities = null;
            OrganizationRegistryImportConfigured = false;
            OrganizationRegistryImportMessage =
                "ДАН холболтын төлөв тодорхойгүй байна.";
            registeredPersonNames.Clear();
            metadata = null;
            credential = null;
            // The credential carrying the stored grant is gone with it.
            CompanionAnswer = StudioCompanionServerAnswer.NotContacted;
            StatedCompanionEntitlement = null;
            CachedCompanionEntitlement = null;
            LastError = "";
            StateChanged?.Invoke();
        });
    }

    public void Dispose()
    {
        sessionTransitions.Dispose();
        sessionRefreshGate.Dispose();
        httpClient.Dispose();
    }

    private async Task<StudioCloudAccountLookupResponse> LookupAccountCoreAsync(
        string email,
        CancellationToken cancellationToken)
    {
        string normalizedEmail = NormalizeEmail(email);
        StudioCloudAccountLookupResponse result = await GetAuthorizedAsync<StudioCloudAccountLookupResponse>(
            "/api/cloud-era/v1/accounts/lookup?email=" + Uri.EscapeDataString(normalizedEmail),
            cancellationToken).ConfigureAwait(true);
        if (!result.Found)
            return result;

        StudioRegisteredPersonName name = StudioRegisteredPersonNameResolver.Resolve(
            result.FamilyName,
            result.GivenName,
            result.DisplayName,
            displayNameUsesCanonicalProfileOrder: true);
        StudioRegisteredPersonNameResolver.Apply(result, name);
        registeredPersonNames[normalizedEmail] = name;
        return result;
    }

    private async Task<StudioCloudProjectDetail> ResolveProjectParticipantNamesAsync(
        StudioCloudProjectDetail project,
        CancellationToken cancellationToken)
    {
        foreach (StudioCloudParticipant participant in project.Participants ?? [])
        {
            if (string.IsNullOrWhiteSpace(participant.AccountEmail))
                continue;

            string email = participant.AccountEmail.Trim();
            StudioRegisteredPersonName? registeredName = null;
            if (!string.IsNullOrWhiteSpace(participant.FamilyName) ||
                !string.IsNullOrWhiteSpace(participant.GivenName))
            {
                registeredName = StudioRegisteredPersonNameResolver.Resolve(
                    participant.FamilyName,
                    participant.GivenName,
                    participant.DisplayName,
                    displayNameUsesCanonicalProfileOrder: false);
            }
            else if (Current is { } current &&
                     email.Equals(current.Email, StringComparison.OrdinalIgnoreCase))
            {
                registeredName = new StudioRegisteredPersonName(
                    current.FamilyName,
                    current.GivenName,
                    current.DisplayName);
            }
            else if (registeredPersonNames.TryGetValue(email, out StudioRegisteredPersonName? cached))
            {
                registeredName = cached;
            }
            else
            {
                try
                {
                    StudioCloudAccountLookupResponse lookup = await LookupAccountCoreAsync(
                        email,
                        cancellationToken).ConfigureAwait(true);
                    if (lookup.Found)
                    {
                        registeredName = new StudioRegisteredPersonName(
                            lookup.FamilyName,
                            lookup.GivenName,
                            lookup.DisplayName);
                    }
                }
                catch (Exception exception) when (
                    !cancellationToken.IsCancellationRequested &&
                    exception is StudioAccountException or HttpRequestException)
                {
                    // Older Cloud ERA servers may not expose account lookup. Keep the
                    // snapshot intact instead of guessing which word is the surname.
                }
            }

            if (registeredName is not null)
            {
                StudioRegisteredPersonNameResolver.Apply(participant, registeredName);
                registeredPersonNames[email] = registeredName;
            }
        }

        return project;
    }

    private async Task EnsureFreshSessionAsync(CancellationToken cancellationToken)
    {
        if (Current is not null && Current.TokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            return;
        StudioSessionTransition transition = CaptureSessionTransition();
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                transition.CancellationToken);
        await sessionRefreshGate.WaitAsync(linked.Token).ConfigureAwait(true);
        try
        {
            RequireSessionEpoch(transition.Epoch);
            if (Current is not null &&
                Current.TokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return;
            }

            metadata ??= ReadMetadata();
            if (metadata is null)
                throw new StudioAccountException("Erk-S Studio бүртгэлээр нэвтэрнэ үү.");
            credential ??= credentialStore.Read<StudioActivationCredential>(CredentialTarget(metadata));
            if (credential is null)
                throw new StudioAccountException("Studio лицензийн төхөөрөмжийн activation олдсонгүй. Дахин нэвтэрнэ үү.");
            await RefreshInternalAsync(
                metadata,
                credential,
                transition.Epoch,
                linked.Token).ConfigureAwait(true);
        }
        finally
        {
            sessionRefreshGate.Release();
        }
    }

    private CloudEraClientContext CurrentCloudEraContext()
    {
        StudioAccountSession session = Current
            ?? throw new StudioAccountException("Studio account is required.");
        return new CloudEraClientContext(session.ServerUrl, session.AccessToken);
    }

    private async Task RefreshInternalAsync(
        StudioAccountMetadata savedMetadata,
        StudioActivationCredential savedCredential,
        long expectedSessionEpoch,
        CancellationToken cancellationToken)
    {
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.Fingerprints;
        var validateRequest = new StudioLicenseValidateRequest
        {
            Email = savedMetadata.Email,
            ProductCode = ProductCode,
            LicenseId = savedCredential.LicenseId,
            ActivationId = savedCredential.ActivationId,
            DeviceFingerprint = fingerprints.Canonical,
            LegacyDeviceFingerprint = fingerprints.Legacy,
            DeviceName = Environment.MachineName,
            AppVersion = AppVersion,
        };
        StudioLicenseResponse license = await PostAsync<StudioLicenseValidateRequest, StudioLicenseResponse>(
            savedMetadata.ServerUrl,
            "/api/license/validate",
            validateRequest,
            cancellationToken).ConfigureAwait(true);
        RequireSessionEpoch(expectedSessionEpoch);
        if (!license.IsValid)
        {
            throw new StudioAccountException(string.IsNullOrWhiteSpace(license.Message)
                ? "Studio лицензийн төхөөрөмжийн activation хүчингүй байна."
                : license.Message);
        }

        var request = new StudioSessionRefreshRequest
        {
            Email = savedMetadata.Email,
            ProductCode = ProductCode,
            LicenseId = savedCredential.LicenseId,
            ActivationId = savedCredential.ActivationId,
            DeviceFingerprint = fingerprints.Canonical,
            LegacyDeviceFingerprint = fingerprints.Legacy,
            DeviceName = Environment.MachineName,
            AppVersion = AppVersion,
        };
        StudioSessionResponse session = await PostAsync<StudioSessionRefreshRequest, StudioSessionResponse>(
            savedMetadata.ServerUrl,
            "/api/studio/session/refresh",
            request,
            cancellationToken).ConfigureAwait(true);
        RequireSessionEpoch(expectedSessionEpoch);
        CloudEraCapabilitiesSnapshot capabilities =
            await LoadCapabilitiesAsync(
                savedMetadata.ServerUrl,
                cancellationToken).ConfigureAwait(true);
        RequireSessionEpoch(expectedSessionEpoch);
        bool persistCredential =
            !string.IsNullOrWhiteSpace(session.LicenseId) &&
            !string.IsNullOrWhiteSpace(session.ActivationId) &&
            (!savedCredential.LicenseId.Equals(
                 session.LicenseId,
                 StringComparison.Ordinal) ||
             !savedCredential.ActivationId.Equals(
                 session.ActivationId,
                 StringComparison.Ordinal));
        // Keep the legacy value in the protected cache until both server calls
        // have succeeded. Only then is it safe to adopt canonical locally;
        // credentials on an unrelated device never pass this migration path.
        if (StudioDeviceIdentity.TryMigrateStoredFingerprint(
                savedCredential.DeviceFingerprint,
                out string canonicalFingerprint))
        {
            savedCredential.DeviceFingerprint = canonicalFingerprint;
            persistCredential = true;
        }
        CommitSession(
            expectedSessionEpoch,
            savedMetadata,
            savedCredential,
            session,
            capabilities,
            persistCredential,
            entitlements: license.Entitlements);
    }

    private async Task<CloudEraCapabilitiesSnapshot> LoadCapabilitiesAsync(
        string serverUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using CloudEraCapabilitiesClient client = new(new Uri(NormalizeServerUrl(serverUrl)));
            CloudEraCapabilitiesSnapshot capabilities = await client.GetAsync(cancellationToken).ConfigureAwait(true);
            CloudEraCapabilityPolicy.Require(capabilities, CloudEraFeatures.Projects);
            CloudEraCapabilityPolicy.Require(capabilities, CloudEraFeatures.AlbumRevisions);
            CloudEraCapabilityPolicy.Require(capabilities, CloudEraFeatures.OptimisticConcurrency);
            CloudEraCapabilityPolicy.Require(capabilities, CloudEraFeatures.IdempotentSync);
            return capabilities;
        }
        catch (CloudEraContractMismatchException error) when (
            CanUseLegacyLoopbackCapabilities(serverUrl, error, StudioReleaseInfo.IsDevelopmentBuild))
        {
            return CreateLegacyLoopbackCapabilities();
        }
        catch (Exception error) when (error is CloudEraContractMismatchException or HttpRequestException or TaskCanceledException)
        {
            throw new StudioAccountException("Cloud ERA API contract тохирохгүй байна. " + error.Message);
        }
    }

    internal static bool CanUseLegacyLoopbackCapabilities(
        string serverUrl,
        CloudEraContractMismatchException error,
        bool isDevelopmentBuild)
    {
        if (!isDevelopmentBuild || error.StatusCode != 404 ||
            !Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.IsLoopback && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    internal static CloudEraCapabilitiesSnapshot CreateLegacyLoopbackCapabilities() => new(
        CloudEraCapabilityPolicy.SupportedApiVersion,
        "/api/cloud-era/v1",
        "",
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [CloudEraFeatures.Projects] = true,
            [CloudEraFeatures.Organizations] = true,
            [CloudEraFeatures.Collaboration] = true,
            [CloudEraFeatures.ConceptArchitectAssignment] = true,
            [CloudEraFeatures.ParticipantRoleManagement] = true,
            [CloudEraFeatures.SourcePackagesV4] = true,
            [CloudEraFeatures.SourcePackageCasV1] = false,
            [CloudEraFeatures.AlbumRevisions] = true,
            [CloudEraFeatures.AlbumComponentMergeV1] = false,
            [CloudEraFeatures.ContributorOwnedComponentsV1] = false,
            [CloudEraFeatures.OptimisticConcurrency] = false,
            [CloudEraFeatures.IdempotentSync] = true,
            [CloudEraFeatures.RelationshipBoundary] = true,
            [CloudEraFeatures.NativeSourceRemainsLocal] = true,
        });

    internal static bool NeedsCapabilityRefresh(
        CloudEraCapabilitiesSnapshot? capabilities,
        string feature) =>
        capabilities is null || !CloudEraCapabilityPolicy.Supports(capabilities, feature);

    private async Task EnsureCapabilityAsync(string feature, CancellationToken cancellationToken)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        if (NeedsCapabilityRefresh(CurrentCapabilities, feature))
        {
            StudioSessionTransition transition = CaptureSessionTransition();
            StudioAccountSession session = Current
                ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
            CloudEraCapabilitiesSnapshot capabilities =
                await LoadCapabilitiesAsync(
                    session.ServerUrl,
                    cancellationToken).ConfigureAwait(true);
            RequireSessionEpoch(transition.Epoch);
            CurrentCapabilities = capabilities;
        }
        RequireCapability(feature);
    }

    private void RequireCapability(string feature)
    {
        CloudEraCapabilitiesSnapshot capabilities = CurrentCapabilities
            ?? throw new StudioAccountException("Cloud ERA capabilities ачаалагдаагүй байна. Дахин нэвтэрнэ үү.");
        try
        {
            CloudEraCapabilityPolicy.Require(capabilities, feature);
        }
        catch (CloudEraContractMismatchException error)
        {
            throw new StudioAccountException(error.Message);
        }
    }

    /// <summary>
    /// Decides whether Studio may open for the account as things currently
    /// stand. This is the single place the enforcement switch and the decision
    /// rules meet; callers only read the outcome.
    /// </summary>
    public StudioCompanionDecision EvaluateCompanionAccess() =>
        StudioCompanionPolicy.Evaluate(
            StudioCompanionEnforcement.IsEnabledFor(Current?.ServerUrl ?? metadata?.ServerUrl),
            CompanionAnswer,
            StatedCompanionEntitlement,
            CachedCompanionEntitlement,
            DateTimeOffset.UtcNow);

    private bool RecordCompanionEntitlement(
        StudioActivationCredential savedCredential,
        StudioCloudEntitlements entitlements)
    {
        var stated = new StudioCompanionEntitlement(
            entitlements.StudioCompanion,
            entitlements.CompanionExpiresAtUtc,
            DateTimeOffset.UtcNow);
        CompanionAnswer = StudioCompanionServerAnswer.Stated;
        StatedCompanionEntitlement = stated;
        CachedCompanionEntitlement = stated;

        // Rewrite the stored grant when it changed, when there was none, or
        // when it is old enough that the grace window is worth rolling forward.
        // Persisting on every refresh would rewrite the credential all day.
        bool changed =
            savedCredential.CompanionCheckedAtUtc == default ||
            savedCredential.CompanionGranted != stated.StudioCompanion ||
            savedCredential.CompanionExpiresAtUtc != stated.CompanionExpiresAtUtc ||
            stated.CheckedAtUtc - savedCredential.CompanionCheckedAtUtc > TimeSpan.FromHours(12);
        savedCredential.CompanionGranted = stated.StudioCompanion;
        savedCredential.CompanionExpiresAtUtc = stated.CompanionExpiresAtUtc;
        savedCredential.CompanionCheckedAtUtc = stated.CheckedAtUtc;
        return changed;
    }

    private void LoadCachedCompanionEntitlement(StudioActivationCredential savedCredential) =>
        CachedCompanionEntitlement = StudioCompanionPolicy.ReadStoredGrant(
            savedCredential.CompanionGranted,
            savedCredential.CompanionExpiresAtUtc,
            savedCredential.CompanionCheckedAtUtc,
            savedCredential.DeviceFingerprint,
            StudioDeviceIdentity.FingerprintForStoredGrant(
                savedCredential.DeviceFingerprint));

    private void CommitSession(
        long expectedSessionEpoch,
        StudioAccountMetadata savedMetadata,
        StudioActivationCredential savedCredential,
        StudioSessionResponse session,
        CloudEraCapabilitiesSnapshot capabilities,
        bool persistCredential,
        StudioCloudEntitlements? entitlements = null)
    {
        StudioCloudEntitlements? statedEntitlements = session.Entitlements ?? entitlements;
        // A server that never mentions entitlements is an older deployment, not
        // an account without a licence, so the cached grant is left untouched.
        if (statedEntitlements is null)
        {
            CompanionAnswer = StudioCompanionServerAnswer.FieldAbsent;
            StatedCompanionEntitlement = null;
        }
        else
        {
            persistCredential |= RecordCompanionEntitlement(savedCredential, statedEntitlements);
        }

        sessionTransitions.Commit(expectedSessionEpoch, () =>
        {
            if (persistCredential &&
                !string.IsNullOrWhiteSpace(session.LicenseId) &&
                !string.IsNullOrWhiteSpace(session.ActivationId))
            {
                savedCredential.LicenseId = session.LicenseId;
                savedCredential.ActivationId = session.ActivationId;
            }

            savedMetadata.LicenseType = session.LicenseType;
            savedMetadata.LicenseExpiresAtUtc =
                session.LicenseExpiresAtUtc;
            metadata = savedMetadata;
            credential = savedCredential;
            CurrentCapabilities = capabilities;
            SetCurrent(savedMetadata, session);
            RequireSessionEpoch(expectedSessionEpoch);
            if (persistCredential)
            {
                credentialStore.Write(
                    CredentialTarget(savedMetadata),
                    savedMetadata.Email,
                    savedCredential);
            }
            WriteMetadata(savedMetadata);
            LastError = "";
        });
    }

    private void SetCurrent(StudioAccountMetadata savedMetadata, StudioSessionResponse session)
    {
        if (string.IsNullOrWhiteSpace(session.AccessToken))
            throw new StudioAccountException("Cloud ERA session token хоосон байна.");
        bool sessionSuppliedCanonicalDisplay = !string.IsNullOrWhiteSpace(session.DisplayName);
        string displayName = sessionSuppliedCanonicalDisplay
            ? session.DisplayName
            : savedMetadata.DisplayName;
        string familyName = !string.IsNullOrWhiteSpace(session.FamilyName)
            ? session.FamilyName
            : savedMetadata.FamilyName;
        string givenName = !string.IsNullOrWhiteSpace(session.GivenName)
            ? session.GivenName
            : savedMetadata.GivenName;
        StudioRegisteredPersonName registeredName = StudioRegisteredPersonNameResolver.Resolve(
            familyName,
            givenName,
            displayName,
            sessionSuppliedCanonicalDisplay);
        savedMetadata.DisplayName = registeredName.DisplayName;
        savedMetadata.FamilyName = registeredName.FamilyName;
        savedMetadata.GivenName = registeredName.GivenName;
        if (!string.IsNullOrWhiteSpace(session.ProfileImageUrl))
            savedMetadata.ProfileImageUrl = session.ProfileImageUrl;
        Current = new StudioAccountSession(
            savedMetadata.ServerUrl,
            session.AccountEmail,
            registeredName.DisplayName,
            registeredName.FamilyName,
            registeredName.GivenName,
            savedMetadata.ProfileImageUrl,
            session.LicenseType,
            session.LicenseExpiresAtUtc,
            session.ExpiresAtUtc,
            session.AccessToken);
        if (!string.IsNullOrWhiteSpace(session.AccountEmail))
            registeredPersonNames[session.AccountEmail] = registeredName;
        StateChanged?.Invoke();
    }

    // Sheet comments. A reviewer's remarks on a drawing, kept in their own
    // collection rather than in the album, so the album's merge rules never
    // have to reason about them and an approver can write one without being
    // able to change the drawing.

    public async Task<StudioSheetCommentList> ListSheetCommentsAsync(
        string projectId,
        string? pageIdentity = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        string path = "/api/cloud-era/v1/projects/" +
            Uri.EscapeDataString(projectId) + "/sheet-comments";
        if (!string.IsNullOrWhiteSpace(pageIdentity))
            path += "?pageIdentity=" + Uri.EscapeDataString(pageIdentity.Trim());
        return await GetAuthorizedAsync<StudioSheetCommentList>(path, cancellationToken)
            .ConfigureAwait(true);
    }

    public async Task<StudioSheetCommentList> AddSheetCommentAsync(
        string projectId,
        StudioSheetCommentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioSheetCommentCreateRequest, StudioSheetCommentList>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) + "/sheet-comments",
            request,
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioSheetCommentList> ReplyToSheetCommentAsync(
        string projectId,
        string commentId,
        string body,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioSheetCommentReplyRequest, StudioSheetCommentList>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
                "/sheet-comments/" + Uri.EscapeDataString(commentId) + "/replies",
            new StudioSheetCommentReplyRequest { Body = body ?? "" },
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioSheetCommentList> SetSheetCommentStatusAsync(
        string projectId,
        string commentId,
        string status,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioSheetCommentStatusRequest, StudioSheetCommentList>(
            "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
                "/sheet-comments/" + Uri.EscapeDataString(commentId) + "/status",
            new StudioSheetCommentStatusRequest { Status = status ?? "" },
            cancellationToken).ConfigureAwait(true);
    }

    public async Task<StudioSheetCommentList> DeleteSheetCommentAsync(
        string projectId,
        string commentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current
            ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            BuildUri(
                session.ServerUrl,
                "/api/cloud-era/v1/projects/" + Uri.EscapeDataString(projectId) +
                    "/sheet-comments/" + Uri.EscapeDataString(commentId)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(true);
        return await ReadResponseAsync<StudioSheetCommentList>(response, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The rules the server hands out, so behaviour it owns can change without
    /// updating anyone's Studio.
    /// </summary>
    /// <remarks>
    /// Read here rather than through the generated Cloud ERA client because
    /// that client is regenerated on the server's schedule and drops fields it
    /// was not generated with - which for a channel whose whole purpose is to
    /// carry new rules would mean each new rule silently disappearing until the
    /// next regeneration.
    ///
    /// Failure is not an error worth surfacing: no rules means every default
    /// stands, which is exactly how an older Studio already behaves.
    /// </remarks>
    public async Task<IReadOnlyList<StudioServerRule>> GetServerRulesAsync(
        CancellationToken cancellationToken = default)
    {
        StudioAccountSession? session = Current;
        if (session is null)
            return [];

        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                BuildUri(session.ServerUrl, "/api/cloud-era/v1/capabilities"));
            using HttpResponseMessage response =
                await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
                return [];

            StudioServerRulesResponse? rules = await response.Content
                .ReadFromJsonAsync<StudioServerRulesResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(true);
            return rules?.Rules ?? [];
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    // -----------------------------------------------------------------------
    // Bot seats, bot state, PIN, link invitations.
    // -----------------------------------------------------------------------

    private static string OrgPath(string organizationId) =>
        "/api/cloud-era/v1/organizations/" + Uri.EscapeDataString(organizationId);

    private static string SeatPath(string organizationId, string botId) =>
        OrgPath(organizationId) + "/bot-seats/" + Uri.EscapeDataString(botId);

    public async Task<StudioCloudBotSeatListResponse> ListBotSeatsAsync(
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioCloudBotSeatListResponse>(
            OrgPath(organizationId) + "/bot-seats",
            cancellationToken).ConfigureAwait(true) ?? new StudioCloudBotSeatListResponse();
    }

    public async Task<StudioCloudBotSeat> CreateBotSeatAsync(
        string organizationId,
        string displayName,
        string internalEmail,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudBotSeatCreateRequest, StudioCloudBotSeat>(
            OrgPath(organizationId) + "/bot-seats",
            new StudioCloudBotSeatCreateRequest
            {
                DisplayName = displayName?.Trim() ?? "",
                InternalEmail = internalEmail?.Trim() ?? "",
            },
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер ботын суудлын хариу буцаасангүй.");
    }

    /// <summary>
    /// Sets or replaces a seat's PIN. Four digits, and nothing more is asked of
    /// it: 0000 and 1234 are allowed on purpose. The response says whether the
    /// seated device must register again, which the caller must show in the
    /// same breath as the new PIN.
    /// </summary>
    public async Task<StudioCloudBotPinSetResponse> SetBotPinAsync(
        string organizationId,
        string botId,
        string pin,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PutAuthorizedAsync<StudioCloudBotPinSetRequest, StudioCloudBotPinSetResponse>(
            SeatPath(organizationId, botId) + "/pin",
            new StudioCloudBotPinSetRequest { Pin = pin?.Trim() ?? "" },
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер ПИН-ий хариу буцаасангүй.");
    }

    /// <summary>Owner-only. The bot cannot read its own PIN and the employee never reads it.</summary>
    public async Task<StudioCloudBotPinReveal> RevealBotPinAsync(
        string organizationId,
        string botId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioCloudBotPinReveal>(
            SeatPath(organizationId, botId) + "/pin",
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер ПИН буцаасангүй.");
    }

    public async Task UnlockBotPinAsync(
        string organizationId,
        string botId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        _ = await PostAuthorizedAsync<object?>(
            SeatPath(organizationId, botId) + "/pin/unlock",
            cancellationToken).ConfigureAwait(true);
    }

    private async Task<StudioCloudBotStateEnterResponse> RequestBotStateAsync(
        string organizationId,
        string botId,
        CancellationToken cancellationToken)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.Fingerprints;
        return await PostAuthorizedAsync<StudioCloudBotStateEnterRequest, StudioCloudBotStateEnterResponse>(
            SeatPath(organizationId, botId) + "/state",
            new StudioCloudBotStateEnterRequest
            {
                DeviceFingerprint = fingerprints.Canonical,
                LegacyDeviceFingerprint = fingerprints.Legacy,
                DeviceName = Environment.MachineName,
            },
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер ботын төлөвийн хариу буцаасангүй.");
    }

    /// <summary>
    /// Enters bot state on this machine: the server seats the device, then the
    /// stored owner credential is erased here.
    ///
    /// FAILING TO ERASE IS FAILING TO ENTER. The server cannot see this
    /// machine's credential vault, so a half-done transition would leave it
    /// believing the owner is out while a working refresh token sits on the
    /// disk - and becoming a bot would be a switch the employee could flip
    /// back. So the erase runs straight after the server call, and if it
    /// throws, the seat is released again and the failure is raised.
    /// </summary>
    public async Task<StudioCloudBotStateEnterResponse> EnterBotStateAsync(
        string organizationId,
        string botId,
        CancellationToken cancellationToken = default)
    {
        StudioCloudBotStateEnterResponse entered =
            await RequestBotStateAsync(organizationId, botId, cancellationToken).ConfigureAwait(true);
        try
        {
            EraseOwnerCredentialForBotState();
        }
        catch (Exception)
        {
            try
            {
                await LeaveBotStateAsync(organizationId, botId, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception)
            {
                // The rollback failed too. The original failure is the one that
                // must reach the user; it already says not to trust the state.
            }
            throw;
        }
        return entered;
    }

    public async Task LeaveBotStateAsync(
        string organizationId,
        string botId,
        CancellationToken cancellationToken = default)
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            BuildUri(session.ServerUrl, SeatPath(organizationId, botId) + "/state"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response =
            await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        _ = await ReadResponseAsync<object?>(response, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// What a seated device asks at start-up: which seat is this, and is it
    /// locked. Carries both fingerprint forms.
    /// </summary>
    public async Task<StudioCloudBotStateResume> ResumeBotStateAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.Fingerprints;
        return await PostAuthorizedAsync<StudioCloudBotStateResumeRequest, StudioCloudBotStateResume>(
            "/api/cloud-era/v1/bot-state/resume",
            new StudioCloudBotStateResumeRequest
            {
                DeviceFingerprint = fingerprints.Canonical,
                LegacyDeviceFingerprint = fingerprints.Legacy,
            },
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер ботын төлөв буцаасангүй.");
    }

    public async Task<StudioCloudBotInvitation> InviteBotMemberAsync(
        string organizationId,
        string botId,
        string projectId,
        IReadOnlyList<string> roles,
        string targetEmail,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudBotInvitationCreateRequest, StudioCloudBotInvitation>(
            SeatPath(organizationId, botId) + "/invitations",
            new StudioCloudBotInvitationCreateRequest
            {
                ProjectId = projectId?.Trim() ?? "",
                Roles = [.. roles ?? []],
                TargetEmail = targetEmail?.Trim() ?? "",
            },
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер урилгын хариу буцаасангүй.");
    }

    public async Task<StudioCloudBotInvitationListResponse> ListMyBotInvitationsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await GetAuthorizedAsync<StudioCloudBotInvitationListResponse>(
            "/api/cloud-era/v1/bot-invitations",
            cancellationToken).ConfigureAwait(true) ?? new StudioCloudBotInvitationListResponse();
    }

    public async Task<StudioCloudBotInvitationAccepted> AcceptBotInvitationAsync(
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        return await PostAuthorizedAsync<StudioCloudBotInvitationAccepted>(
            "/api/cloud-era/v1/bot-invitations/" + Uri.EscapeDataString(invitationId) + "/accept",
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер урилга зөвшөөрсөн хариу буцаасангүй.");
    }

    public async Task DeclineBotInvitationAsync(
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        _ = await PostAuthorizedAsync<object?>(
            "/api/cloud-era/v1/bot-invitations/" + Uri.EscapeDataString(invitationId) + "/decline",
            cancellationToken).ConfigureAwait(true);
    }

    public async Task CancelBotInvitationAsync(
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        _ = await PostAuthorizedAsync<object?>(
            "/api/cloud-era/v1/bot-invitations/" + Uri.EscapeDataString(invitationId) + "/cancel",
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Erases this machine's stored owner credential. Deliberately NOT
    /// SignOut(): that one swallows an IOException while removing the metadata,
    /// which would leave credential-gone-metadata-present without saying so.
    /// Here every failure throws, because the caller treats a failure as a
    /// refusal to enter bot state.
    /// </summary>
    private void EraseOwnerCredentialForBotState()
    {
        StudioAccountMetadata? saved = metadata ?? ReadMetadata();
        if (saved is not null)
        {
            // Throws if the vault refuses. A credential that was already absent
            // is not a failure: the post-condition is that no owner credential
            // remains on this machine.
            credentialStore.Delete(CredentialTarget(saved));
        }

        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }

        Current = null;
        CurrentCapabilities = null;
        metadata = null;
    }

    /// <summary>
    /// The seat's own credential while this machine is unlocked as a bot. Held
    /// in memory only: at rest it lives sealed under the PIN, so a machine that
    /// has not been unlocked cannot act as the seat even with the file in hand.
    /// </summary>
    private StudioCloudBotStateToken? botToken;

    public bool HasBotSession => botToken is not null &&
        !string.IsNullOrWhiteSpace(botToken.AccessToken);

    public void UseBotToken(StudioCloudBotStateToken? token) => botToken = token;

    private StudioCloudBotStateToken RequireBotToken() =>
        botToken ?? throw new StudioAccountException(
            "Энэ төхөөрөмж ботын эрхээр нэвтрээгүй байна.");

    /// <summary>
    /// Bot-scoped calls carry the SEAT's credential, never the owner's - the
    /// owner's is erased when the device is seated, and nothing turns one into
    /// the other.
    /// </summary>
    private async Task<TResponse> PostBotAuthorizedAsync<TRequest, TResponse>(
        string path,
        TRequest value,
        CancellationToken cancellationToken)
    {
        StudioCloudBotStateToken token = RequireBotToken();
        using HttpRequestMessage request = new(HttpMethod.Post, BuildUri(BotServerUrl, path))
        {
            Content = JsonContent.Create(value, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            string.IsNullOrWhiteSpace(token.TokenType) ? "Bearer" : token.TokenType,
            token.AccessToken);
        using HttpResponseMessage response =
            await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Where a seated machine talks to. There is no signed-in session to read a
    /// server from, so the public address is used unless one was recorded.
    /// </summary>
    private string BotServerUrl =>
        Current?.ServerUrl is { Length: > 0 } signedIn ? signedIn : PublicServerUrl;

    /// <summary>
    /// Asks for the seat's credential using nothing but the device's own
    /// fingerprints - both forms, because one machine has two valid values and
    /// a request carrying one proves nothing about a record stored under the
    /// other. This is the call a locked machine makes after the PIN opens it.
    /// </summary>
    public async Task<StudioCloudBotStateToken> RequestBotTokenAsync(
        CancellationToken cancellationToken = default)
    {
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.Fingerprints;
        var body = new StudioCloudBotStateResumeRequest
        {
            DeviceFingerprint = fingerprints.Canonical,
            LegacyDeviceFingerprint = fingerprints.Legacy,
        };
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            BuildUri(BotServerUrl, "/api/cloud-era/v1/bot-state/token"))
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        using HttpResponseMessage response =
            await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        StudioCloudBotStateToken token =
            await ReadResponseAsync<StudioCloudBotStateToken>(response, cancellationToken)
                .ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер ботын токен буцаасангүй.");
        botToken = token;
        return token;
    }

    /// <summary>
    /// What this seat may open, read with the seat's own credential. The list
    /// is the whole of it: a project absent from it is refused, whatever a
    /// local file says.
    /// </summary>
    public async Task<StudioCloudBotStateResume> ResumeAsBotAsync(
        CancellationToken cancellationToken = default)
    {
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.Fingerprints;
        return await PostBotAuthorizedAsync<StudioCloudBotStateResumeRequest, StudioCloudBotStateResume>(
            "/api/cloud-era/v1/bot-state/resume",
            new StudioCloudBotStateResumeRequest
            {
                DeviceFingerprint = fingerprints.Canonical,
                LegacyDeviceFingerprint = fingerprints.Legacy,
            },
            cancellationToken).ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер ботын төлөв буцаасангүй.");
    }

    /// <summary>
    /// Reports that this device locked itself after wrong PINs. Carries the
    /// seat's credential, because by then there is no owner session here.
    /// </summary>
    public async Task ReportBotLockoutAsync(CancellationToken cancellationToken = default)
    {
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.Fingerprints;
        _ = await PostBotAuthorizedAsync<StudioCloudBotPinLockoutRequest, object?>(
            "/api/cloud-era/v1/bot-state/pin/lockout",
            new StudioCloudBotPinLockoutRequest
            {
                DeviceFingerprint = fingerprints.Canonical,
                LegacyDeviceFingerprint = fingerprints.Legacy,
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes a seat. Owner only. A device sitting in it is released by the
    /// deletion itself - the response says whether one was - and the seat's
    /// history is kept: intervals and assignments hang off the bot id, which is
    /// never reused, so dropping the row would leave them pointing at nothing.
    /// </summary>
    public async Task<StudioCloudBotSeatDeleted> DeleteBotSeatAsync(
        string organizationId,
        string botId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(true);
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            BuildUri(session.ServerUrl, SeatPath(organizationId, botId)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response =
            await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<StudioCloudBotSeatDeleted>(response, cancellationToken)
            .ConfigureAwait(true)
            ?? throw new StudioAccountException("Сервер суудал устгасан хариу буцаасангүй.");
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
        bool relationshipBoundaryAcknowledged = false,
        string ifMatchToken = "")
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio бүртгэлээр нэвтэрнэ үү.");
        using HttpRequestMessage request = new(HttpMethod.Put, BuildUri(session.ServerUrl, path))
        {
            Content = JsonContent.Create(value, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (!string.IsNullOrWhiteSpace(ifMatchToken))
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue(
                '"' + ifMatchToken.Trim().Trim('"') + '"'));
        }
        AddRelationshipBoundaryAcknowledgement(request, relationshipBoundaryAcknowledged);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(true);
    }

    private async Task SendAuthorizedNoContentAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        bool relationshipBoundaryAcknowledged = false,
        string ifMatchToken = "")
    {
        StudioAccountSession session = Current ?? throw new StudioAccountException("Studio account is required.");
        using HttpRequestMessage request = new(method, BuildUri(session.ServerUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (!string.IsNullOrWhiteSpace(ifMatchToken))
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue(
                '"' + ifMatchToken.Trim().Trim('"') + '"'));
        }
        AddRelationshipBoundaryAcknowledgement(request, relationshipBoundaryAcknowledged);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(true);
        if (response.IsSuccessStatusCode)
            return;
        await ReadResponseAsync<object>(response, cancellationToken).ConfigureAwait(true);
    }

    private static void RequireExactConcurrencyToken(
        string? token,
        string message)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new StudioAccountException(message);
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
            throw new StudioAccountException(
                message,
                response.StatusCode,
                error?.Code ?? "",
                StudioCloudTraceIdentifier.Resolve(response, error),
                error?.FieldErrors,
                error?.CurrentSourceId ?? "",
                error?.CurrentRevisionId ?? "",
                error?.CurrentOrganizationConcurrencyToken ?? "");
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

    /// <summary>
    /// Resolves a server-supplied path or URL for a request that will carry the
    /// session's bearer token. The token must never travel to any host other
    /// than the session's own server, so a value resolving elsewhere is refused.
    /// </summary>
    internal static bool TryBuildSameOriginUri(string serverUrl, string pathOrUrl, out Uri uri)
    {
        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        uri = new Uri(baseUri, pathOrUrl.TrimStart('/'));
        return string.Equals(
            uri.GetLeftPart(UriPartial.Authority),
            baseUri.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);
    }

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

    // The same string the request base sends as HostVersion. Read once, in one
    // place: two readers of the same attribute would agree until one of them
    // was "tidied".
    private static string AppVersion => StudioHost.Version;

    private sealed class StudioAccountMetadata
    {
        public string ServerUrl { get; set; } = "";
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string GivenName { get; set; } = "";
        public string ProfileImageUrl { get; set; } = "";
        public string LicenseType { get; set; } = "";
        public DateTimeOffset LicenseExpiresAtUtc { get; set; }
    }

    private sealed class StudioActivationCredential
    {
        public string LicenseId { get; set; } = "";
        public string ActivationId { get; set; } = "";
        public string DeviceFingerprint { get; set; } = "";

        /// <summary>
        /// Last companion entitlement this device confirmed, so Studio can open
        /// offline for a while. It lives in the credential blob rather than a
        /// settings file because that store is encrypted for this user on this
        /// machine: the grant cannot be hand-edited into a longer one. A default
        /// <see cref="CompanionCheckedAtUtc"/> means nothing was ever confirmed.
        /// </summary>
        public bool CompanionGranted { get; set; }

        public DateTimeOffset? CompanionExpiresAtUtc { get; set; }

        public DateTimeOffset CompanionCheckedAtUtc { get; set; }
    }
}

internal static class StudioDeviceIdentity
{
    private const string CanonicalSalt = "Erk-S device v1";
    private const string LegacyStudioSalt = "Erk-S Studio device v1";

    public static StudioDeviceFingerprints Fingerprints { get; } = BuildFingerprints();

    public static string Fingerprint => Fingerprints.Canonical;

    private static StudioDeviceFingerprints BuildFingerprints()
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

        return BuildFingerprints(
            Environment.MachineName,
            Environment.UserName,
            machineGuid,
            sid);
    }

    internal static StudioDeviceFingerprints BuildFingerprints(
        string machineName,
        string userName,
        string machineGuid,
        string sid) =>
        new(
            Hash(CanonicalSalt, machineName, userName, machineGuid, sid),
            Hash(LegacyStudioSalt, machineName, userName, machineGuid, sid));

    internal static StudioDeviceFingerprintValidation ValidateStoredFingerprint(
        string? storedFingerprint)
    {
        string stored = (storedFingerprint ?? "").Trim();
        if (stored.Equals(Fingerprints.Canonical, StringComparison.Ordinal))
            return new StudioDeviceFingerprintValidation(true, false);
        if (stored.Equals(Fingerprints.Legacy, StringComparison.Ordinal))
            return new StudioDeviceFingerprintValidation(true, true);
        return new StudioDeviceFingerprintValidation(false, false);
    }

    internal static bool TryMigrateStoredFingerprint(
        string? storedFingerprint,
        out string migratedFingerprint)
    {
        StudioDeviceFingerprintValidation validation =
            ValidateStoredFingerprint(storedFingerprint);
        migratedFingerprint = validation.RequiresCanonicalRewrite
            ? Fingerprints.Canonical
            : (storedFingerprint ?? "").Trim();
        return validation.RequiresCanonicalRewrite;
    }

    internal static string FingerprintForStoredGrant(string? storedFingerprint) =>
        ValidateStoredFingerprint(storedFingerprint).IsValid
            ? (storedFingerprint ?? "").Trim()
            : Fingerprint;

    private static string Hash(
        string salt,
        string machineName,
        string userName,
        string machineGuid,
        string sid)
    {
        string material = string.Join(
            "|",
            salt,
            machineName,
            userName,
            machineGuid,
            sid);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }
}

internal sealed record StudioDeviceFingerprints(string Canonical, string Legacy);

internal readonly record struct StudioDeviceFingerprintValidation(
    bool IsValid,
    bool RequiresCanonicalRewrite);

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
