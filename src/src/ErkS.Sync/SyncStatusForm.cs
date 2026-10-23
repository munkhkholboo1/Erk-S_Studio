using ErkS.Sync.Core;

namespace ErkS.Sync;

/// <summary>
/// «Erk-S Sync — төлөв»: the folders this machine holds, each member machine and how far their
/// copies differ. Counts only; the file list lives in Studio's «Erk-S Sync» section.
/// </summary>
internal sealed class SyncStatusForm : Form
{
    public SyncStatusForm(SyncState state, string? focusPath)
    {
        Text = "Erk-S Sync — төлөв";
        Width = 760;
        Height = 460;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
        };
        box.Text = Describe(state, focusPath);
        var close = new Button { Text = "Хаах", Dock = DockStyle.Bottom, Height = 34 };
        close.Click += (_, _) => Close();
        Controls.Add(box);
        Controls.Add(close);
    }

    private static string Describe(SyncState state, string? focusPath)
    {
        var lines = new List<string>();
        if (state.ServerStatus is not ("ok" or ""))
            lines.Add("Сервер: " + (string.IsNullOrWhiteSpace(state.ServerMessage) ? state.ServerStatus : state.ServerMessage));
        IEnumerable<SyncFolderState> folders = state.Folders;
        if (!string.IsNullOrWhiteSpace(focusPath))
        {
            List<SyncFolderState> matching = folders
                .Where(folder => SyncPath.IsSameOrInside(folder.CanonicalPath, focusPath) || SyncPath.IsSameOrInside(focusPath, folder.CanonicalPath))
                .ToList();
            if (matching.Count > 0)
                folders = matching;
        }
        if (!folders.Any())
            lines.Add("Энэ машин нэг ч синк хавтсанд холбогдоогүй байна.");
        foreach (SyncFolderState folder in folders)
        {
            lines.Add("");
            lines.Add($"{folder.ProjectCode} — {folder.ProjectName}");
            lines.Add($"   {folder.CanonicalPath}");
            lines.Add(folder.PresentHere
                ? $"   Энэ машинд: {folder.FileCount} файл, {folder.TotalBytes / (1024.0 * 1024.0):0.#} МБ · шалгасан {folder.IndexedAtUtc?.ToLocalTime():yyyy-MM-dd HH:mm}"
                : "   " + folder.LastError);
            foreach (SyncPeerState peer in folder.Peers)
            {
                string who = string.IsNullOrWhiteSpace(peer.AccountEmail) ? peer.DeviceKeyId[..Math.Min(8, peer.DeviceKeyId.Length)] : peer.AccountEmail;
                string compared = peer.ExchangedAtUtc is null
                    ? "индекс ирээгүй"
                    : $"ижил {peer.SameCount} · зөрүүтэй {peer.DifferentCount} · зөвхөн энд {peer.OnlyHereCount} · зөвхөн тэнд {peer.OnlyThereCount}";
                lines.Add($"   • {who}: {compared}" + (peer.LastError.Length > 0 ? $" ({peer.LastError})" : ""));
            }
        }
        lines.Add("");
        lines.Add($"Шинэчилсэн: {state.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        return string.Join(Environment.NewLine, lines);
    }
}
