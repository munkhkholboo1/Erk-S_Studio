using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Studio;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The server's shared building composition put Batnasan's ЕТ sheets back under
/// «Барилга 1» after the package had taken them out (2026-10-20). In the
/// working drawings a general-plan sheet is never a building's - neither the
/// canonical composition nor the shared mirror can file it there.
/// </summary>
public sealed class THESHAREDCompositionCannotFileAGENERALPlanUnderABuildingTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-gp-composition-" + Guid.NewGuid().ToString("N"));

    public THESHAREDCompositionCannotFileAGENERALPlanUnderABuildingTests() =>
        Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void TheCanonicalCompositionLeavesAnETSheetUnfiled()
    {
        (ProjectWorkspace project, SheetLibrary library, SheetRecord sheet) = Batnasan("ЕТ", workingDrawings: true);

        StudioBuildingCompositionSync.ApplyCanonical(project, library, Canonical(), preserveLocalEdits: false);

        Assert.False(project.SheetBuildingAssignments.ContainsKey(sheet.Key));
    }

    [Fact]
    public void TheSharedMirrorTakesAnETSheetBackOut()
    {
        (ProjectWorkspace project, SheetLibrary library, SheetRecord sheet) = Batnasan("ЕТ", workingDrawings: true);
        StudioBuildingCompositionSync.ApplyCanonical(project, library, Canonical(), preserveLocalEdits: true);
        project.SheetBuildingAssignments[sheet.Key] = "building-1";

        bool changed = StudioBuildingCompositionSync.MaterializeSharedAssignments(project, library);

        Assert.True(changed);
        Assert.False(project.SheetBuildingAssignments.ContainsKey(sheet.Key));
    }

    [Fact]
    public void AnArchitectureSheetIsStillFiled()
    {
        // The positive control: only the general plan is kept out.
        (ProjectWorkspace project, SheetLibrary library, SheetRecord sheet) = Batnasan("БА", workingDrawings: true);

        StudioBuildingCompositionSync.ApplyCanonical(project, library, Canonical(), preserveLocalEdits: false);

        Assert.Equal("building-1", project.SheetBuildingAssignments[sheet.Key]);
    }

    [Fact]
    public void OutsideTheWorkingDrawingsTheRuleDoesNotApply()
    {
        (ProjectWorkspace project, SheetLibrary library, SheetRecord sheet) = Batnasan("ЕТ", workingDrawings: false);

        StudioBuildingCompositionSync.ApplyCanonical(project, library, Canonical(), preserveLocalEdits: false);

        Assert.Equal("building-1", project.SheetBuildingAssignments[sheet.Key]);
    }

    [Fact]
    public void ALeftoverLocalAssignmentIsNeverSentToTheServer()
    {
        (ProjectWorkspace project, SheetLibrary library, SheetRecord sheet) = Batnasan("ЕТ", workingDrawings: true);
        project.BuildingGroups = [new ProjectBuildingGroup { Id = "building-1", Name = "Барилга 1", Order = 1 }];
        project.SheetBuildingAssignments[sheet.Key] = "building-1";

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(project, library);

        Assert.Empty(update.SheetAssignments);
    }

    [Fact]
    public void ALocalArchitectureAssignmentIsStillSent()
    {
        (ProjectWorkspace project, SheetLibrary library, SheetRecord sheet) = Batnasan("БА", workingDrawings: true);
        project.BuildingGroups = [new ProjectBuildingGroup { Id = "building-1", Name = "Барилга 1", Order = 1 }];
        project.SheetBuildingAssignments[sheet.Key] = "building-1";

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(project, library);

        Assert.Equal("building-1", Assert.Single(update.SheetAssignments).BuildingGroupId);
    }

    private static StudioCloudBuildingComposition Canonical() => new()
    {
        Version = 3,
        Groups = [new StudioCloudBuildingGroup { Id = "building-1", Name = "Барилга 1", Order = 1 }],
        SheetAssignments =
        [
            new StudioCloudBuildingSheetAssignment
            {
                SourceOwnerEmail = "owner@erks.local",
                SourceKey = "bn-et",
                SheetId = "et-01",
                BuildingGroupId = "building-1",
            },
        ],
    };

    private (ProjectWorkspace, SheetLibrary, SheetRecord) Batnasan(string mark, bool workingDrawings)
    {
        string folder = Path.Combine(workDirectory, mark + workingDrawings);
        Directory.CreateDirectory(folder);
        string pdfPath = Path.Combine(folder, "et-01.pdf");
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420);
            page.Height = XUnit.FromMillimeter(297);
            document.Save(pdfPath);
        }

        var source = new ProjectDesignSource
        {
            Id = "bn-autocad",
            Kind = DesignSourceKind.AutoCad,
            InboxFolder = folder,
        };
        var project = new ProjectWorkspace { ProjectId = "bn", Sources = [source] };
        if (workingDrawings)
        {
            project.Identity.ProjectType = ErkS.Platform.Core.ProjectTypes.BuildingDesignProjectType.TypeId;
            project.Identity.StageCode = "working-drawings";
        }
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "bn-et");
        ProjectCloudSyncMetadata.BindCloudOwner(source, "owner@erks.local");

        var manifest = new SheetPackageManifest
        {
            PackageScope = SheetPackageScope.FullSnapshot,
            Source = new SheetPackageSource { SourceId = "bn-autocad", Application = SheetSourceApplication.AutoCad },
            Sheets =
            {
                new SheetPackageEntry
                {
                    SheetId = "et-01",
                    Number = "01",
                    Name = "ЕРӨНХИЙ ТӨЛӨВЛӨГӨӨ",
                    Discipline = mark,
                    WidthMm = 420,
                    HeightMm = 297,
                    ContentWidthMm = 420,
                    ContentHeightMm = 297,
                    PdfFileName = "et-01.pdf",
                    PageCount = 1,
                },
            },
        };
        string manifestPath = SheetPackageWriter.Write(manifest, folder, "bn");
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(manifestPath), source.Id);
        return (project, library, Assert.Single(library.Snapshot()));
    }
}
