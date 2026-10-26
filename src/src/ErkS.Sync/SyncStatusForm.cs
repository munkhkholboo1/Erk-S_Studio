using ErkS.Sync.Core;

namespace ErkS.Sync;

/// <summary>
/// «Erk-S Sync — төлөв»: the summary first, one row per project (owner, 2026-10-26: «синк дээрх
/// мэдээллийг цомхон ойлгомжтой хураангуй болгоорой. дэлгэрэнгүй харж болно. гэхдээ эхлээд
/// хураангуйгаар хардаг байна»); the selected project's details below - what is coming, what needs
/// a person, shared files, members. The members' raw counts sit under «Техникийн мэдээлэл».
/// Counts only; the file list lives in Studio's «Erk-S Sync» section.
/// </summary>
internal sealed class SyncStatusForm : Form
{
    private readonly SyncState _state;
    private readonly IReadOnlyList<SyncProjectSummary> _projects;
    private readonly ListView _list;
    private readonly TextBox _details;
    private readonly CheckBox _technical;

    public SyncStatusForm(SyncState state, string? focusPath)
    {
        _state = state;
        _projects = SyncSummary.Projects(state);
        Text = "Erk-S Sync — төлөв";
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var line = new Label
        {
            Text = SyncSummary.Line(_projects, state.ServerStatus) +
                (state.ServerStatus is "ok" or "" ? "" : " — " + (string.IsNullOrWhiteSpace(state.ServerMessage) ? state.ServerStatus : state.ServerMessage)),
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(10, 8, 10, 0),
            Font = new Font("Segoe UI Semibold", 10.5f),
        };
        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Dock = DockStyle.Top,
            Height = 150,
            BorderStyle = BorderStyle.None,
        };
        _list.Columns.Add("Төлөв", 190);
        _list.Columns.Add("Төсөл", 320);
        _list.Columns.Add("Сүүлд", 170);
        foreach (SyncProjectSummary project in _projects)
        {
            var item = new ListViewItem(project.StateWords) { Tag = project };
            item.SubItems.Add(string.IsNullOrWhiteSpace(project.ProjectName) ? project.ProjectCode : $"{project.ProjectCode} — {project.ProjectName}");
            item.SubItems.Add(project.LastSyncedAtUtc?.ToLocalTime().ToString("MM/dd HH:mm") ?? "");
            _list.Items.Add(item);
        }
        _details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
        };
        _technical = new CheckBox { Text = "Техникийн мэдээлэл", Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(10, 0, 0, 0) };
        var close = new Button { Text = "Хаах", Dock = DockStyle.Bottom, Height = 34 };
        close.Click += (_, _) => Close();
        _list.SelectedIndexChanged += (_, _) => ShowDetails();
        _technical.CheckedChanged += (_, _) => ShowDetails();

        Controls.Add(_details);
        Controls.Add(_list);
        Controls.Add(line);
        Controls.Add(_technical);
        Controls.Add(close);

        if (_list.Items.Count == 0)
        {
            _details.Text = "Энэ машин нэг ч синк хавтсанд холбогдоогүй байна.";
            return;
        }
        int focus = 0;
        if (!string.IsNullOrWhiteSpace(focusPath))
        {
            int found = state.Folders.ToList().FindIndex(folder =>
                SyncPath.IsSameOrInside(folder.CanonicalPath, focusPath) || SyncPath.IsSameOrInside(focusPath, folder.CanonicalPath));
            if (found >= 0)
                focus = found;
        }
        _list.Items[focus].Selected = true;
        ShowDetails();
    }

    private void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not SyncProjectSummary project)
            return;
        SyncFolderState? folder = _state.Folders.FirstOrDefault(item => item.FolderId == project.FolderId);
        if (folder is null)
            return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lines = new List<string>
        {
            $"{project.StateWords} · {folder.CanonicalPath}",
            folder.PresentHere
                ? $"Энэ машинд: {folder.FileCount} файл, {Mb(folder.TotalBytes)} · шалгасан {folder.IndexedAtUtc?.ToLocalTime():MM/dd HH:mm}"
                : folder.LastError,
        };

        // Coming in / going out now.
        foreach (SyncPeerState peer in folder.Peers.Where(peer => peer.Transferring))
        {
            lines.Add($"Ирж байна: {Mb(peer.TransferDoneBytes)} / {Mb(peer.TransferTotalBytes)} · {peer.TransferFilesDone}/{peer.TransferFiles} файл" +
                (peer.CurrentFile.Length > 0 ? $" · одоо {peer.CurrentFile}" : ""));
        }
        if (folder.Uploading)
            lines.Add($"Серверт илгээж байна: {Mb(folder.UploadedBytes)} / {Mb(folder.UploadPendingBytes)}");
        int needed = folder.Peers.Where(peer => SyncInStep.IsOnline(peer, now)).Sum(peer => peer.NeededFiles);
        if (needed > 0 && !folder.Peers.Any(peer => peer.Transferring))
            lines.Add($"Энд ирэх: {needed} файл ({Mb(folder.Peers.Where(peer => SyncInStep.IsOnline(peer, now)).Sum(peer => peer.NeededBytes))})");

        // What waits for a person.
        var attention = new List<string>();
        if (folder.PresentHere && folder.LastError.Length > 0)
            attention.Add(folder.LastError);
        if (folder.RedactionsUnsent > 0)
            attention.Add($"Цэвэрлэлтийн бүртгэл серверт очоогүй: {folder.RedactionsUnsent}" + (folder.RedactionError.Length > 0 ? $" — {folder.RedactionError}" : ""));
        if (folder.ConflictCount > 0)
            attention.Add($"Зөрүүтэй файл {folder.ConflictCount} — Studio-гийн «Erk-S Sync» хэсэгт «Тэндхийг авах» / «Минийхийг үлдээх».");
        foreach (SyncCollabStatus collab in folder.Collabs)
        {
            if (collab.LastError.Length > 0)
                attention.Add("Хамтын файл: " + collab.LastError);
            else if (collab.CentralEdited)
                attention.Add("Хамтын файл: төв файл шууд засагдсан.");
            else if (collab.Writers.Any(writer => writer.Gap))
                attention.Add("Хамтын файл: багцын дугаар дутуу — ирэхийг хүлээж байна.");
        }
        if (attention.Count > 0)
        {
            lines.Add("");
            lines.Add("Анхаарах:");
            lines.AddRange(attention.Select(text => "  ⚠ " + text));
        }

        // Shared CGA files.
        if (folder.Collabs.Count > 0)
        {
            lines.Add("");
            lines.Add("Хамтын файлууд:");
            foreach (SyncCollabStatus collab in folder.Collabs)
                lines.Add($"  {collab.FileId[..Math.Min(8, collab.FileId.Length)]} · checkpoint {collab.Checkpoint} · энд хүлээгдэж буй {collab.Waiting} · бусдад очоогүй {collab.PendingOut}");
        }

        // Members: online / offline - an offline member never counts in the percent.
        if (folder.Peers.Count > 0)
        {
            lines.Add("");
            lines.Add("Гишүүд:");
            foreach (SyncPeerState peer in folder.Peers)
            {
                string state = peer.Lagging ? "хоцорсон — шинэчлэгдэж, онлайн болох хүртэл түүнээс файл авахгүй"
                    : peer.ExchangedAtUtc is null ? "холбогдохыг хүлээж байна"
                    : SyncInStep.IsOnline(peer, now) ? "онлайн"
                    : peer.ServerLastSeenAtUtc is { } seen ? $"офлайн ({seen.ToLocalTime():MM/dd HH:mm}-өөс)" : "офлайн";
                lines.Add($"  {Name(peer)} — {state}");
                if (_technical.Checked && peer.ExchangedAtUtc is not null)
                {
                    lines.Add($"      ижил {peer.SameCount} · зөрүүтэй {peer.DifferentCount} · зөвхөн энд {peer.OnlyHereCount} · зөвхөн тэнд {peer.OnlyThereCount}" +
                        $" · энд ирэх {peer.NeededFiles} ({Mb(peer.NeededBytes)})" + (peer.Route.Length > 0 ? $" · {peer.Route}" : ""));
                    if (peer.LastError.Length > 0)
                        lines.Add("      " + peer.LastError);
                }
            }
        }
        lines.Add("");
        lines.Add($"Шинэчилсэн: {_state.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        _details.Text = string.Join(Environment.NewLine, lines);
    }

    private static string Name(SyncPeerState peer)
    {
        int at = peer.AccountEmail.IndexOf('@');
        return at > 0 ? peer.AccountEmail[..at] : "төхөөрөмж " + peer.DeviceKeyId[..Math.Min(8, peer.DeviceKeyId.Length)];
    }

    private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.#} МБ";
}
