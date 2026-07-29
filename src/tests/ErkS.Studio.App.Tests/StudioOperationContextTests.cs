using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioOperationContextTests
{
    [Fact]
    public void ContextRejectsProjectOrAccountSwitch()
    {
        ProjectWorkspace project = Project("p-1", "server-p");
        StudioAccountSession accountA = Account("a@example.com");
        StudioOperationContext context = StudioOperationContext.Capture(
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            accountA,
            workspaceEpoch: 1,
            accountEpoch: 1);

        Assert.True(context.Matches(
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            accountA,
            1,
            1));
        Assert.False(context.Matches(
            true,
            Project("q-1", "server-q"),
            @"C:\projects\q\project.erkstudio",
            accountA,
            2,
            1));
        Assert.False(context.Matches(
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            Account("b@example.com"),
            1,
            2));
        Assert.False(context.Matches(
            false,
            null,
            null,
            accountA,
            2,
            1));
        Assert.False(context.Matches(
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            accountA,
            workspaceEpoch: 2,
            accountEpoch: 1));
        Assert.False(context.Matches(
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            accountA,
            workspaceEpoch: 1,
            accountEpoch: 2));
    }

    [Fact]
    public void AccountOnlyContextRejectsSignOutAndSameAccountRelogin()
    {
        StudioAccountSession account = Account("a@example.com");
        StudioOperationContext context = StudioOperationContext.Capture(
            false,
            null,
            null,
            account,
            workspaceEpoch: 7,
            accountEpoch: 11);

        Assert.True(context.Matches(
            false,
            null,
            null,
            account,
            workspaceEpoch: 7,
            accountEpoch: 11));
        Assert.False(context.Matches(
            false,
            null,
            null,
            null,
            workspaceEpoch: 7,
            accountEpoch: 12));
        Assert.False(context.Matches(
            false,
            null,
            null,
            Account("a@example.com"),
            workspaceEpoch: 7,
            accountEpoch: 13));
    }

    [Fact]
    public void ProjectContextRejectsDifferentLocalMirrorOfSameCloudProject()
    {
        ProjectWorkspace project = Project("local-p", "server-p");
        StudioAccountSession account = Account("a@example.com");
        StudioOperationContext context = StudioOperationContext.Capture(
            true,
            project,
            @"C:\projects\first\project.erkstudio",
            account,
            workspaceEpoch: 3,
            accountEpoch: 5);

        Assert.False(context.Matches(
            true,
            Project("local-p", "server-p"),
            @"C:\projects\second\project.erkstudio",
            account,
            workspaceEpoch: 4,
            accountEpoch: 5));
    }

    [Fact]
    public void SourceBindingContinuationRejectsStaleSessionAndDetachedSource()
    {
        ProjectWorkspace project = Project("local-p", "server-p");
        var source = new ProjectDesignSource { Id = "source-1" };
        project.Sources.Add(source);
        StudioAccountSession account = Account("owner@example.com");
        StudioOperationContext context = StudioOperationContext.Capture(
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            account,
            workspaceEpoch: 3,
            accountEpoch: 7);

        Assert.True(StudioCloudSourceBindingContinuationPolicy.CanApply(
            context,
            source,
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            account,
            workspaceEpoch: 3,
            accountEpoch: 7));
        Assert.False(StudioCloudSourceBindingContinuationPolicy.CanApply(
            context,
            source,
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            Account("other@example.com"),
            workspaceEpoch: 3,
            accountEpoch: 8));
        Assert.False(StudioCloudSourceBindingContinuationPolicy.CanApply(
            context,
            source,
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            Account("owner@example.com"),
            workspaceEpoch: 3,
            accountEpoch: 8));

        project.Sources.Clear();
        project.Sources.Add(new ProjectDesignSource { Id = source.Id });

        Assert.False(StudioCloudSourceBindingContinuationPolicy.CanApply(
            context,
            source,
            true,
            project,
            @"C:\projects\p\project.erkstudio",
            account,
            workspaceEpoch: 3,
            accountEpoch: 7));
    }

    [Fact]
    public void LifecycleBlocksMutatingOperationsButNotIdleOrRegistryPolling()
    {
        Assert.True(StudioWorkspaceLifecyclePolicy.Evaluate(
            new StudioWorkspaceLifecycleActivity(
                false, false, false, false, false, false,
                false, false, false, false)).Allowed);
        Assert.False(StudioWorkspaceLifecyclePolicy.Evaluate(
            new StudioWorkspaceLifecycleActivity(
                false, false, true, false, false, false,
                false, false, false, false)).Allowed);
        Assert.False(StudioWorkspaceLifecyclePolicy.Evaluate(
            new StudioWorkspaceLifecycleActivity(
                false, false, false, false, false, true,
                false, false, false, false)).Allowed);
    }

    private static ProjectWorkspace Project(
        string projectId,
        string serverProjectId) => new()
    {
        ProjectId = projectId,
        Cloud =
        {
            Origin = ProjectOrigins.Cloud,
            ServerProjectId = serverProjectId,
        },
    };

    private static StudioAccountSession Account(string email) => new(
        "https://cloud.example",
        email,
        email,
        "",
        "",
        "",
        "Dev",
        DateTimeOffset.UtcNow.AddDays(1),
        DateTimeOffset.UtcNow.AddHours(1),
        "token");
}
