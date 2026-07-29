using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioPdfPageEditCloudPolicyTests
{
    private const string Owner = "owner@example.com";
    private const string Collaborator = "collaborator@example.com";
    private const string Device = "device-a";
    private const string SourceKey = "local-pdf";

    [Fact]
    public void ExactLocalPdfEdit_QueuesOnlyOwnedComponentInMixedCloudAlbum()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource();
        project.Sources.Add(source);
        string localCode =
            StudioAlbumComponentIdentity.SourceCode(Owner, SourceKey);
        string collaboratorCode =
            StudioAlbumComponentIdentity.SourceCode(
                Collaborator,
                "collaborator-source");
        project.Cloud.SharedAlbumComponents =
        [
            Component(localCode, Owner, SourceKey, 1),
            Component(
                collaboratorCode,
                Collaborator,
                "collaborator-source",
                2),
        ];

        StudioPdfPageEditCloudDecision decision =
            StudioPdfPageEditCloudPolicy.Resolve(
                project,
                source,
                Owner,
                Device,
                hasVerifiedPayload: true);

        Assert.True(decision.Allowed);
        Assert.Equal(localCode, decision.ComponentCode);
        Assert.Equal(
            StudioWorkspaceOperation.LocalPdfPageEdit,
            decision.BuildOperation);

        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            decision.ComponentCode,
            Owner,
            Device,
            isRemoval: false);
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [collaboratorCode]);

        StudioCloudUnionPendingScope scope =
            StudioCloudUnionPreviewScope.Resolve(
                project,
                Owner,
                Device,
                _ => true);

        Assert.Equal([localCode], scope.ComponentCodes);
        Assert.Contains(
            localCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
        Assert.NotNull(ProjectCloudSyncMetadata.PendingAlbumComponentClaim(
            project,
            localCode,
            Owner,
            Device));
        Assert.Contains(
            project.Cloud.SharedAlbumComponents,
            component => component.Code == collaboratorCode);
    }

    [Fact]
    public void SameOwnerDifferentDevice_CannotQueuePdfEditComponent()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource();
        project.Sources.Add(source);

        StudioPdfPageEditCloudDecision decision =
            StudioPdfPageEditCloudPolicy.Resolve(
                project,
                source,
                Owner,
                "device-b",
                hasVerifiedPayload: true);

        Assert.False(decision.Allowed);
        Assert.Equal(
            "pdf_page_edit_source_not_local",
            decision.ReasonCode);
        Assert.Empty(ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    [Fact]
    public void ForeignCloudComponentWithoutSheetKeys_RequiresCanonicalPatchOrDefer()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = LocalSource();
        project.Sources.Add(source);
        project.PrimaryAlbum.LastPdfPath = "albums/existing-canonical.pdf";
        string localCode =
            StudioAlbumComponentIdentity.SourceCode(Owner, SourceKey);
        ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
            project,
            localCode,
            Owner,
            Device,
            isRemoval: false);
        project.Cloud.SharedAlbumComponents =
        [
            Component(localCode, Owner, SourceKey, 1),
            Component(
                StudioAlbumComponentIdentity.SourceCode(
                    Collaborator,
                    "collaborator-source"),
                Collaborator,
                "collaborator-source",
                2),
        ];

        StudioPdfPageEditAlbumRouteDecision route =
            StudioPdfPageEditCloudPolicy.ResolveAlbumRoute(
                project,
                Owner,
                Device,
                _ => true);

        Assert.Equal(
            StudioPdfPageEditAlbumRoute.CanonicalPatchOrDefer,
            route.Route);
        Assert.Equal(1, route.CloudOnlyComponentCount);
        Assert.Equal(
            "pdf_page_edit_cloud_components_require_canonical",
            route.ReasonCode);
        Assert.Equal(
            "albums/existing-canonical.pdf",
            project.PrimaryAlbum.LastPdfPath);
        Assert.Contains(
            localCode,
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    private static ProjectWorkspace CloudProject() => new()
    {
        ProjectId = "project-1",
        Deliverables =
        {
            Albums = [new ProjectAlbumRecord()],
        },
        Cloud =
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = "server-project-1",
            SharedSources =
            [
                new ProjectCloudSourceReference
                {
                    SourceId = "registry-local",
                    SourceKey = SourceKey,
                    Status = "Registered",
                    RegisteredBy = Owner,
                    OwnerEmail = Owner,
                    CustodianEmail = Owner,
                },
                new ProjectCloudSourceReference
                {
                    SourceId = "registry-collaborator",
                    SourceKey = "collaborator-source",
                    Status = "Registered",
                    RegisteredBy = Collaborator,
                    OwnerEmail = Collaborator,
                    CustodianEmail = Collaborator,
                },
            ],
        },
    };

    private static ProjectDesignSource LocalSource()
    {
        var source = new ProjectDesignSource
        {
            Id = "local-source",
            Kind = DesignSourceKind.Pdf,
        };
        var temporaryProject = new ProjectWorkspace { Sources = [source] };
        ProjectCloudSyncMetadata.BindToCloudSource(
            temporaryProject,
            source,
            SourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, Owner);
        StudioLocalSourceBindingPolicy.Bind(source, Owner, Device);
        return source;
    }

    private static ProjectCloudAlbumComponentReference Component(
        string code,
        string owner,
        string sourceKey,
        int pageNumber) => new()
    {
        Code = code,
        Label = sourceKey,
        Order = pageNumber,
        PageNumbers = [pageNumber],
        Status = "Available",
        OwnerEmail = owner,
        SourceKey = sourceKey,
        ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
    };
}
