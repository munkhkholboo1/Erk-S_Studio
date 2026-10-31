namespace ErkS.Studio.App.Tests;

/// <summary>
/// Erin, 2026-10-31: the refresh ended on «⚠ Альбум бүрэн бус — 1 хэсэг илгээгдээгүй» for an ATD
/// component this device does not hold and the Cloud holds whole. The sync refused it, the
/// indicator's quicker rule counted it as sendable. What the sync refuses is counted blocked.
/// </summary>
public sealed class AREADOnlyComponentIsNotCountedAsWaitingTests
{
    [Fact]
    public void TheSyncRecordsWhatItRefusedAndTheSplitReadsIt()
    {
        string shell = ReadAppSource("ShellView.cs");
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");

        Assert.Contains("RememberSyncRefusedComponents(allPendingAlbumComponents, authorizedPendingAlbumComponents);", shell, StringComparison.Ordinal);
        Assert.Contains("renderable.Contains(code) && !syncRefusedComponentCodes.Contains(code)", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePreviewAloneRecordsWhatItRefuses()
    {
        string shell = ReadAppSource("ShellView.cs");

        Assert.Contains(".Except(plan.Blocked.Select(item => item.Code), StringComparer.OrdinalIgnoreCase)", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySyncFirstMarksCompanyPagesWhoseScansArrived()
    {
        string shell = ReadAppSource("ShellView.cs");
        string collaboration = ReadAppSource("ShellView.Collaboration.cs");

        Assert.Contains("RequireOperationContext(operationContext, \"cloud_sync_prepare\");\n            MarkCompanyPagesWhoseCopiesArrived();", shell.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("if (MarkCompanyPagesWhoseCopiesArrived() || fetched > 0)", collaboration, StringComparison.Ordinal);
    }

    [Fact]
    public void AScanOnlyTheProjectHoldsIsPutIntoItsOrganisation()
    {
        string collaboration = ReadAppSource("ShellView.Collaboration.cs");

        int send = collaboration.IndexOf("await SendProjectOrganizationDocumentsMissingFromServerAsync(profile);", StringComparison.Ordinal);
        int early = collaboration.IndexOf("if (wanted.Length == 0)", StringComparison.Ordinal);
        Assert.True(send > 0 && send < early, "the scans go up before an organisation with none returns early");
        Assert.Contains("canManage: true)", collaboration, StringComparison.Ordinal);
        Assert.Contains("organizationDocumentUploadsRefused.Add(", collaboration, StringComparison.Ordinal);
    }

    [Fact]
    public void AScanReplacedUnderTheSameIdIsFetchedAgain()
    {
        string collaboration = ReadAppSource("ShellView.Collaboration.cs");

        Assert.Contains("if (File.Exists(fullPath) && local.IsAvailable && sameScan &&", collaboration, StringComparison.Ordinal);
        Assert.Contains("local.Sha256 = document.Sha256.Trim().ToUpperInvariant();", collaboration, StringComparison.Ordinal);
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
