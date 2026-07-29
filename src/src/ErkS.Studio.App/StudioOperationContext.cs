using ErkS.Platform.Core;
using System.IO;

namespace ErkS.Studio;

internal sealed record StudioOperationContext(
    long WorkspaceEpoch,
    long AccountEpoch,
    string ProjectId,
    string ProjectPath,
    string ServerProjectId,
    string AccountEmail,
    string AccountServerUrl)
{
    public static StudioOperationContext Capture(
        bool hasOpenProject,
        ProjectWorkspace? project,
        string? projectPath,
        StudioAccountSession? account,
        long workspaceEpoch,
        long accountEpoch)
    {
        if (!hasOpenProject || project is null)
        {
            return new StudioOperationContext(
                workspaceEpoch,
                accountEpoch,
                "",
                "",
                "",
                Normalize(account?.Email),
                NormalizeUrl(account?.ServerUrl));
        }

        return new StudioOperationContext(
            workspaceEpoch,
            accountEpoch,
            Normalize(project.ProjectId),
            NormalizePath(projectPath),
            Normalize(project.Cloud?.ServerProjectId),
            Normalize(account?.Email),
            NormalizeUrl(account?.ServerUrl));
    }

    public bool Matches(
        bool hasOpenProject,
        ProjectWorkspace? project,
        string? projectPath,
        StudioAccountSession? account,
        long workspaceEpoch,
        long accountEpoch)
    {
        StudioOperationContext current =
            Capture(
                hasOpenProject,
                project,
                projectPath,
                account,
                workspaceEpoch,
                accountEpoch);
        return WorkspaceEpoch == current.WorkspaceEpoch &&
            AccountEpoch == current.AccountEpoch &&
            ProjectId.Equals(current.ProjectId, StringComparison.Ordinal) &&
            ProjectPath.Equals(
                current.ProjectPath,
                StringComparison.OrdinalIgnoreCase) &&
            ServerProjectId.Equals(
                current.ServerProjectId,
                StringComparison.OrdinalIgnoreCase) &&
            AccountEmail.Equals(
                current.AccountEmail,
                StringComparison.OrdinalIgnoreCase) &&
            AccountServerUrl.Equals(
                current.AccountServerUrl,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeUrl(string? value) =>
        (value ?? "").Trim().TrimEnd('/').ToLowerInvariant();

    private static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        try
        {
            return Path.GetFullPath(value).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return value.Trim();
        }
    }
}

internal sealed record StudioWorkspaceLifecycleActivity(
    bool ProjectOpen,
    bool ProjectAccessRefresh,
    bool SyncPreparation,
    bool Sync,
    bool SourceRefresh,
    bool SourceRemoval,
    bool CompanySave,
    bool FoundationSave,
    bool RelationshipMutation,
    bool ChatSend);

internal sealed record StudioWorkspaceLifecycleDecision(
    bool Allowed,
    string ReasonCode,
    string Message);

internal static class StudioCloudSourceBindingContinuationPolicy
{
    public static bool CanApply(
        StudioOperationContext capturedContext,
        ProjectDesignSource expectedSource,
        bool hasOpenProject,
        ProjectWorkspace? currentProject,
        string? currentProjectPath,
        StudioAccountSession? currentAccount,
        long workspaceEpoch,
        long accountEpoch)
    {
        ArgumentNullException.ThrowIfNull(capturedContext);
        ArgumentNullException.ThrowIfNull(expectedSource);
        if (!capturedContext.Matches(
                hasOpenProject,
                currentProject,
                currentProjectPath,
                currentAccount,
                workspaceEpoch,
                accountEpoch))
        {
            return false;
        }

        return currentProject?.Sources.Any(
            source => ReferenceEquals(source, expectedSource)) == true;
    }
}

internal static class StudioWorkspaceLifecyclePolicy
{
    public static StudioWorkspaceLifecycleDecision Evaluate(
        StudioWorkspaceLifecycleActivity activity)
    {
        if (activity.ProjectAccessRefresh)
            return Blocked("lifecycle_project_access_refresh_running", "төслийн access шалгалт");
        if (activity.Sync || activity.SyncPreparation)
            return Blocked("lifecycle_cloud_sync_running", "Cloud Sync");
        if (activity.SourceRefresh)
            return Blocked("lifecycle_source_refresh_running", "Source Refresh");
        if (activity.SourceRemoval)
            return Blocked("lifecycle_source_removal_running", "source retire");
        if (activity.CompanySave)
            return Blocked("lifecycle_company_save_running", "байгууллагын хадгалалт");
        if (activity.FoundationSave)
            return Blocked("lifecycle_project_save_running", "төслийн мэдээллийн хадгалалт");
        if (activity.RelationshipMutation)
            return Blocked("lifecycle_relationship_change_running", "эрх/хариуцлагын өөрчлөлт");
        if (activity.ChatSend)
            return Blocked("lifecycle_chat_send_running", "мессеж илгээх");
        if (activity.ProjectOpen)
            return Blocked("lifecycle_project_open_running", "төсөл нээх");
        return new StudioWorkspaceLifecycleDecision(true, "", "");
    }

    private static StudioWorkspaceLifecycleDecision Blocked(
        string reasonCode,
        string operation) =>
        new(
            false,
            reasonCode,
            $"{operation} дуусаагүй байна. Төсөл эсвэл бүртгэл солихын өмнө энэ үйлдлийг дуусгана уу.");
}

internal sealed class StudioOperationContextChangedException : OperationCanceledException
{
    public StudioOperationContextChangedException(string operation)
        : base(
            $"{operation} cancelled because the signed-in account or open project changed.")
    {
    }
}
