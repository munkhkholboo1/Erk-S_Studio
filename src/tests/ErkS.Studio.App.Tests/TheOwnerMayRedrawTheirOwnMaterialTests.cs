using System.Text;
using System.Text.RegularExpressions;
using ErkS.Platform.Core;

namespace ErkS.Studio.Tests;

/// <summary>
/// A generated album page is redrawn by whoever holds the material it is drawn
/// from - and the pages drawn from project and company information stay with the
/// administrator.
///
/// 🔴 EVERY GENERATED PAGE WAS ADMINISTRATOR-ONLY, INCLUDING THE ONES MADE OF THE
/// PERSON'S OWN FILES. The owner of the licence is an architect, not an
/// administrator, so their own 26 renders could not be sent - the same wall as
/// the company's registration certificate. They drew the line themselves:
/// «Би төслийн мэдээлэл болон байгууллагын мэдээлэлд өөрчлөлт оруулах эрхгүй
/// байгаа. Тэр нь ч зөв. Гагцхүү би өөрийн эх үүсвэрээ бүрэн удирдах эрхтэй байх
/// ёстой.»
///
/// The half of this that must never loosen is the second half, so most of what
/// follows is the lock on it.
/// </summary>
public sealed class TheOwnerMayRedrawTheirOwnMaterialTests
{
    private const string Architect = "architect@erks.local";

    [Fact]
    public void ANArchitectHoldingTheirOwnRendersMayRedrawTheVisualizationPages()
    {
        // 🔴 THE OWNER'S EXACT CASE. Not an administrator, holding 26 renders of
        // their own, and the album's «Харагдах байдал» pages were drawn by an
        // older build.
        IReadOnlyList<string> codes = Select(
            Generated(ProjectCloudSyncMetadata.VisualizationsComponentCode),
            hasVisualizations: true,
            canManageCanonicalMetadata: false);

        Assert.Equal([ProjectCloudSyncMetadata.VisualizationsComponentCode], codes);
    }

    [Fact]
    public void THESameArchitectHoldingNOTHINGMayNot()
    {
        // The negative control that says where the authority came from. It is the
        // material, not the code: remove the renders and the same account, the
        // same page and the same project stop short.
        IReadOnlyList<string> codes = Select(
            Generated(ProjectCloudSyncMetadata.VisualizationsComponentCode),
            hasVisualizations: false,
            canManageCanonicalMetadata: false);

        Assert.Empty(codes);
    }

    [Fact]
    public void ANArchitectHoldingTheApprovedPlanningTaskMayRedrawItsPage()
    {
        // The document is one they uploaded; Studio only lays it out.
        IReadOnlyList<string> codes = Select(
            Generated(ProjectCloudSyncMetadata.ApprovedAtdComponentCode),
            hasOwnedAtd: true,
            canManageCanonicalMetadata: false);

        Assert.Equal([ProjectCloudSyncMetadata.ApprovedAtdComponentCode], codes);
    }

    [Fact]
    public void APageTheCloudHasFiledUnderSOMEBODYELSEIsNotTakenOver()
    {
        // Holding the same KIND of material is not holding THAT material. This is
        // the source branch's own owner test, applied here for the same reason:
        // a second contributor with renders of their own must not quietly replace
        // the first contributor's pages.
        ProjectCloudAlbumComponentReference filed =
            Generated(ProjectCloudSyncMetadata.VisualizationsComponentCode);
        filed.OwnerEmail = "somebody-else@erks.local";

        IReadOnlyList<string> codes = Select(
            filed,
            hasVisualizations: true,
            canManageCanonicalMetadata: false);

        Assert.Empty(codes);
    }

    [Fact]
    public void ANUNNAMEDPageIsLeftAloneOnceTheAlbumShowsThatMaterialUnderANOTHERName()
    {
        // 🔴 THE ONE THING THIS CHANGE WOULD OTHERWISE HAVE OPENED. A generated
        // code carries no owner, and the owner test lets anybody past an empty
        // name - so a second contributor holding renders of their own could
        // replace the first one's pages, which then leave the shared album when
        // the unnamed code is retired. Their files are safe and they can render
        // again, but they would not have been asked.
        ProjectCloudAlbumComponentReference theirs = Generated(
            ProjectCloudSyncMetadata.VisualizationsComponentCode);
        var somebodyElses = new ProjectCloudAlbumComponentReference
        {
            Code = "source:" + new string('a', 16) + ":visualizations",
            OwnerEmail = "somebody-else@erks.local",
            SourceKey = StudioAlbumComponentIdentity.VisualizationSourceKey,
            ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
        };

        Assert.Empty(
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                CloudProject(),
                [theirs, somebodyElses],
                Architect,
                hasOwnedAtd: false,
                hasVisualizations: true,
                canManageCanonicalMetadata: false));

        // The positive control, so that a rule refusing EVERYTHING cannot pass
        // for this one: the same album without that second name is redrawn.
        Assert.Equal(
            [ProjectCloudSyncMetadata.VisualizationsComponentCode],
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                CloudProject(),
                [theirs],
                Architect,
                hasOwnedAtd: false,
                hasVisualizations: true,
                canManageCanonicalMetadata: false));
    }

    [Fact]
    public void MYOWNSecondComponentIsNotSomebodyElse()
    {
        // The narrowing above must key on WHOSE name, not on there being one.
        // An account that already has an owner-keyed component of its own is the
        // commonest shape there is, and refusing it would have made the fix
        // useless in exactly the project it was written for.
        ProjectCloudAlbumComponentReference unnamed = Generated(
            ProjectCloudSyncMetadata.VisualizationsComponentCode);
        var mine = new ProjectCloudAlbumComponentReference
        {
            Code = "source:" + new string('b', 16) + ":visualizations",
            OwnerEmail = Architect,
            SourceKey = StudioAlbumComponentIdentity.VisualizationSourceKey,
            ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
        };

        Assert.Contains(
            ProjectCloudSyncMetadata.VisualizationsComponentCode,
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                CloudProject(),
                [unnamed, mine],
                Architect,
                hasOwnedAtd: false,
                hasVisualizations: true,
                canManageCanonicalMetadata: false));
    }

    [Fact]
    public void CANONICALPagesStayAdministratorOnlyWithEVERYMaterialInHand()
    {
        // 🔒 THE LOCK. This account holds every kind of personal material there
        // is - renders, the approved planning task, and control of the general
        // plan the location scheme is cut from - and still may not touch a page
        // made of project or company information. The owner asked for exactly
        // this line, in these words: no right over the project's or the
        // organisation's information.
        ProjectWorkspace project = ProjectWithGeneralPlanControlledBy(Architect);
        string[] canonical =
        [
            ProjectCloudSyncMetadata.CoverComponentCode,
            ProjectCloudSyncMetadata.CompanyRegistrationComponentCode,
            ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
            "generated:table-of-contents",
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix + "studio-building:b1",
        ];

        IReadOnlyList<string> refused =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                project,
                canonical.Select(Generated).ToList(),
                Architect,
                hasOwnedAtd: true,
                hasVisualizations: true,
                canManageCanonicalMetadata: false);

        Assert.Empty(refused);

        // The positive half, so that deleting the whole feature cannot satisfy
        // the assertion above: an administrator still redraws every one of them.
        IReadOnlyList<string> allowed =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                project,
                canonical.Select(Generated).ToList(),
                Architect,
                hasOwnedAtd: true,
                hasVisualizations: true,
                canManageCanonicalMetadata: true);

        Assert.Equal(canonical.Order(StringComparer.OrdinalIgnoreCase), allowed);
    }

    [Fact]
    public void ANEWGeneratedPageIsCanonicalUntilSomebodySaysWhatDrawsIt()
    {
        // The default has to fall this way. A page whose material nobody has
        // named is a page nobody has thought about, and the cheap mistake is
        // making its author ask for it - not handing it out.
        IReadOnlyList<string> codes = Select(
            Generated("generated:something-nobody-has-written-yet"),
            hasOwnedAtd: true,
            hasVisualizations: true,
            canManageCanonicalMetadata: false);

        Assert.Empty(codes);
    }

    [Fact]
    public void EVERYGeneratedPageTheProductCanEmitIsClassifiedONPURPOSE()
    {
        // 🔒 DERIVED FROM THE SOURCE, NOT FROM A LIST I TYPED. The renderer and
        // the sync metadata are read for every literal «generated:» code they can
        // put in an album, and each one is required to land on a side of the line
        // deliberately. Add a generated page and this test names it.
        IReadOnlyList<string> emitted = GeneratedCodesTheProductCanEmit();

        // The instrument first: a scan that found nothing, or lost the two codes
        // this whole change is about, proves nothing about the codes it did find.
        Assert.Contains(ProjectCloudSyncMetadata.CoverComponentCode, emitted);
        Assert.Contains(ProjectCloudSyncMetadata.VisualizationsComponentCode, emitted);
        Assert.True(emitted.Count >= 6, "the scan found only " + emitted.Count + " generated codes");

        ProjectWorkspace project = ProjectWithGeneralPlanControlledBy(Architect);
        IReadOnlyList<string> personal =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                project,
                emitted.Select(Generated).ToList(),
                Architect,
                hasOwnedAtd: true,
                hasVisualizations: true,
                canManageCanonicalMetadata: false);

        // The pages made of a person's own material - stated exactly, so that a
        // fourth one cannot arrive without somebody writing it down here.
        Assert.Equal(
            new[]
            {
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                ProjectCloudSyncMetadata.SiteContextComponentCode,
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
            }.Order(StringComparer.OrdinalIgnoreCase),
            personal.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void THELocationSchemeFollowsTheGeneralPlanItIsCutFrom()
    {
        // Not a flag on this call - the project answers it, through the same
        // policy that decides who may edit the scheme anywhere else in Studio.
        ProjectWorkspace theirs = ProjectWithGeneralPlanControlledBy(Architect);
        ProjectWorkspace somebodyElses =
            ProjectWithGeneralPlanControlledBy("somebody-else@erks.local");

        Assert.Equal(
            [ProjectCloudSyncMetadata.SiteContextComponentCode],
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                theirs,
                [Generated(ProjectCloudSyncMetadata.SiteContextComponentCode)],
                Architect,
                hasOwnedAtd: false,
                hasVisualizations: false,
                canManageCanonicalMetadata: false));

        Assert.Empty(
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                somebodyElses,
                [Generated(ProjectCloudSyncMetadata.SiteContextComponentCode)],
                Architect,
                hasOwnedAtd: false,
                hasVisualizations: false,
                canManageCanonicalMetadata: false));
    }

    [Fact]
    public void THEUploadPathAndTHEGateReadTheSAMEAnswer()
    {
        // 🔴 THE CORRESPONDENCE USED TO BE WRITTEN TWICE. One copy decided which
        // identity a redrawn page is filed under; the other decided whether the
        // page could be redrawn at all. Two copies of a rule about who may
        // overwrite whose pages is how one of them drifts - so the view now asks
        // the resolver instead of repeating it.
        string view = ReadSource("ErkS.Studio.App", "ShellView.AlbumComponents.cs");

        Assert.Contains(
            "StudioGeneratedComponentAuthority.Resolve(",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AlbumComponentIdentity.Source(ownerEmail, StudioAlbumComponentIdentity.AtdSourceKey)",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AlbumComponentIdentity.Source(ownerEmail, StudioAlbumComponentIdentity.VisualizationSourceKey)",
            view,
            StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Select(
        ProjectCloudAlbumComponentReference component,
        bool hasOwnedAtd = false,
        bool hasVisualizations = false,
        bool canManageCanonicalMetadata = false) =>
        StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
            CloudProject(),
            [component],
            Architect,
            hasOwnedAtd,
            hasVisualizations,
            canManageCanonicalMetadata);

    private static ProjectCloudAlbumComponentReference Generated(string code) => new()
    {
        Code = code,
        ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
    };

    /// <summary>
    /// Every literal «generated:» component code the renderer and the sync
    /// metadata can put in an album, read out of their source. Interpolated codes
    /// contribute the literal part before the hole, which is what the
    /// classification keys on anyway.
    /// </summary>
    private static IReadOnlyList<string> GeneratedCodesTheProductCanEmit()
    {
        string[] files =
        [
            ReadSource("ErkS.Platform.Core", "ProjectCloudSyncMetadata.cs"),
            ReadSource("ErkS.Platform.Pdf", "PdfSharpAlbumWriter.cs"),
        ];

        var found = new List<string>();
        foreach (string source in files)
        {
            foreach (Match match in Regex.Matches(source, "\"generated:[^\"{]*"))
            {
                string code = match.Value["\"".Length..].Trim();
                if (code.Length > "generated:".Length &&
                    !found.Contains(code, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(code);
                }
            }
        }

        return found;
    }

    private static ProjectWorkspace CloudProject()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("REN-002", "Own material");
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project";
        return project;
    }

    /// <summary>
    /// A cloud project whose location scheme is cut from a general plan the named
    /// account controls - the shape the site-context policy grants on.
    /// </summary>
    private static ProjectWorkspace ProjectWithGeneralPlanControlledBy(string email)
    {
        ProjectWorkspace project = CloudProject();
        var source = new ProjectDesignSource
        {
            Id = "general-plan-source",
            Kind = DesignSourceKind.CityGen,
            NativeDocumentPath = "general-plan.dwg",
        };
        project.Sources.Add(source);
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "general-plan");
        ProjectCloudSyncMetadata.BindCloudOwner(source, email);
        project.Cloud.SharedSources.Add(new ProjectCloudSourceReference
        {
            SourceId = "cloud-general-plan",
            SourceKey = "general-plan",
            RegisteredBy = email,
            CustodianEmail = email,
            OwnerEmail = email,
            Status = "Registered",
        });
        project.SiteContext.Boundary = new ProjectSiteBoundary
        {
            SourceId = "general-plan-source",
            AreaSquareMeters = 10_000,
            Ring =
            [
                new ProjectGeoCoordinate { Longitude = 106.90, Latitude = 47.90 },
                new ProjectGeoCoordinate { Longitude = 106.91, Latitude = 47.90 },
                new ProjectGeoCoordinate { Longitude = 106.91, Latitude = 47.91 },
                new ProjectGeoCoordinate { Longitude = 106.90, Latitude = 47.90 },
            ],
        };
        return project;
    }

    private static string ReadSource(string projectName, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", projectName, fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
