using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A package can be verified and still not belong here. Nothing quarantines
/// such a package, so the reason given at this moment is the only account of
/// it that will ever exist - without one, a refused delivery is
/// indistinguishable from one that never arrived.
/// </summary>
public sealed class PackageAdmissionReasonTests
{
    [Fact]
    public void APackageForAnotherProject_SaysSo()
    {
        PackageAdmission admission = Admit(
            project: ProjectWith("project-a"),
            manifestProjectId: "project-b");

        Assert.False(admission.IsAdmitted);
        Assert.Contains("төслийнх", admission.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void APackageWhoseSourceIsNotRegistered_SaysSo()
    {
        PackageAdmission admission = Admit(
            project: ProjectWith("project-a"),
            manifestProjectId: "project-a",
            manifestSourceId: "a-source-nobody-registered");

        Assert.False(admission.IsAdmitted);
        Assert.Contains("бүртгэлгүй", admission.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void APackageWithNoSourceId_SaysSo()
    {
        PackageAdmission admission = Admit(
            project: ProjectWith("project-a"),
            manifestProjectId: "project-a",
            manifestSourceId: "");

        Assert.False(admission.IsAdmitted);
        Assert.Contains("дугаар алга", admission.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRefusalSaysSomething()
    {
        // A blank reason would put the user back where this started: a delivery
        // that vanished with no account of itself.
        foreach (string sourceId in new[] { "", "unregistered" })
        {
            PackageAdmission admission = Admit(
                ProjectWith("project-a"),
                "project-a",
                sourceId);
            Assert.False(admission.IsAdmitted);
            Assert.False(string.IsNullOrWhiteSpace(admission.Refusal));
        }
    }

    private static ProjectWorkspace ProjectWith(string projectId) => new()
    {
        ProjectId = projectId,
        Sources = [new ProjectDesignSource { Id = "registered-source", Name = "AutoCAD" }],
    };

    private static PackageAdmission Admit(
        ProjectWorkspace project,
        string manifestProjectId,
        string manifestSourceId = "registered-source")
    {
        var manifest = new SheetPackageManifest
        {
            ProjectId = manifestProjectId,
            Source = new SheetPackageSource { SourceId = manifestSourceId },
        };
        var result = new SheetPackageLoadResult
        {
            ManifestPath = @"C:\inbox\x.erks-sheets.json",
            Manifest = manifest,
        };
        return StudioRuntimeSourceScope.Admit(project, result, "a@b.mn", "device", _ => true);
    }
}
