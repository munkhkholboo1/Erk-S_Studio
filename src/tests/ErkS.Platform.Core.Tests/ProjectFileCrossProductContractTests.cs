using System.Text;
using System.Text.Json;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The part of project.erksproject that AutoCAD and Revit read for themselves.
/// </summary>
/// <remarks>
/// Studio writes this file; PFA and PFR open it with their own JsonElement
/// readers and pick a handful of fields out of it by name. Neither of them
/// deserialises into Studio's types, so nothing in a build tells us when a
/// rename lands - the field simply stops being found and the binding silently
/// resolves to nothing.
///
/// That is the failure this file exists to make loud. It asserts the exact
/// property names and JSON kinds those two readers depend on, taken from their
/// source rather than from what Studio happens to emit:
///
///   PFA - AutoCAD_v2/src/sheet-packages/StudioSourceBindingResolver.cs
///   PFR - ErkS.Revit.TitleBlocks/StudioSourceBindingCatalog.cs
///
/// A field listed here is a cross-product contract term. Renaming one is a
/// three-product change, and this test is where that is said out loud.
/// </remarks>
public sealed class ProjectFileCrossProductContractTests
{
    /// <summary>
    /// The metadata entry both products look inside a source for. PFR named it
    /// when it sent its reader's real behaviour; the first draft of this file
    /// guessed "buildingGroupId" and was wrong.
    /// </summary>
    private const string BuildingGroupMetadataKey = "source.buildingGroupId";

    [Fact]
    public void EveryFieldTheOtherTwoProductsReadIsStillWritten()
    {
        JsonElement root = SaveAndReadBack(BuildRepresentativeProject());

        // Both readers start here and give up if either is missing.
        Assert.Equal(JsonValueKind.String, Kind(root, "projectId"));
        Assert.Equal(JsonValueKind.Array, Kind(root, "sources"));

        // PFR reads both; PFA reads only the code.
        JsonElement identity = root.GetProperty("identity");
        Assert.Equal(JsonValueKind.String, Kind(identity, "code"));
        Assert.Equal(JsonValueKind.String, Kind(identity, "name"));

        JsonElement source = root.GetProperty("sources")[0];
        foreach (string field in new[]
                 {
                     "id",                    // both
                     "kind",                  // both, matched against the enum name
                     "nativeDocumentPath",    // both, matched against the open document
                     "inboxFolder",           // both
                     "name",                  // PFR
                     "nativeDocumentTitle",   // PFR, its fallback for an unnamed source
                 })
        {
            Assert.Equal(JsonValueKind.String, Kind(source, field));
        }

        Assert.Equal(JsonValueKind.Object, Kind(source, "metadata"));

        // Guid? in Studio, read as a string by both. A set value must therefore
        // serialise as a string and not as a number or an object.
        Assert.Equal(JsonValueKind.String, Kind(source, "stageId"));
        Assert.Equal(JsonValueKind.String, Kind(source, "workPackageId"));

        JsonElement group = root.GetProperty("buildingGroups")[0];
        Assert.Equal(JsonValueKind.String, Kind(group, "id"));
        Assert.Equal(JsonValueKind.String, Kind(group, "name"));

        Assert.Equal(JsonValueKind.Object, Kind(root, "sheetBuildingAssignments"));

        // PFA only, for the title block's corner table.
        Assert.Equal(JsonValueKind.String, Kind(root.GetProperty("albumStyle"), "cornerTable"));
    }

    [Fact]
    public void TheKindNamesTheOtherProductsMatchOnAreTheEnumNames()
    {
        // PFR compares kind against the literal "Revit" and PFA against
        // "AutoCad" with separators stripped. Both are the C# member names, so
        // renaming a member renames a wire value in two other products.
        Assert.Equal("Revit", DesignSourceKind.Revit.ToString());
        Assert.Equal("AutoCad", DesignSourceKind.AutoCad.ToString());
    }

    [Fact]
    public void TheTwoKeysInsideTheFileAreShapedTheWayTheOtherProductsParseThem()
    {
        // Two of the file's values are themselves formats rather than plain
        // strings, and both were guessed wrong when this file was first
        // written. PFR reported its reader's real behaviour and corrected them.
        //
        // Pinned by asking Studio to produce them rather than by repeating a
        // literal, so this follows the code if the shape ever moves - and
        // fails, which is the point.
        var source = new ProjectDesignSource { Id = "revit-source-001" };
        ProjectDesignSourceClassification.SetExplicitPurpose(
            source,
            ProjectDesignSourcePurpose.Building,
            "group-a");

        Assert.True(
            source.Metadata.ContainsKey(BuildingGroupMetadataKey),
            $"Both products read a source's building through metadata['{BuildingGroupMetadataKey}']. "
            + "Renaming that key is a three-product change.");

        // The sheet-assignment key is the source's identity and the sheet id
        // joined, lower-cased. PFR matches it by the "{sourceId}|" prefix - a
        // sheet number on its own would match nothing.
        string key = SheetRecord.MakeKey(
            new SheetPackageSource
            {
                Application = SheetSourceApplication.Revit,
                DocumentPath = @"C:\p\BuildingA.rvt",
            },
            new SheetPackageEntry { SheetId = "A-01" },
            "revit-source-001");

        Assert.Equal("revit-source-001|a-01", key);
    }

    [Fact]
    public void TheFileSaysWhatVersionItIs()
    {
        // The gate the other two products do not yet have. It can only be added
        // over there if the value is actually in the file, so this holds the
        // near end of that contract.
        JsonElement root = SaveAndReadBack(BuildRepresentativeProject());

        Assert.Equal(JsonValueKind.Number, Kind(root, "formatVersion"));
        Assert.Equal(
            ProjectWorkspace.CurrentFormatVersion,
            root.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void RaisingTheProjectFormatVersionIsAThreeProductChange()
    {
        // The anchor PFR asked for, and they were right to: Studio promised to
        // tell PFA and PFR before raising this, and a promise is not a
        // mechanism. The change happens in Studio's code, so the reminder
        // belongs in Studio's tests.
        //
        // Three constants in this codebase carry the same name and different
        // numbers, which is how PFR nearly built its gate on the wrong one.
        // Pinned together so the next reader meets all three at once:
        Assert.Equal(3, ProjectWorkspace.CurrentFormatVersion);      // project.erksproject
        Assert.Equal(2, StudioAlbumDocument.CurrentFormatVersion);   // .erksalbum
        Assert.Equal(2, AlbumProject.CurrentFormatVersion);          // legacy .erksalbum

        // Only the first of those crosses a product boundary. If it is what
        // changed, tell PFA and PFR before this ships: their readers declare
        // their own SupportedProjectFormatVersion and will refuse the new file
        // outright - which is the agreed behaviour, and useless as a surprise.
        // If one of the album numbers changed, this test is only in the way;
        // update it and carry on.
        Assert.True(
            ProjectWorkspace.CurrentFormatVersion == 3,
            "ProjectWorkspace.CurrentFormatVersion changed. project.erksproject is read by "
            + "AutoCAD and Revit, each holding its own SupportedProjectFormatVersion, and both "
            + "refuse a file newer than they know. Tell PFA and PFR before this ships, then "
            + "update this test. See docs/PROJECT-FILE-READER-CONTRACT.md.");
    }

    [Fact]
    public void AFileFromANewerStudioIsRefusedRatherThanReadWithOldMeanings()
    {
        string body =
            "{\"formatVersion\": "
            + (ProjectWorkspace.CurrentFormatVersion + 1)
            + ", \"projectId\": \"x\"}";
        string path = Path.Combine(
            Path.GetTempPath(),
            "erks-newer-" + Guid.NewGuid().ToString("N") + ".erksproject");
        File.WriteAllText(path, body, Encoding.UTF8);
        try
        {
            var error = Assert.Throws<InvalidDataException>(() => ProjectWorkspaceStore.Load(path));

            Assert.Contains("newer", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileWithNoVersionAtAllIsTakenAsTheCurrentOne()
    {
        // Recorded because it is a trap rather than a decision: an absent
        // formatVersion keeps the property initialiser, so a file written
        // before the field existed reads as though it were current. It is
        // harmless today - the versions so far differ only by added fields -
        // and it is the reason a gate cannot be built on "is the field there".
        string path = Path.Combine(
            Path.GetTempPath(),
            "erks-unversioned-" + Guid.NewGuid().ToString("N") + ".erksproject");
        File.WriteAllText(path, "{\"projectId\": \"x\"}", Encoding.UTF8);
        try
        {
            ProjectWorkspace project = ProjectWorkspaceStore.Load(path);

            Assert.Equal(ProjectWorkspace.CurrentFormatVersion, project.FormatVersion);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static JsonValueKind Kind(JsonElement element, string property)
    {
        Assert.True(
            element.TryGetProperty(property, out JsonElement value),
            "'" + property + "' is read by AutoCAD or Revit out of project.erksproject and is "
            + "no longer written. Renaming it is a three-product change - see the class remarks.");
        return value.ValueKind;
    }

    private static JsonElement SaveAndReadBack(ProjectWorkspace project)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "erks-contract-" + Guid.NewGuid().ToString("N") + ".erksproject");
        try
        {
            ProjectWorkspaceStore.Save(project, path);
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            return document.RootElement.Clone();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static ProjectWorkspace BuildRepresentativeProject()
    {
        var project = new ProjectWorkspace
        {
            ProjectId = "contract-sample",
            Identity = { Code = "P-CON-001", Name = "Гэрээний жишиг төсөл" },
        };

        project.Sources.Add(new ProjectDesignSource
        {
            Id = "revit-source-001",
            Kind = DesignSourceKind.Revit,
            Name = "Барилга А",
            NativeDocumentTitle = "BuildingA.rvt",
            NativeDocumentPath = @"C:\projects\BuildingA.rvt",
            InboxFolder = @"C:\projects\P-CON-001\sources\Revit\deliveries",
            StageId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            WorkPackageId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Metadata = { [BuildingGroupMetadataKey] = "group-a" },
        });

        project.Sources.Add(new ProjectDesignSource
        {
            Id = "autocad-source-001",
            Kind = DesignSourceKind.AutoCad,
            Name = "Ерөнхий төлөвлөгөө",
            NativeDocumentPath = @"C:\projects\GeneralPlan.dwg",
            InboxFolder = @"C:\projects\P-CON-001\sources\AutoCAD\deliveries",
        });

        project.BuildingGroups.Add(new ProjectBuildingGroup { Id = "group-a", Name = "Барилга А" });

        // Not a sheet number: the key is the sheet's own identity, source and
        // sheet id joined and lower-cased, which is what PFR matches by prefix.
        // The first draft of this file used "A-01" and would have shipped a
        // sample whose keys no reader could have parsed.
        project.SheetBuildingAssignments["revit-source-001|a-01"] = "group-a";

        return project;
    }
}
