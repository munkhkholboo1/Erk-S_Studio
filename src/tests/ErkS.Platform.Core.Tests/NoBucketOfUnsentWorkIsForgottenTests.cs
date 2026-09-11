using System.Reflection;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Unsent work lives in six buckets, and the two things that tell a person
/// whether they are up to date must read all six.
///
/// 🔴 THEY READ ONE. The cloud indicator counted album components and nothing
/// else, so a project with two changed sources waiting to go up painted GREEN
/// and said «✓ Шинэчлэлт алга»; the refresh report, reading the same single
/// bucket, said «Таны оруулга: илгээх зүйл байсангүй» in the same breath. Both
/// were honest about what they read. Neither read enough, and the owner spent a
/// morning believing their work had been delivered.
/// </summary>
public sealed class NoBucketOfUnsentWorkIsForgottenTests
{
    [Fact]
    public void THETotalCountsEVERYBucketTheTypeDeclares()
    {
        // 🔴 DERIVED FROM THE TYPE, NOT FROM A LIST I TYPED. Total is written out
        // by hand - six additions - and a seventh bucket added to the record
        // would compile perfectly while Total quietly ignored it. That is the
        // same defect one level up: a reader that looks at a subset.
        //
        // So the expected total is computed by reflection over the record's own
        // components: give every bucket a distinct value and the sum must match.
        PropertyInfo[] buckets = typeof(StudioPendingWork)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(int) &&
                !property.Name.Equals(nameof(StudioPendingWork.Total), StringComparison.Ordinal))
            .ToArray();

        Assert.True(buckets.Length >= 6, "only " + buckets.Length + " buckets were found");

        // 1, 2, 4, 8, … so that a missing bucket cannot be masked by another.
        object?[] values = buckets
            .Select((_, index) => (object?)(1 << index))
            .ToArray();
        var pending = (StudioPendingWork)Activator.CreateInstance(
            typeof(StudioPendingWork),
            values)!;

        int expected = values.Sum(value => (int)value!);
        Assert.Equal(expected, pending.Total);
    }

    [Fact]
    public void ANEmptyProjectHasNOTHINGWaiting()
    {
        // The negative control. «Everything is counted» is also satisfied by a
        // reader that always answers «something is waiting», and a permanently
        // yellow indicator is as useless as a permanently green one.
        var project = new ProjectWorkspace();

        StudioPendingWork pending = StudioPendingWork.Of(project);

        Assert.Equal(0, pending.Total);
        Assert.False(pending.Any);
    }

    [Fact]
    public void AWAITINGAlbumComponentIsCounted()
    {
        var project = new ProjectWorkspace();
        project.Cloud.PendingAlbumComponentCodes = ["generated:site-plan"];

        Assert.Equal(1, StudioPendingWork.Of(project).AlbumComponents);
        Assert.True(StudioPendingWork.Of(project).Any);
    }

    [Fact]
    public void AQUEUEDProjectInformationUpdateIsCounted()
    {
        // 🔴 A FLAG, NOT A LIST - and that is exactly the kind a counting reader
        // skips. Four of the six buckets are booleans.
        var project = new ProjectWorkspace();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            QueuedAtUtc = DateTimeOffset.UtcNow,
        };

        Assert.Equal(1, StudioPendingWork.Of(project).ProjectInformation);
        Assert.True(StudioPendingWork.Of(project).Any);
    }

    [Fact]
    public void PENDINGCompositionAndTitleBlockAreCounted()
    {
        var project = new ProjectWorkspace();
        project.Cloud.BuildingCompositionPending = true;
        project.Cloud.CanonicalTitleBlockPending = true;

        StudioPendingWork pending = StudioPendingWork.Of(project);

        Assert.Equal(1, pending.BuildingComposition);
        Assert.Equal(1, pending.CanonicalTitleBlock);
        Assert.Equal(2, pending.Total);
    }

    [Fact]
    public void ALOCALLYMadeCompanyAssignmentIsCounted()
    {
        var project = new ProjectWorkspace();
        project.Foundation.DesignCompany.AssignmentSource = "StudioCloudPending";

        Assert.Equal(1, StudioPendingWork.Of(project).CompanyAssignment);
    }

    [Fact]
    public void READINGTheBucketsCostsNOFileAccess()
    {
        // 🔴 THE INDICATOR REPAINTS CONSTANTLY AND MUST STAY CHEAP. The question
        // «which buckets hold something» is answered from records and flags; the
        // far more expensive question «could this device actually send each
        // piece» hashes files, and asking THAT from the indicator is what froze
        // the window for minutes at a time.
        //
        // Asserted by pointing every path at a directory that does not exist: a
        // reader that touched the disk would throw or answer differently.
        var project = new ProjectWorkspace
        {
            ProjectId = "PRJ-1",
        };
        project.Cloud.PendingAlbumComponentCodes = ["generated:site-plan"];
        project.Sources.Add(new ProjectDesignSource
        {
            Id = "src-1",
            Name = "Revit",
            InboxFolder = Path.Combine("Z:", "does", "not", "exist"),
        });

        StudioPendingWork pending = StudioPendingWork.Of(project);

        Assert.Equal(1, pending.AlbumComponents);
        Assert.Equal(0, pending.SourcePackages);
    }
}
