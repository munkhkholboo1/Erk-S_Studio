using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Taking the server's current state must not throw away what this device has
/// not sent yet.
///
/// 🔴 WRITTEN BEFORE THE FIX IT GUARDS, AND DELIBERATELY SO. A sync that stops
/// on a conflict leaves the person pressing the button again against the same
/// stale base, so the client has to re-read the server before retrying. That
/// re-read overwrites the project mirror - and if it took the unsent work with
/// it, we would have cured an annoyance by introducing total loss.
///
/// So this is asserted by RUNNING the rebase over a project that holds work in
/// every bucket, not by reading the code and concluding it looks safe.
/// </summary>
public sealed class RebasingKeepsUnsentWorkTests
{
    [Fact]
    public void EVERYBucketOfUnsentWorkSurvivesARebase()
    {
        ProjectWorkspace project = ProjectHoldingUnsentWorkInEveryBucket();
        StudioPendingWork before = StudioPendingWork.Of(project);
        Assert.Equal(6, before.Total);

        ProjectCanonicalSyncService.Apply(project, ServerSnapshot());

        StudioPendingWork after = StudioPendingWork.Of(project);
        Assert.Equal(before.SourcePackages, after.SourcePackages);
        Assert.Equal(before.AlbumComponents, after.AlbumComponents);
        Assert.Equal(before.ProjectInformation, after.ProjectInformation);
        Assert.Equal(before.CompanyAssignment, after.CompanyAssignment);
        Assert.Equal(before.BuildingComposition, after.BuildingComposition);
        Assert.Equal(before.CanonicalTitleBlock, after.CanonicalTitleBlock);
    }

    [Fact]
    public void THETypedProjectInformationSurvivesWORDFORWORD()
    {
        // Counting buckets is not enough: a rebase that kept the record but
        // emptied its fields would pass the count and still lose the typing.
        // What the person wrote has to come back unchanged.
        ProjectWorkspace project = ProjectHoldingUnsentWorkInEveryBucket();

        ProjectCanonicalSyncService.Apply(project, ServerSnapshot());

        PendingProjectInformationUpdate? pending = project.Cloud.PendingProjectInformation;
        Assert.NotNull(pending);
        Assert.Equal("Эрин Апартмент", pending.Name);
        Assert.Equal("НАНУТО ХХК", pending.ClientName);
        Assert.Equal("Улаанбаатар, ХУД", pending.Location);
    }

    [Fact]
    public void ANEmptyPendingRecordIsTheONEThingARebaseMayDrop()
    {
        // 🔴 THE LIMIT, ASSERTED POSITIVELY. One discard exists and it is
        // deliberate: a pending record with every field blank carries nothing a
        // person typed, and keeping it would show «you have unsent changes»
        // forever over an empty envelope.
        //
        // Writing this down is what stops the discard from quietly widening -
        // the next field added to the record is covered by the test above, and
        // this one says exactly how narrow the exception is.
        var project = new ProjectWorkspace();
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "srv-1";
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };

        ProjectCanonicalSyncService.Apply(project, ServerSnapshot());

        Assert.Null(project.Cloud.PendingProjectInformation);
    }

    [Fact]
    public void APENDINGRecordWithONEFieldTypedIsNOTDropped()
    {
        // The boundary of that exception, from the other side. One typed field
        // is work, and the discard must not reach it.
        var project = new ProjectWorkspace();
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "srv-1";
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Location = "Улаанбаатар",
        };

        ProjectCanonicalSyncService.Apply(project, ServerSnapshot());

        Assert.NotNull(project.Cloud.PendingProjectInformation);
        Assert.Equal("Улаанбаатар", project.Cloud.PendingProjectInformation.Location);
    }

    private static ProjectWorkspace ProjectHoldingUnsentWorkInEveryBucket()
    {
        var project = new ProjectWorkspace
        {
            ProjectId = "PRJ-1",
        };
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "srv-1";

        var source = new ProjectDesignSource
        {
            Id = "src-1",
            Name = "Revit - Архитектур",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cloud.manifestId"] = Guid.NewGuid().ToString("D"),
                ["cloud.contentHash"] = new string('a', 64),
            },
        };
        project.Sources.Add(source);

        project.Cloud.PendingAlbumComponentCodes = ["generated:site-plan"];
        project.Cloud.BuildingCompositionPending = true;
        project.Cloud.CanonicalTitleBlockPending = true;
        project.Foundation.DesignCompany.AssignmentSource = "StudioCloudPending";
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Name = "Эрин Апартмент",
            ClientName = "НАНУТО ХХК",
            Location = "Улаанбаатар, ХУД",
        };
        return project;
    }

    private static ProjectServerSnapshot ServerSnapshot() => new()
    {
        ProjectId = "srv-1",
        ConcurrencyToken = "token-2",
    };
}
