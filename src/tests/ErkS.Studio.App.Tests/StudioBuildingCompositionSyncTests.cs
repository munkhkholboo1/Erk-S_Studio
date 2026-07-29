using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using ErkS.Studio;
using PdfSharp.Pdf;

namespace ErkS.Studio.App.Tests;

public sealed class StudioBuildingCompositionSyncTests
{
    [Fact]
    public void ApplyCanonicalPreservesPendingLocalCompositionButCachesCloudUnion()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "local-building",
                    Name = "Локал барилга",
                    Order = 1,
                },
            ],
        };
        var canonical = new StudioCloudBuildingComposition
        {
            Version = 4,
            Groups =
            [
                new StudioCloudBuildingGroup
                {
                    Id = "cloud-building",
                    Name = "Cloud барилга",
                    Order = 2,
                },
            ],
            SheetAssignments =
            [
                new StudioCloudBuildingSheetAssignment
                {
                    SourceKey = "remote-revit",
                    SheetId = "sheet-7",
                    BuildingGroupId = "cloud-building",
                },
            ],
        };

        bool changed = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            canonical,
            preserveLocalEdits: true);

        Assert.True(changed);
        Assert.Equal("local-building", Assert.Single(project.BuildingGroups).Id);
        Assert.Equal(4, project.Cloud.SharedBuildingCompositionVersion);
        Assert.Equal("cloud-building", Assert.Single(project.Cloud.SharedBuildingGroups).Id);
        ProjectCloudBuildingSheetAssignmentReference shared =
            Assert.Single(project.Cloud.SharedBuildingSheetAssignments);
        Assert.Equal("remote-revit", shared.SourceKey);
        Assert.Equal("sheet-7", shared.SheetId);
    }

    [Fact]
    public void CreateUpdateKeepsForeignAssignmentsAndDropsStaleLocalAssignments()
    {
        var project = new ProjectWorkspace
        {
            Sources =
            [
                new ProjectDesignSource
                {
                    Id = "local-autocad",
                    Kind = DesignSourceKind.AutoCad,
                },
            ],
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Барилга 1",
                    Order = 1,
                },
            ],
        };
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceKey = "remote-revit",
                SheetId = "section-1",
                BuildingGroupId = "building-1",
            },
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceKey = "local-autocad",
                SheetId = "deleted-plan",
                BuildingGroupId = "building-1",
            },
        ];

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary());

        StudioCloudBuildingSheetAssignment assignment =
            Assert.Single(update.SheetAssignments);
        Assert.Equal("remote-revit", assignment.SourceKey);
        Assert.Equal("section-1", assignment.SheetId);
        Assert.Equal("building-1", assignment.BuildingGroupId);
    }

    [Fact]
    public void CreateUpdate_CopiedMirrorCannotDeleteCanonicalSourceAssignments()
    {
        var copiedSource = new ProjectDesignSource
        {
            Id = "copied-source",
            Kind = DesignSourceKind.Revit,
        };
        var project = new ProjectWorkspace
        {
            Sources = [copiedSource],
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Building 1",
                    Order = 1,
                },
            ],
        };
        ProjectCloudSyncMetadata.BindToCloudSource(
            project,
            copiedSource,
            "shared-source");
        ProjectCloudSyncMetadata.BindCloudOwner(
            copiedSource,
            "owner@erks.local");
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceOwnerEmail = "owner@erks.local",
                SourceKey = "shared-source",
                SheetId = "sheet-1",
                BuildingGroupId = "building-1",
            },
        ];

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary(),
                locallyAuthoritativeSources: []);

        StudioCloudBuildingSheetAssignment assignment =
            Assert.Single(update.SheetAssignments);
        Assert.Equal("owner@erks.local", assignment.SourceOwnerEmail);
        Assert.Equal("shared-source", assignment.SourceKey);
        Assert.Equal("sheet-1", assignment.SheetId);
    }

    [Fact]
    public void ApplyCanonicalReplacesLocalCompositionAfterSuccessfulSync()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "old-building",
                    Name = "Хуучин",
                    Order = 1,
                },
            ],
            SheetBuildingAssignments = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["local|sheet-1"] = "old-building",
            },
        };
        var canonical = new StudioCloudBuildingComposition
        {
            Version = 2,
            Groups =
            [
                new StudioCloudBuildingGroup
                {
                    Id = "building-2",
                    Name = "Барилга 2",
                    Order = 2,
                },
            ],
        };

        _ = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            canonical,
            preserveLocalEdits: false);

        Assert.Equal("building-2", Assert.Single(project.BuildingGroups).Id);
        Assert.Empty(project.SheetBuildingAssignments);
    }

    [Fact]
    public void ApplyCanonicalBeforeLibraryHydrationPreservesValidPersistedAssignment()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "school",
                    Name = "Сургууль",
                    Order = 3,
                },
            ],
            SheetBuildingAssignments = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["local-autocad|sheet-1"] = "school",
            },
        };
        var canonical = new StudioCloudBuildingComposition
        {
            Version = 6,
            Groups =
            [
                new StudioCloudBuildingGroup
                {
                    Id = "school",
                    Name = "Сургууль",
                    Order = 3,
                },
            ],
            SheetAssignments =
            [
                new StudioCloudBuildingSheetAssignment
                {
                    SourceOwnerEmail = "architect@example.com",
                    SourceKey = "school-autocad",
                    SheetId = "sheet-1",
                    BuildingGroupId = "school",
                },
            ],
        };

        _ = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            canonical,
            preserveLocalEdits: false);

        Assert.Equal(
            "school",
            project.SheetBuildingAssignments["local-autocad|sheet-1"]);
    }

    [Fact]
    public void SamePortableSheetForDifferentOwnersSurvivesAndOnlyLocalOwnerMaterializes()
    {
        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            "erks-building-owner-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            string sourcePath = Path.Combine(workDirectory, "source.pdf");
            using (var document = new PdfDocument())
            {
                document.AddPage();
                document.Save(sourcePath);
            }

            var source = new ProjectDesignSource
            {
                Id = "local-pdf-source",
                Kind = DesignSourceKind.Pdf,
                Name = "Local owner B source",
                NativeDocumentTitle = "source.pdf",
                NativeDocumentPath = sourcePath,
                InboxFolder = Path.Combine(workDirectory, "inbox"),
            };
            var project = new ProjectWorkspace
            {
                ProjectId = "building-owner-project",
                Sources = [source],
            };
            ProjectCloudSyncMetadata.BindToCloudSource(project, source, "shared-source");
            ProjectCloudSyncMetadata.BindCloudOwner(source, "owner-b@erks.local");

            LocalPdfSheetPackageImportResult package =
                new LocalPdfSheetPackageImporter().Import(project, source);
            var library = new SheetLibrary();
            library.Absorb(
                SheetPackageReader.Load(package.ManifestPath),
                source.Id);
            SheetRecord localSheet = Assert.Single(library.Snapshot());

            var canonical = new StudioCloudBuildingComposition
            {
                Version = 2,
                Groups =
                [
                    new StudioCloudBuildingGroup
                    {
                        Id = "building-a",
                        Name = "Building A",
                        Order = 1,
                    },
                    new StudioCloudBuildingGroup
                    {
                        Id = "building-b",
                        Name = "Building B",
                        Order = 2,
                    },
                ],
                SheetAssignments =
                [
                    new StudioCloudBuildingSheetAssignment
                    {
                        SourceOwnerEmail = "owner-a@erks.local",
                        SourceKey = "shared-source",
                        SheetId = "pdf-page-0001",
                        BuildingGroupId = "building-a",
                    },
                    new StudioCloudBuildingSheetAssignment
                    {
                        SourceOwnerEmail = "owner-b@erks.local",
                        SourceKey = "shared-source",
                        SheetId = "pdf-page-0001",
                        BuildingGroupId = "building-b",
                    },
                ],
            };

            bool changed = StudioBuildingCompositionSync.ApplyCanonical(
                project,
                library,
                canonical,
                preserveLocalEdits: false);

            Assert.True(changed);
            Assert.Equal(2, project.Cloud.SharedBuildingSheetAssignments.Count);
            Assert.Equal("building-b", project.SheetBuildingAssignments[localSheet.Key]);

            StudioCloudBuildingCompositionUpdateRequest update =
                StudioBuildingCompositionSync.CreateUpdate(project, library);
            Assert.Collection(
                update.SheetAssignments,
                first =>
                {
                    Assert.Equal("owner-a@erks.local", first.SourceOwnerEmail);
                    Assert.Equal("building-a", first.BuildingGroupId);
                },
                second =>
                {
                    Assert.Equal("owner-b@erks.local", second.SourceOwnerEmail);
                    Assert.Equal("building-b", second.BuildingGroupId);
                });
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    [Fact]
    public void RemoveSourceAssignmentsKeepsForeignOwnerWithSamePortableSourceKey()
    {
        var source = new ProjectDesignSource
        {
            Id = "local-source",
            Kind = DesignSourceKind.Revit,
        };
        var project = new ProjectWorkspace
        {
            Sources = [source],
            SheetBuildingAssignments = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["local-source|sheet-1"] = "building-a",
            },
        };
        ProjectCloudSyncMetadata.BindToCloudSource(project, source, "shared-source");
        ProjectCloudSyncMetadata.BindCloudOwner(source, "owner-a@erks.local");
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceOwnerEmail = "owner-a@erks.local",
                SourceKey = "shared-source",
                SheetId = "sheet-1",
                BuildingGroupId = "building-a",
            },
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceOwnerEmail = "",
                SourceKey = "shared-source",
                SheetId = "legacy-sheet",
                BuildingGroupId = "building-a",
            },
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceOwnerEmail = "owner-b@erks.local",
                SourceKey = "shared-source",
                SheetId = "sheet-1",
                BuildingGroupId = "building-b",
            },
        ];

        bool changed = StudioBuildingCompositionSync.RemoveSourceAssignments(
            project,
            source,
            ["local-source|sheet-1"]);

        Assert.True(changed);
        Assert.Empty(project.SheetBuildingAssignments);
        ProjectCloudBuildingSheetAssignmentReference foreign =
            Assert.Single(project.Cloud.SharedBuildingSheetAssignments);
        Assert.Equal("owner-b@erks.local", foreign.SourceOwnerEmail);
        Assert.Equal("shared-source", foreign.SourceKey);
    }

    [Fact]
    public void CreateUpdateMergesRemoteOnlyBuildingAheadOfPendingLocalBuilding()
    {
        var localSource = new ProjectDesignSource
        {
            Id = "local-source",
            Kind = DesignSourceKind.Revit,
        };
        var project = new ProjectWorkspace
        {
            Sources = [localSource],
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Building 1 canonical",
                    Order = 1,
                },
            ],
        };
        ProjectCloudSyncMetadata.BindToCloudSource(project, localSource, "shared-source");
        ProjectCloudSyncMetadata.BindCloudOwner(localSource, "owner-a@erks.local");
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "Building 1 canonical",
                Order = 1,
            },
        ];
        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Building 1 local",
                    Order = 1,
                },
                new ProjectBuildingGroup
                {
                    Id = "building-3",
                    Name = "Building 3 pending",
                    Order = 2,
                },
            ]);
        project.BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "building-1",
                Name = "Building 1 local",
                Order = 1,
            },
            new ProjectBuildingGroup
            {
                Id = "building-3",
                Name = "Building 3 pending",
                Order = 2,
            },
        ];
        project.Cloud.SharedBuildingGroups.Add(
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-2",
                Name = "Building 2 remote",
                Order = 2,
            });
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceOwnerEmail = "owner-b@erks.local",
                SourceKey = "shared-source",
                SheetId = "sheet-1",
                BuildingGroupId = "building-2",
            },
        ];

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary());

        Assert.Equal(
            ["building-1", "building-2", "building-3"],
            update.Groups.Select(group => group.Id));
        Assert.Equal([1, 2, 3], update.Groups.Select(group => group.Order));
        Assert.Equal("Building 1 local", update.Groups[0].Name);
        StudioCloudBuildingSheetAssignment assignment =
            Assert.Single(update.SheetAssignments);
        Assert.Equal("owner-b@erks.local", assignment.SourceOwnerEmail);
        Assert.Equal("building-2", assignment.BuildingGroupId);
    }

    [Fact]
    public void ExplicitlyDeletedCanonicalGroupIsNotResurrectedWhileRemoteAdditionIsMerged()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Building 1",
                    Order = 1,
                },
                new ProjectBuildingGroup
                {
                    Id = "building-2",
                    Name = "Building 2",
                    Order = 2,
                },
            ],
        };
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "Building 1",
                Order = 1,
            },
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-2",
                Name = "Building 2",
                Order = 2,
            },
        ];

        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Building 1",
                    Order = 1,
                },
            ]);
        project.BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "building-1",
                Name = "Building 1",
                Order = 1,
            },
        ];

        // A collaborator added building-3 after this editor began. It should
        // merge, while the explicit building-2 deletion remains authoritative.
        project.Cloud.SharedBuildingGroups.Add(
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-3",
                Name = "Building 3",
                Order = 3,
            });

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary(),
                locallyAuthoritativeSources: []);

        Assert.Equal(
            ["building-1", "building-3"],
            update.Groups.Select(group => group.Id));
        Assert.Contains(
            "building-2",
            project.Cloud.PendingBuildingGroupDeletionIds,
            StringComparer.OrdinalIgnoreCase);

        ProjectCloudSyncMetadata.MarkBuildingCompositionSynced(project);

        Assert.Empty(project.Cloud.PendingBuildingGroupDeletionIds);
    }

    [Fact]
    public void ReaddingDeletedGroupCancelsItsTombstone()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Building 1",
                    Order = 1,
                },
            ],
        };

        StudioBuildingCompositionSync.RecordLocalGroupSet(project, []);
        Assert.Contains(
            "building-1",
            project.Cloud.PendingBuildingGroupDeletionIds,
            StringComparer.OrdinalIgnoreCase);
        project.BuildingGroups = [];

        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Building 1 restored",
                    Order = 1,
                },
            ]);

        Assert.Empty(project.Cloud.PendingBuildingGroupDeletionIds);
    }

    [Fact]
    public void ConcurrentRemoteRenameOfSameGroupStopsWholeStateUpdate()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Base name",
                    Order = 1,
                },
            ],
        };
        project.Cloud.SharedBuildingCompositionVersion = 1;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "Base name",
                Order = 1,
            },
        ];

        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Local rename",
                    Order = 1,
                },
            ]);
        project.BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "building-1",
                Name = "Local rename",
                Order = 1,
            },
        ];
        project.Cloud.BuildingCompositionPending = true;

        _ = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            new StudioCloudBuildingComposition
            {
                Version = 2,
                Groups =
                [
                    new StudioCloudBuildingGroup
                    {
                        Id = "building-1",
                        Name = "Remote rename",
                        Order = 1,
                    },
                ],
            },
            preserveLocalEdits: true);

        StudioBuildingCompositionConflictException error =
            Assert.Throws<StudioBuildingCompositionConflictException>(() =>
                StudioBuildingCompositionSync.CreateUpdate(
                    project,
                    new SheetLibrary(),
                    locallyAuthoritativeSources: []));

        Assert.Equal(
            StudioBuildingCompositionConflictException.ConflictReasonCode,
            error.ReasonCode);
        Assert.Contains("building-1:name", error.Conflicts);
        Assert.True(project.Cloud.BuildingCompositionPending);
        Assert.Equal("Local rename", Assert.Single(project.BuildingGroups).Name);
        Assert.Equal("Remote rename", Assert.Single(project.Cloud.SharedBuildingGroups).Name);
    }

    [Fact]
    public void LocalRenameAndRemoteReorderMergeFromCapturedBase()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup { Id = "building-a", Name = "A", Order = 1 },
                new ProjectBuildingGroup { Id = "building-b", Name = "B", Order = 2 },
            ],
        };
        project.Cloud.SharedBuildingCompositionVersion = 4;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
                { Id = "building-a", Name = "A", Order = 1 },
            new ProjectCloudBuildingGroupReference
                { Id = "building-b", Name = "B", Order = 2 },
        ];

        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup
                    { Id = "building-a", Name = "A local", Order = 1 },
                new ProjectBuildingGroup
                    { Id = "building-b", Name = "B", Order = 2 },
            ]);
        project.BuildingGroups =
        [
            new ProjectBuildingGroup
                { Id = "building-a", Name = "A local", Order = 1 },
            new ProjectBuildingGroup
                { Id = "building-b", Name = "B", Order = 2 },
        ];
        project.Cloud.BuildingCompositionPending = true;

        _ = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            new StudioCloudBuildingComposition
            {
                Version = 5,
                Groups =
                [
                    new StudioCloudBuildingGroup
                        { Id = "building-x", Name = "X remote", Order = 1 },
                    new StudioCloudBuildingGroup
                        { Id = "building-a", Name = "A", Order = 2 },
                    new StudioCloudBuildingGroup
                        { Id = "building-b", Name = "B", Order = 3 },
                ],
            },
            preserveLocalEdits: true);

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary(),
                locallyAuthoritativeSources: []);

        Assert.Equal(
            ["building-x", "building-a", "building-b"],
            update.Groups.Select(group => group.Id));
        Assert.Equal("A local", update.Groups[1].Name);
        Assert.Equal([1, 2, 3], update.Groups.Select(group => group.Order));
    }

    [Fact]
    public void LocalReorderRemainsPendingWhenRemoteOrderMatchesCapturedBase()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup { Id = "building-a", Name = "A", Order = 1 },
                new ProjectBuildingGroup { Id = "building-b", Name = "B", Order = 2 },
            ],
        };
        project.Cloud.SharedBuildingCompositionVersion = 3;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
                { Id = "building-a", Name = "A", Order = 1 },
            new ProjectCloudBuildingGroupReference
                { Id = "building-b", Name = "B", Order = 2 },
        ];

        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup { Id = "building-b", Name = "B", Order = 1 },
                new ProjectBuildingGroup { Id = "building-a", Name = "A", Order = 2 },
            ]);
        project.BuildingGroups =
        [
            new ProjectBuildingGroup { Id = "building-b", Name = "B", Order = 1 },
            new ProjectBuildingGroup { Id = "building-a", Name = "A", Order = 2 },
        ];

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary(),
                locallyAuthoritativeSources: []);

        Assert.Equal(
            ["building-b", "building-a"],
            update.Groups.Select(group => group.Id));
    }

    [Fact]
    public void ConcurrentDivergentReordersStopWholeStateUpdate()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup { Id = "building-a", Name = "A", Order = 1 },
                new ProjectBuildingGroup { Id = "building-b", Name = "B", Order = 2 },
                new ProjectBuildingGroup { Id = "building-c", Name = "C", Order = 3 },
            ],
        };
        project.Cloud.SharedBuildingCompositionVersion = 1;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
                { Id = "building-a", Name = "A", Order = 1 },
            new ProjectCloudBuildingGroupReference
                { Id = "building-b", Name = "B", Order = 2 },
            new ProjectCloudBuildingGroupReference
                { Id = "building-c", Name = "C", Order = 3 },
        ];

        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup { Id = "building-c", Name = "C", Order = 1 },
                new ProjectBuildingGroup { Id = "building-a", Name = "A", Order = 2 },
                new ProjectBuildingGroup { Id = "building-b", Name = "B", Order = 3 },
            ]);
        project.BuildingGroups =
        [
            new ProjectBuildingGroup { Id = "building-c", Name = "C", Order = 1 },
            new ProjectBuildingGroup { Id = "building-a", Name = "A", Order = 2 },
            new ProjectBuildingGroup { Id = "building-b", Name = "B", Order = 3 },
        ];
        project.Cloud.BuildingCompositionPending = true;

        _ = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            new StudioCloudBuildingComposition
            {
                Version = 2,
                Groups =
                [
                    new StudioCloudBuildingGroup
                        { Id = "building-b", Name = "B", Order = 1 },
                    new StudioCloudBuildingGroup
                        { Id = "building-c", Name = "C", Order = 2 },
                    new StudioCloudBuildingGroup
                        { Id = "building-a", Name = "A", Order = 3 },
                ],
            },
            preserveLocalEdits: true);

        StudioBuildingCompositionConflictException error =
            Assert.Throws<StudioBuildingCompositionConflictException>(() =>
                StudioBuildingCompositionSync.CreateUpdate(
                    project,
                    new SheetLibrary(),
                    locallyAuthoritativeSources: []));

        Assert.Contains("building-a:order", error.Conflicts);
        Assert.Contains("building-b:order", error.Conflicts);
        Assert.Contains("building-c:order", error.Conflicts);
    }

    [Fact]
    public void LegacyPendingMirrorWithoutEditBaseStopsBeforeOverwritingRemoteGroup()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Stale local name",
                    Order = 2,
                },
            ],
        };
        project.Cloud.BuildingCompositionPending = true;
        project.Cloud.SharedBuildingCompositionVersion = 7;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "New remote name",
                Order = 1,
            },
        ];

        StudioBuildingCompositionConflictException error =
            Assert.Throws<StudioBuildingCompositionConflictException>(() =>
                StudioBuildingCompositionSync.CreateUpdate(
                    project,
                    new SheetLibrary(),
                    locallyAuthoritativeSources: []));

        Assert.Contains("building-1:unbased", error.Conflicts);
        Assert.True(project.Cloud.BuildingCompositionPending);
    }

    [Fact]
    public void ConcurrentRemoteChangeMakesLocalDeletionExplicitConflict()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Base",
                    Order = 1,
                },
            ],
        };
        project.Cloud.SharedBuildingCompositionVersion = 2;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "Base",
                Order = 1,
            },
        ];

        StudioBuildingCompositionSync.RecordLocalGroupSet(project, []);
        project.BuildingGroups = [];
        project.Cloud.BuildingCompositionPending = true;
        _ = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            new StudioCloudBuildingComposition
            {
                Version = 3,
                Groups =
                [
                    new StudioCloudBuildingGroup
                    {
                        Id = "building-1",
                        Name = "Remote changed",
                        Order = 1,
                    },
                ],
            },
            preserveLocalEdits: true);

        StudioBuildingCompositionConflictException error =
            Assert.Throws<StudioBuildingCompositionConflictException>(() =>
                StudioBuildingCompositionSync.CreateUpdate(
                    project,
                    new SheetLibrary(),
                    locallyAuthoritativeSources: []));

        Assert.Contains("building-1:delete", error.Conflicts);
    }

    [Fact]
    public void CapturedEditBaseSurvivesProjectRoundTripAndClearsAfterAcknowledgement()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-building-edit-base-" + Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(root, ProjectWorkspace.DefaultFileName);
        var project = new ProjectWorkspace
        {
            ProjectId = "composition-base-project",
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Base",
                    Order = 1,
                },
            ],
        };
        project.Cloud.SharedBuildingCompositionVersion = 9;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "Base",
                Order = 1,
            },
        ];

        try
        {
            StudioBuildingCompositionSync.RecordLocalGroupSet(
                project,
                [
                    new ProjectBuildingGroup
                    {
                        Id = "building-1",
                        Name = "Local",
                        Order = 1,
                    },
                ]);
            ProjectWorkspaceStore.Save(project, projectPath);

            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

            Assert.True(loaded.Cloud.BuildingCompositionEditBaseCaptured);
            Assert.Equal(9, loaded.Cloud.BuildingCompositionEditBaseVersion);
            Assert.Equal(
                "Base",
                Assert.Single(loaded.Cloud.BuildingCompositionEditBaseGroups).Name);

            ProjectCloudSyncMetadata.MarkBuildingCompositionSynced(loaded);

            Assert.False(loaded.Cloud.BuildingCompositionEditBaseCaptured);
            Assert.Equal(0, loaded.Cloud.BuildingCompositionEditBaseVersion);
            Assert.Empty(loaded.Cloud.BuildingCompositionEditBaseGroups);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepeatedPendingMarksDoNotSilentlyRebaseLocalComposition()
    {
        var project = new ProjectWorkspace();
        project.Cloud.SharedBuildingCompositionVersion = 1;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "Initial canonical",
                Order = 1,
            },
        ];

        ProjectCloudSyncMetadata.MarkBuildingCompositionPending(project);
        project.Cloud.SharedBuildingCompositionVersion = 2;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "New remote canonical",
                Order = 1,
            },
        ];

        ProjectCloudSyncMetadata.MarkBuildingCompositionPending(project);

        Assert.Equal(1, project.Cloud.BuildingCompositionEditBaseVersion);
        Assert.Equal(
            "Initial canonical",
            Assert.Single(project.Cloud.BuildingCompositionEditBaseGroups).Name);
    }

    [Fact]
    public void CanonicalVersionRegressionStopsBeforeRebasingPendingEdit()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Base",
                    Order = 1,
                },
            ],
        };
        project.Cloud.SharedBuildingCompositionVersion = 3;
        project.Cloud.SharedBuildingGroups =
        [
            new ProjectCloudBuildingGroupReference
            {
                Id = "building-1",
                Name = "Base",
                Order = 1,
            },
        ];
        StudioBuildingCompositionSync.RecordLocalGroupSet(
            project,
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Local",
                    Order = 1,
                },
            ]);
        project.BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = "building-1",
                Name = "Local",
                Order = 1,
            },
        ];

        project.Cloud.SharedBuildingCompositionVersion = 2;

        StudioBuildingCompositionConflictException error =
            Assert.Throws<StudioBuildingCompositionConflictException>(() =>
                StudioBuildingCompositionSync.CreateUpdate(
                    project,
                    new SheetLibrary(),
                    locallyAuthoritativeSources: []));

        Assert.Contains("composition:version-regression", error.Conflicts);
    }
}
