using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The site-context lock decides who may edit the location scheme and the
/// surroundings overview. It is held as a <c>sourceKey</c> on the album
/// component, and it is the album component - not the source list - that
/// survives a source being removed.
///
/// 🔴 MEASURED IN THE OWNER'S OWN PROJECT (2026-09-27). An AutoCAD test source
/// was registered, generated the site-context page, was merged into cloud album
/// R22, and then removed. What remains is a component whose
/// <c>sourceKey = 43e90b07…</c> names a source that is in NEITHER the local
/// source list NOR the cloud mirror. Nothing can match that key again.
///
/// The refusal the owner then sees says the general-plan source "is not
/// connected on this device", which describes a source that exists elsewhere
/// and could be connected. That is not this state, and the difference matters
/// because the two have different exits: connect the source, versus re-own the
/// component from a different source. One sentence for two states, and the
/// state with no exit is the one wearing the other's words.
/// </summary>
public sealed class ALOCKNamingADeletedSourceSaysSOTests
{
    private const string RetiredKey = "43e90b072500458baca754f673c2f66f";
    private const string OwnerEmail = "owner@example.com";

    [Fact]
    public void ALockWhoseSourceIsGoneFromBOTHListsSaysTheSourceIsGone()
    {
        ProjectWorkspace project = ProjectWithLock();

        ProjectSiteContextEditAuthority authority =
            ProjectSiteContextEditingPolicy.Resolve(project, OwnerEmail);

        Assert.False(authority.CanEdit);
        // Names the state that actually holds: the lock points at something the
        // project no longer has. "Not connected on this device" would send the
        // reader looking for a source to connect.
        Assert.Contains("хасагдсан", authority.Message, StringComparison.Ordinal);
        Assert.Equal(RetiredKey, authority.SourceKey);
    }

    [Fact]
    public void ALockWhoseSourceIsSTILLINTHECloudMirrorKeepsTheDeviceWording()
    {
        // The distinction this test defends: here the source does exist - a
        // colleague holds it, or it lives on the owner's other machine - so
        // connecting it IS the exit, and the older sentence is the right one.
        ProjectWorkspace project = ProjectWithLock();
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceId = "somewhere-else",
            SourceKey = RetiredKey,
            SourceOwnerKind = ProjectSourceOwnerKinds.Person,
            SourceOwnerRef = OwnerEmail,
            RegisteredBy = OwnerEmail,
        });

        ProjectSiteContextEditAuthority authority =
            ProjectSiteContextEditingPolicy.Resolve(project, OwnerEmail);

        Assert.False(authority.CanEdit);
        Assert.DoesNotContain("хасагдсан", authority.Message, StringComparison.Ordinal);
        Assert.Contains("төхөөрөмж", authority.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALockWhoseSourceIsPresentLocallyIsNotReportedAsGone()
    {
        // The lock is satisfiable: the source is right here. Whatever else
        // Resolve decides, it must not claim the source was removed.
        ProjectWorkspace project = ProjectWithLock();
        project.Sources.Add(new ProjectDesignSource
        {
            Id = RetiredKey,
            Kind = DesignSourceKind.AutoCad,
            Name = "Ерөнхий төлөвлөгөө",
        });

        ProjectSiteContextEditAuthority authority =
            ProjectSiteContextEditingPolicy.Resolve(project, OwnerEmail);

        Assert.DoesNotContain("хасагдсан", authority.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASourcePresentUnderAKindTHATCANNOTHoldTheSchemeIsStillNotGone()
    {
        // 🔴 SABOTAGE FOUND THIS GAP. The test above adds an AutoCAD source, so
        // ResolveLocalSource finds it and the removal branch is never reached -
        // deleting the local half of the check left every test green. The two
        // ask different questions: "is there a source of a kind that may hold
        // the scheme" versus "does this project still hold that source at all".
        //
        // Here the key belongs to a source of a kind that cannot hold the
        // location scheme, so the first answer is no while the second is yes.
        // Saying "removed from the project" about a source sitting in the list
        // would send the reader to re-own something they already have.
        ProjectWorkspace project = ProjectWithLock();
        project.Sources.Add(new ProjectDesignSource
        {
            Id = RetiredKey,
            Kind = DesignSourceKind.ErkSCad,
            Name = "Erk-S CAD диаграм",
        });

        ProjectSiteContextEditAuthority authority =
            ProjectSiteContextEditingPolicy.Resolve(project, OwnerEmail);

        Assert.False(authority.CanEdit);
        Assert.DoesNotContain("хасагдсан", authority.Message, StringComparison.Ordinal);
        Assert.False(
            ProjectSiteContextEditingPolicy.CanonicalSourceIsGoneFromProject(project));
    }

    [Fact]
    public void WithNoLockAtAllTheReaderIsToldToCLASSIFYASource()
    {
        // Positive control for the other end: an empty key is a project that has
        // never had a general plan, and its message must keep pointing at
        // classification rather than at a removal that never happened.
        var project = new ProjectWorkspace();

        ProjectSiteContextEditAuthority authority =
            ProjectSiteContextEditingPolicy.Resolve(project, OwnerEmail);

        Assert.False(authority.CanEdit);
        Assert.DoesNotContain("хасагдсан", authority.Message, StringComparison.Ordinal);
        Assert.Equal("", authority.SourceKey);
    }

    private static ProjectWorkspace ProjectWithLock()
    {
        var project = new ProjectWorkspace();
        project.Cloud.SharedAlbumComponents.Add(
            new ProjectCloudAlbumComponentReference
            {
                Code = ProjectCloudSyncMetadata.SiteContextComponentCode,
                Label = "БАЙРШЛЫН СХЕМ / ОРЧНЫ ТОЙМ",
                ComponentKind = ProjectSiteContextEditingPolicy.SiteContextComponentKind,
                SourceKey = RetiredKey,
                OwnerEmail = OwnerEmail,
            });
        return project;
    }
}
