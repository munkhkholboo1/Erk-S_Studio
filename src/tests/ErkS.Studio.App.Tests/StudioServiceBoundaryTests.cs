using ErkS.Studio;
using System.Reflection;

namespace ErkS.Studio.App.Tests;

public sealed class StudioServiceBoundaryTests
{
    [Fact]
    public void AccountFacade_ExposesNarrowCloudClientBoundaries()
    {
        Type facade = typeof(StudioAccountService);

        Assert.True(typeof(IStudioSessionClient).IsAssignableFrom(facade));
        Assert.True(typeof(ILicenseClient).IsAssignableFrom(facade));
        Assert.True(typeof(IProjectsClient).IsAssignableFrom(facade));
        Assert.True(typeof(IOrganizationsClient).IsAssignableFrom(facade));
        Assert.NotNull(typeof(IOrganizationsClient).GetMethod(nameof(IOrganizationsClient.DeleteOrganizationAsync)));
        Assert.NotNull(typeof(IOrganizationsClient).GetMethod(nameof(IOrganizationsClient.BeginOrganizationRegistryImportAsync)));
        Assert.NotNull(typeof(IOrganizationsClient).GetMethod(nameof(IOrganizationsClient.GetOrganizationRegistryImportAsync)));
        Assert.True(typeof(ICollaborationClient).IsAssignableFrom(facade));
        Assert.True(typeof(ISourcePackagesClient).IsAssignableFrom(facade));
        Assert.True(typeof(IAlbumsClient).IsAssignableFrom(facade));
        Assert.NotNull(typeof(IAlbumsClient).GetMethod(nameof(IAlbumsClient.DownloadAlbumRevisionPdfAsync)));
        Assert.True(typeof(IProfileImageClient).IsAssignableFrom(facade));
    }

    [Fact]
    public void UpdateService_ExposesUpdateClientBoundary()
    {
        Assert.True(typeof(IUpdatesClient).IsAssignableFrom(typeof(StudioUpdateService)));
    }

    [Fact]
    public void AccountFacade_DependsOnCredentialStoreBoundary()
    {
        Assert.True(typeof(ICredentialStore).IsAssignableFrom(typeof(WindowsCredentialStore)));
        Assert.Contains(
            typeof(StudioAccountService).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic),
            constructor => constructor.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(ICredentialStore));
    }

    [Fact]
    public void AccountFacade_DependsOnGeneratedCloudEraContractBoundary()
    {
        Assert.Contains(
            typeof(StudioAccountService).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic),
            constructor => constructor.GetParameters() is
            [
                { ParameterType: var credentialType },
                { ParameterType: var cloudContractType },
            ] &&
            credentialType == typeof(ICredentialStore) &&
            cloudContractType == typeof(ICloudEraContractClient));
    }

    [Fact]
    public void CollaborationBoundary_ExposesParticipantRoleUpdates()
    {
        Assert.NotNull(typeof(ICollaborationClient).GetMethod(
            nameof(ICollaborationClient.UpdateParticipantRolesAsync)));
        Assert.NotNull(typeof(ICloudEraContractClient).GetMethod(
            nameof(ICloudEraContractClient.UpdateParticipantRolesAsync)));
        Assert.NotNull(typeof(ICloudEraContractClient).GetMethod(
            nameof(ICloudEraContractClient.AssignConceptArchitectAsync)));
    }

    [Fact]
    public async Task DownloadedFile_IsClosedBeforeAtomicReplacement()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "erk-s-studio-download-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(folder, "album.pdf");
        byte[] expected = [1, 2, 3, 4, 5];
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(destination, [9, 9]);

        try
        {
            await using var source = new MemoryStream(expected, writable: false);

            await StudioAccountService.ReplaceDownloadedFileAsync(source, destination);

            Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".download"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
