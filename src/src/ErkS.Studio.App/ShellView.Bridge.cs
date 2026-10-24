using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// The bridge's actions (docs/STU-BRIDGE.md). Each one calls the function the
/// matching button calls, on the UI thread, inside the bridge's quiet mode so
/// no dialog waits for a person who is not there.
/// </summary>
internal sealed partial class ShellView
{
    private StudioBridgeServer? bridgeServer;

    /// <summary>Set by the bridge while it runs the cloud button: true sends, false only previews.</summary>
    private bool? bridgeSyncDecision;

    /// <summary>The plan the sync would have shown in its dialog, kept for the bridge's answer.</summary>
    private CloudSyncPreviewPlan? lastBridgeSyncPlan;

    /// <summary>Every warning of the last album build - the status line names only the first.</summary>
    private IReadOnlyList<string> lastAlbumBuildWarnings = [];

    private void StartBridge()
    {
        try
        {
            bridgeServer = new StudioBridgeServer(
                StudioBridgeProtocol.PipeName(Environment.ProcessId),
                request => Root.Dispatcher.InvokeAsync(() => HandleBridgeRequestAsync(request)).Task.Unwrap());
            bridgeServer.Start();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            bridgeServer = null;
            SetStatus("Studio-гийн гүүр эхэлсэнгүй: " + exception.Message);
        }
    }

    private void StopBridge()
    {
        bridgeServer?.Dispose();
        bridgeServer = null;
    }

    private async Task<StudioBridgeResponse> HandleBridgeRequestAsync(StudioBridgeRequest request)
    {
        StudioBridgeAction? action = StudioBridgeActionCatalog.Find(request.Action);
        if (action is null)
        {
            return StudioBridgeProtocol.Refuse(
                request.Id,
                "unknown_action",
                $"«{request.Action}» гэх үйлдэл алга. «actions»-аар жагсаалтыг харна уу.");
        }

        using (StudioBridgeQuietMode.Shared.Enter())
        {
            StudioBridgeResponse response = await RunBridgeActionAsync(request, action.Name);
            return response with
            {
                Notices = action.Name == "notices"
                    ? response.Notices
                    : StudioBridgeQuietMode.Shared.Drain(),
            };
        }
    }

    private async Task<StudioBridgeResponse> RunBridgeActionAsync(StudioBridgeRequest request, string action)
    {
        switch (action)
        {
            case "actions":
                return StudioBridgeProtocol.Accept(request.Id, StudioBridgeActionCatalog.All);
            case "status":
                return StudioBridgeProtocol.Accept(request.Id, BridgeStatus());
            case "notices":
                return StudioBridgeProtocol.Accept(request.Id, null, notices: StudioBridgeQuietMode.Shared.Drain());
            case "projects":
                return StudioBridgeProtocol.Accept(
                    request.Id,
                    new LocalProjectCatalog().ListProjects()
                        .Where(item => MaySeeProject(item.ProjectId))
                        .Select(item => new
                        {
                            code = item.ProjectCode,
                            name = item.DisplayName,
                            stage = item.StageName,
                            path = item.ProjectPath,
                            syncStatus = item.SyncStatus,
                        })
                        .ToList());
            case "open-project":
                return await BridgeOpenProjectAsync(request);
            case "sync-make":
            case "sync-join":
            case "sync-unlink":
            case "sync-unregister":
                return await BridgeSyncFolderAsync(request, action["sync-".Length..]);
            case "sync-decide":
                return BridgeSyncDecide(request);
            case "sync-status":
                return BridgeSyncStatus(request);
            case "sync-folders":
                return await BridgeSyncFoldersAsync(request);
            case "close":
                _ = Root.Dispatcher.BeginInvoke(new Action(() => Window.GetWindow(Root)?.Close()));
                return StudioBridgeProtocol.Accept(request.Id, null, "Studio хаагдаж байна.");
        }

        if (!state.HasOpenProject)
            return StudioBridgeProtocol.Refuse(request.Id, "no_open_project", "Нээлттэй төсөл алга. Эхлээд «open-project».");

        switch (action)
        {
            case "check-sources":
            {
                await RelinkMovedSourcesAsync();
                SourceRefreshOutcome outcome = await CheckForSourceUpdatesAsync();
                return outcome.Succeeded
                    ? StudioBridgeProtocol.Accept(request.Id, new { outcome.CheckedCount, outcome.ChangedCount }, statusText.Text)
                    : StudioBridgeProtocol.Refuse(request.Id, outcome.Ran ? "check_failed" : "check_blocked", outcome.FailureMn);
            }
            case "rebuild-album":
            {
                bool rebuilt = UpdateAlbum(silent: false, statusPrefix: "Гүүрээр альбум шинэчлэгдлээ");
                return rebuilt
                    ? StudioBridgeProtocol.Accept(request.Id, new { album = lastAlbumPath, warnings = lastAlbumBuildWarnings }, statusText.Text)
                    : StudioBridgeProtocol.Refuse(request.Id, "rebuild_failed", statusText.Text);
            }
            case "album-format":
                return BridgeAlbumFormat(request);
            case "components":
                return BridgeComponents(request);
            case "sheet-numbers":
                return BridgeSheetNumbers(request);
            case "rearm-component":
                return BridgeRearmComponent(request);
            case "sync-preview":
            case "sync":
                return await BridgeSyncAsync(request, send: action == "sync");
            case "relink-source":
                return BridgeRelinkSource(request);
            case "import-front-matter":
                return BridgeImportFrontMatter(request);
        }

        return StudioBridgeProtocol.Refuse(request.Id, "not_implemented", $"«{action}» гүүрт бүртгэгдсэн боловч хэрэгжээгүй.");
    }

    /// <summary>
    /// What a Sync works from: the components this device's contribution build
    /// renders (the same snapshot Sync renders), the Cloud album's components
    /// and the codes waiting to go. Rendered into a temporary folder and
    /// removed; nothing in the project changes.
    /// </summary>
    /// <summary>
    /// Each source sheet's own number and the number the album prints it
    /// under - what a source cites another sheet by
    /// (_shared/sheet-reference-contract-2026-10-20.md).
    /// </summary>
    private StudioBridgeResponse BridgeSheetNumbers(StudioBridgeRequest request)
    {
        if (!state.HasOpenProject)
            return StudioBridgeProtocol.Refuse(request.Id, "no_project", "Төсөл нээгээгүй байна.");
        AlbumProject project = state.CreateAlbumBuildProject(reconcileLinkedProjectAssets: false);
        if (!AlbumBuilder.TryCreateRequest(project, state.Library, out AlbumBuildRequest build))
            return StudioBridgeProtocol.Refuse(request.Id, "no_album", "Альбум угсрах эх хуудас алга.");
        return StudioBridgeProtocol.Accept(request.Id, new
        {
            project = state.Project.Identity.Code,
            sheets = WorkingDrawingSheetNumbering.PrintNumbers(build).Select(sheet => new
            {
                sheet.SourceId,
                sheet.SheetId,
                sheet.SourceNumber,
                sheet.SheetName,
                sheet.Mark,
                sheet.PrintNumber,
                sheet.Label,
                sheet.Section,
            }),
        });
    }

    private StudioBridgeResponse BridgeComponents(StudioBridgeRequest request)
    {
        string workFolder = Path.Combine(Path.GetTempPath(), "erks-bridge-components-" + Guid.NewGuid().ToString("N"));
        try
        {
            AlbumBuildResult build = BuildAlbumContributionSnapshot(workFolder);
            return StudioBridgeProtocol.Accept(request.Id, new
            {
                local = build.Components.Select(component => new
                {
                    component.Code,
                    component.SequenceKey,
                    pages = component.PageNumbers.Count,
                }),
                cloud = (state.Project.Cloud.SharedAlbumComponents ?? []).Select(component => new
                {
                    component.Code,
                    pages = component.PageNumbers?.Count ?? 0,
                }),
                pending = ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project),
            });
        }
        catch (Exception exception)
        {
            return StudioBridgeProtocol.Refuse(request.Id, "components_failed", exception.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workFolder))
                    Directory.Delete(workFolder, recursive: true);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Marks one component this device draws for the next Sync - the way back
    /// for work the shared album lost (Batnasan R12: the Revit sheets were
    /// removed by a sync while the local build drew them). Only a component
    /// the contribution build renders, in its canonical code, is accepted.
    /// </summary>
    private StudioBridgeResponse BridgeRearmComponent(StudioBridgeRequest request)
    {
        string? code = request.Arg("code")?.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return StudioBridgeProtocol.Refuse(request.Id, "code_required", "code=<бүрдлийн код> өгнө үү.");
        if (!CanEditProjectContent())
            return StudioBridgeProtocol.Refuse(request.Id, "forbidden", statusText.Text);

        string workFolder = Path.Combine(Path.GetTempPath(), "erks-bridge-rearm-" + Guid.NewGuid().ToString("N"));
        try
        {
            AlbumBuildResult build = BuildAlbumContributionSnapshot(workFolder);
            List<StudioCloudAlbumSection> rendered = CreateCanonicalComponentManifest(
                build,
                SharedCloudSources());
            StudioCloudAlbumSection? component = rendered.FirstOrDefault(item =>
                item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (component is null)
            {
                return StudioBridgeProtocol.Refuse(
                    request.Id,
                    "not_rendered",
                    "Энэ төхөөрөмжийн угсралт уг бүрдлийг зурдаггүй: " + code);
            }

            state.MarkAlbumComponentChanged(component.Code);
            state.SaveProject();
            return StudioBridgeProtocol.Accept(request.Id, new { code = component.Code, pages = component.PageNumbers.Length });
        }
        catch (Exception exception)
        {
            return StudioBridgeProtocol.Refuse(request.Id, "rearm_failed", exception.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workFolder))
                    Directory.Delete(workFolder, recursive: true);
            }
            catch
            {
            }
        }
    }

    private object BridgeStatus()
    {
        string version =
            typeof(ShellView).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            typeof(ShellView).Assembly.GetName().Version?.ToString() ?? "";
        return new
        {
            processId = Environment.ProcessId,
            version,
            page = activePage.ToString(),
            busy = new
            {
                projectOpen = projectOpenInProgress,
                sourceRefresh = sourceRefreshInProgress,
                albumRefresh = albumRefreshInProgress,
                sync = syncInProgress || syncPreparationInProgress,
            },
            statusText = statusText.Text,
            project = state.HasOpenProject
                ? new
                {
                    code = state.Project.Identity.Code,
                    name = state.Project.Identity.Name,
                    projectType = state.Project.Identity.ProjectType,
                    stage = state.Project.Identity.StageCode,
                    path = state.ProjectPath,
                    albumTemplate = state.Album.TemplateId,
                    albumPages = state.Album.Pages.Count,
                    albumPdf = lastAlbumPath ?? ResolveCurrentProjectAlbumPath(),
                    albumPdfPages = BridgePdfPageCount(lastAlbumPath ?? ResolveCurrentProjectAlbumPath()),
                    cloud = new
                    {
                        status = state.Project.Cloud.SyncStatus,
                        lastSyncedAtUtc = state.Project.Cloud.LastSyncedAtUtc,
                        note = state.Project.Cloud.LastSyncNote,
                    },
                    sources = state.Project.Sources.Select(source => new
                    {
                        id = source.Id,
                        name = source.Name,
                        kind = source.Kind.ToString(),
                        purpose = ProjectDesignSourceClassification.EffectivePurpose(source).ToString(),
                        status = source.Status,
                        nativeDocumentPath = source.NativeDocumentPath,
                        lastPackageAtUtc = source.LastPackageAtUtc,
                    }).ToList(),
                }
                : null,
        };
    }

    private async Task<StudioBridgeResponse> BridgeOpenProjectAsync(StudioBridgeRequest request)
    {
        string? path = request.Arg("path");
        string? code = request.Arg("code");
        if (path is null && code is not null)
        {
            path = new LocalProjectCatalog().ListProjects()
                .FirstOrDefault(item => item.ProjectCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                ?.ProjectPath;
        }
        if (string.IsNullOrWhiteSpace(path))
            return StudioBridgeProtocol.Refuse(request.Id, "project_not_found", "Төсөл олдсонгүй: path эсвэл code заана уу.");

        await OpenProjectAsync(path);
        return state.HasOpenProject &&
               string.Equals(
                   Path.GetFullPath(state.ProjectPath ?? ""),
                   Path.GetFullPath(path.EndsWith(".erksproject", StringComparison.OrdinalIgnoreCase)
                       ? path
                       : Path.Combine(path, "project.erksproject")),
                   StringComparison.OrdinalIgnoreCase)
            ? StudioBridgeProtocol.Accept(request.Id, new { path = state.ProjectPath, code = state.Project.Identity.Code }, statusText.Text)
            : StudioBridgeProtocol.Refuse(request.Id, "open_failed", statusText.Text);
    }

    private async Task<StudioBridgeResponse> BridgeSyncAsync(StudioBridgeRequest request, bool send)
    {
        if (send && !string.Equals(request.Arg("confirm"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return StudioBridgeProtocol.Refuse(
                request.Id,
                "confirm_required",
                "Синк Cloud руу илгээнэ: «confirm=true» гэж баталгаажуулна уу (эхлээд «sync-preview»-ээр харна).");
        }

        lastBridgeSyncPlan = null;
        bridgeSyncDecision = send;
        try
        {
            await RefreshAlbumAsync();
        }
        finally
        {
            bridgeSyncDecision = null;
        }

        CloudSyncPreviewPlan? plan = lastBridgeSyncPlan;
        object result = new
        {
            plan = plan is null
                ? null
                : new
                {
                    uploads = plan.Uploads.Select(item => new { item.Code, item.Title, item.Detail }),
                    downloads = plan.Downloads.Select(item => new { item.Code, item.Title, item.Detail }),
                    blocked = plan.Blocked.Select(item => new { item.Code, item.Title, item.Detail }),
                },
            sent = send && plan is not null,
            cloud = new
            {
                status = state.Project.Cloud.SyncStatus,
                lastSyncedAtUtc = state.Project.Cloud.LastSyncedAtUtc,
                note = state.Project.Cloud.LastSyncNote,
            },
        };
        return StudioBridgeProtocol.Accept(request.Id, result, statusText.Text);
    }

    private StudioBridgeResponse BridgeRelinkSource(StudioBridgeRequest request)
    {
        string? key = request.Arg("source");
        string? path = request.Arg("path");
        if (key is null || path is null)
            return StudioBridgeProtocol.Refuse(request.Id, "bad_request", "source=<id эсвэл нэр> path=<файл> заана уу.");
        if (!File.Exists(path))
            return StudioBridgeProtocol.Refuse(request.Id, "file_not_found", "Файл олдсонгүй: " + path);

        ProjectDesignSource? source = state.Project.Sources.FirstOrDefault(item =>
            item.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return StudioBridgeProtocol.Refuse(request.Id, "source_not_found", "Эх үүсвэр олдсонгүй: " + key);
        if (!EnsureProjectContentPermission())
            return StudioBridgeProtocol.Refuse(request.Id, "forbidden", statusText.Text);

        ProjectSourceEditAuthority authority =
            ProjectCloudSyncAuthority.ResolveSource(state.Project, source, account.Current?.Email);
        if (!authority.CanEdit)
            return StudioBridgeProtocol.Refuse(request.Id, "forbidden", "Бусдын эх үүсвэрийн локал файлыг солих боломжгүй.");

        return RelinkNativeSource(source, authority.OwnerEmail, path)
            ? StudioBridgeProtocol.Accept(request.Id, new { source = source.Id, path = source.NativeDocumentPath }, statusText.Text)
            : StudioBridgeProtocol.Refuse(request.Id, "relink_refused", statusText.Text);
    }

    private StudioBridgeResponse BridgeImportFrontMatter(StudioBridgeRequest request)
    {
        string? file = request.Arg("file");
        if (file is null || !File.Exists(file))
            return StudioBridgeProtocol.Refuse(request.Id, "file_not_found", "file=<.md> олдсонгүй.");
        if (!CanEditProjectContent())
            return StudioBridgeProtocol.Refuse(request.Id, "forbidden", statusText.Text);

        WorkingDrawingFrontMatter matter = WorkingDrawingFrontMatterDraft.Parse(File.ReadAllText(file));
        bool rebuilt = ApplyWorkingDrawingFrontMatter(matter);
        return StudioBridgeProtocol.Accept(
            request.Id,
            new
            {
                noteCharacters = matter.ExplanatoryNote.Length,
                composition = matter.Composition.Count,
                indicators = matter.Indicators.Count,
                blankIndicators = matter.Indicators.Count(row => string.IsNullOrWhiteSpace(row.Value)),
                rebuilt,
                // What the build said about it - a row that did not fit is
                // reported here, not only on the status line (PFR, 2026-10-20).
                warnings = lastAlbumBuildWarnings,
            },
            statusText.Text);
    }

    private static (string Key, int Pages) bridgePdfPages = ("", -1);

    /// <summary>
    /// The album PDF's own page count. <c>albumPages</c> counts album entries, and one entry
    /// (a two-page drawing list) can print as several pages - BN 2026-10-23: 60 entries, 61
    /// pages, and the Twin compares pages. Cached by path, size and time: status is polled.
    /// </summary>
    private static int BridgePdfPageCount(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return -1;
        FileInfo file = new(path);
        string key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        if (bridgePdfPages.Key == key)
            return bridgePdfPages.Pages;
        int pages;
        try
        {
            using var document = PdfSharp.Pdf.IO.PdfReader.Open(file.FullName, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            pages = document.PageCount;
        }
        catch (Exception)
        {
            // PdfSharp throws plain Exception on a file it cannot parse; -1 says «unknown».
            pages = -1;
        }
        bridgePdfPages = (key, pages);
        return pages;
    }
}
