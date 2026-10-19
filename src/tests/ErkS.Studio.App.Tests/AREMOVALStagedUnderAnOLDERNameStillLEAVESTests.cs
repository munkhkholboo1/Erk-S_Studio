using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A source removal staged on this machine must still be sent from this
/// machine, whatever form of its name it signs with today.
///
/// 🔴 ERIN'S CLOUD SYNC STOOD AT «Pending» FROM 2026-08-20, AND SAID NOTHING.
/// Two sources were removed that day; each removal is a claim carrying the
/// device fingerprint it was made under - f3f4aa2a…, this very machine's
/// older-salt trait form (measured on the owner's copy of the project). Since
/// then the machine adopted a registered device key, so it answers with the key
/// form, and the outbox matched claims by exact equality. Both removals became
/// «Локал source олдоогүй» - blocked on every sync, never sent, never cleared.
///
/// The source binding learned this lesson already (AMachineRecognisesItsOwnBinding):
/// accept every form THIS device can present. Widening acceptance, never
/// ownership - another machine's claim stays invisible.
/// </summary>
[Collection(StudioDeviceIdentityCollection.Name)]
public sealed class AREMOVALStagedUnderAnOLDERNameStillLEAVESTests : IDisposable
{
    private const string Owner = "owner@example.com";
    private const string SourceKey = "retiring-source";
    private const string RegistryId = "registry-retiring";

    public AREMOVALStagedUnderAnOLDERNameStillLEAVESTests() =>
        StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();

    public void Dispose() => StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();

    [Fact]
    public void ARemovalStagedUnderTheOLDSALTFormIsStillPendingAfterAKeyIsAdopted()
    {
        ProjectWorkspace project = StagedUnder(StudioDeviceIdentity.TraitBasedFingerprints.Legacy);

        StudioDeviceIdentity.UseRegisteredKeyFingerprint(new string('F', 64));

        Assert.Single(StudioSourceRemovalOutbox.Pending(project, Owner, StudioDeviceIdentity.Fingerprint));
        Assert.True(StudioSourceRemovalOutbox.IsStaged(
            project, Component, Owner, StudioDeviceIdentity.Fingerprint));
    }

    [Fact]
    public void ANOTHERMachinesRemovalIsStillINVISIBLE()
    {
        // The limit: a claim naming a device this machine is not, in any form.
        ProjectWorkspace project = StagedUnder("another-machine");

        StudioDeviceIdentity.UseRegisteredKeyFingerprint(new string('F', 64));

        Assert.Empty(StudioSourceRemovalOutbox.Pending(project, Owner, StudioDeviceIdentity.Fingerprint));
        Assert.False(StudioSourceRemovalOutbox.IsStaged(
            project, Component, Owner, StudioDeviceIdentity.Fingerprint));
    }

    [Fact]
    public void ACallerThatIsNOTThisDeviceGainsNothing()
    {
        // Widening applies only when the CALLER is this machine: someone passing
        // a foreign fingerprint does not get this machine's claims.
        ProjectWorkspace project = StagedUnder(StudioDeviceIdentity.TraitBasedFingerprints.Legacy);

        Assert.Empty(StudioSourceRemovalOutbox.Pending(project, Owner, "another-machine"));
    }

    [Fact]
    public void TheSyncPlanSENDSItAndAcknowledgesItsClaim()
    {
        ProjectWorkspace project = StagedUnder(StudioDeviceIdentity.TraitBasedFingerprints.Legacy);
        StudioDeviceIdentity.UseRegisteredKeyFingerprint(new string('F', 64));

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "this machine",
            new StudioCloudProjectRefreshResult(false, null),
            StudioDeviceIdentity.Fingerprint,
            _ => true);

        Assert.True(plan.IsComponentAuthorized(Component));
        Assert.DoesNotContain(plan.Blocked, item => item.Code == Component);
        Assert.Single(plan.ComponentClaimAcknowledgements([Component]));
    }

    [Fact]
    public void WhatStaysBehindIsWRITTENDownWithItsReason()
    {
        // A claim from another machine stays blocked - and the reason must
        // outlive the session instead of vanishing with a status line.
        ProjectWorkspace project = StagedUnder("another-machine");
        StudioDeviceIdentity.UseRegisteredKeyFingerprint(new string('F', 64));

        CloudSyncPreviewPlan plan = CloudSyncPreviewPlanner.Build(
            project,
            Owner,
            "this machine",
            new StudioCloudProjectRefreshResult(false, null),
            StudioDeviceIdentity.Fingerprint,
            _ => true);

        string summary = plan.BlockedSummary();
        Assert.StartsWith("Илгээгдээгүй 1:", summary, StringComparison.Ordinal);
        Assert.Contains(Component, summary, StringComparison.Ordinal);
        Assert.Contains(Assert.Single(plan.Blocked).Detail, summary, StringComparison.Ordinal);

        // Written both when nothing could be sent and after a partial send.
        string shell = File.ReadAllText(ShellSource());
        Assert.Equal(3, shell.Split("RecordBlockedSyncNote(plan);").Length);
        Assert.Contains("state.Project.Cloud.LastSyncNote = blocked;", shell, StringComparison.Ordinal);
    }

    private static string ShellSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", "ShellView.cs");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        Assert.Fail("ShellView.cs was not found; this test reads it from source");
        return "";
    }

    private static string Component => StudioAlbumComponentIdentity.SourceCode(Owner, SourceKey);

    /// <summary>A removal staged by this owner under <paramref name="device"/>, the local source already gone.</summary>
    private static ProjectWorkspace StagedUnder(string device)
    {
        var project = new ProjectWorkspace
        {
            Cloud =
            {
                Origin = ProjectOrigins.Cloud,
                ServerProjectId = "project-1",
                SharedSources = [],
            },
        };
        var source = new ProjectDesignSource { Id = "local-retiring", Kind = DesignSourceKind.Revit };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, SourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, Owner);
        StudioLocalSourceBindingPolicy.Bind(source, Owner, device);
        project.Sources.Add(source);
        var registry = new ProjectCloudSourceReference
        {
            SourceId = RegistryId,
            SourceKey = SourceKey,
            RegisteredBy = Owner,
            OwnerEmail = Owner,
            Status = "Active",
        };
        project.Cloud.SharedSources.Add(registry);

        StudioSourceRemovalOutbox.Stage(project, source, registry, Owner, device, hasVerifiedPayload: true);
        project.Sources.Remove(source);
        return project;
    }
}
