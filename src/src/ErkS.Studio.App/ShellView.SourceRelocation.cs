using System.IO;
using ErkS.Platform.Core;
using ErkS.Sync.Core;

namespace ErkS.Studio;

/// <summary>
/// Sources whose native file was moved (a folder renamed or reorganised) are pointed at it again.
///
/// 🔴 Master, 2026-10-24: «NP-Apartment-Sheet», «CommercialBuilding-Sheet» - эх файл олдсонгүй,
/// after the owner moved «…\TananTsamkhag\Nairamdal» to «…\NairamdalApartment\RVT». Folders get
/// reorganised as projects move to Erk-S Sync, so finding the file again is a function, not a
/// chore for a person: one same-named file under the nearest surviving folder (or the project's
/// sync folder) is relinked - through the very «Эх файлыг солих» path, with its checks; several
/// are named and left for a person. The owner's files are never touched.
/// </summary>
internal sealed partial class ShellView
{
    private async Task RelinkMovedSourcesAsync()
    {
        if (!state.HasOpenProject || !CanEditProjectContent())
            return;
        string email = account.Current?.Email ?? "";
        string projectId = state.Project.Cloud?.ServerProjectId ?? "";
        string[] syncRoots = (SyncTrayProcess.State()?.Folders ?? [])
            .Where(folder => folder.PresentHere && folder.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            .Select(folder => folder.CanonicalPath)
            .ToArray();

        var relinked = new List<string>();
        var ambiguous = new List<string>();
        foreach (ProjectDesignSource source in state.Project.Sources.ToList())
        {
            string recorded = source.NativeDocumentPath ?? "";
            if (recorded.Length == 0 || File.Exists(recorded))
                continue;
            ProjectSourceEditAuthority authority = ProjectCloudSyncAuthority.ResolveSource(state.Project, source, email);
            if (!authority.CanEdit)
                continue;

            IReadOnlyList<string> candidates = await Task.Run(() => ProjectSourceRelocation.FindCandidates(recorded, syncRoots));
            if (candidates.Count == 1)
            {
                if (RelinkNativeSource(source, authority.OwnerEmail, candidates[0]))
                    relinked.Add($"{source.Name}: {candidates[0]}");
            }
            else if (candidates.Count > 1)
            {
                ambiguous.Add($"{source.Name} ({candidates.Count} ижил нэртэй файл)");
            }
        }

        if (relinked.Count == 0 && ambiguous.Count == 0)
            return;
        string message = relinked.Count > 0
            ? "Зөөгдсөн эх файлыг шинэ замаар нь холболоо: " + string.Join("; ", relinked) + "."
            : "";
        if (ambiguous.Count > 0)
            message += (message.Length > 0 ? " " : "") + "Олон байршилтай тул «Эх файлыг солих»-оор сонгоно уу: " + string.Join("; ", ambiguous) + ".";
        SetStatus(message);
    }
}
