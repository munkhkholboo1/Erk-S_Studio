using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using ErkS.Sync.Core;

namespace ErkS.Studio;

/// <summary>
/// The person's choice behind an Explorer verb: which project a folder becomes the sync folder
/// of, or which registered sync folder it joins. A window of its own - not a message box - so a
/// verb that arrives over the bridge still waits for the person (task 1.1 step 4–5).
/// </summary>
internal sealed class SyncFolderDialog : Window
{
    private readonly ComboBox _choices = new() { Margin = new Thickness(0, 6, 0, 12), MinWidth = 520 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };
    private readonly Button _ok;
    private readonly Func<object, Task<StudioSyncOutcome>> _act;

    private SyncFolderDialog(string title, string folderPath, string prompt, string okText, IReadOnlyList<(string Label, object Value)> choices, int selected, Func<object, Task<StudioSyncOutcome>> act, string? emptyReason = null)
    {
        StudioTheme.Apply(this);
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        _act = act;

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "Хавтас", Foreground = StudioTheme.MutedTextBrush });
        stack.Children.Add(new TextBlock { Text = folderPath, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 12), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 });
        stack.Children.Add(new TextBlock { Text = prompt, Foreground = StudioTheme.MutedTextBrush });
        foreach ((string label, object value) in choices)
            _choices.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        _choices.SelectedIndex = choices.Count == 0 ? -1 : Math.Clamp(selected, 0, choices.Count - 1);
        stack.Children.Add(_choices);
        stack.Children.Add(_message);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _ok = StudioWidgets.CreatePrimaryButton(okText);
        _ok.IsEnabled = choices.Count > 0;
        _ok.Click += async (_, _) =>
        {
            if (Outcome is { Ok: true })
                Close();
            else
                await ActAsync();
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(_ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = stack;
        if (choices.Count == 0)
        {
            _message.Text = emptyReason ?? "Сонгох зүйл алга.";
            _message.Foreground = StudioTheme.DangerBrush;
            _message.MaxWidth = 560;
        }
    }

    public StudioSyncOutcome? Outcome { get; private set; }

    private async Task ActAsync()
    {
        if (_choices.SelectedItem is not ComboBoxItem { Tag: { } value })
            return;
        _ok.IsEnabled = false;
        _message.Foreground = StudioTheme.MutedTextBrush;
        _message.Text = "Түр хүлээнэ үү…";
        StudioSyncOutcome outcome = await _act(value);
        Outcome = outcome;
        _message.Text = outcome.Message;
        _message.Foreground = outcome.Ok ? StudioTheme.AccentBrush : StudioTheme.DangerBrush;
        if (!outcome.Ok)
        {
            _ok.IsEnabled = true;
            return;
        }
        _ok.Content = "Хаах";
        _ok.IsEnabled = true;
    }

    /// <summary>«Erk-S Sync фолдер болгох»: pick the project; the open one first.</summary>
    public static SyncFolderDialog ForMake(string folderPath, IReadOnlyList<ProjectCatalogItem> projects, string? openProjectPath, Func<ProjectCatalogItem, Task<StudioSyncOutcome>> make, string? emptyReason = null)
    {
        List<(string, object)> choices = projects
            .Select(project => ($"{project.ProjectCode} — {project.DisplayName}", (object)project))
            .ToList();
        int selected = projects.ToList().FindIndex(project =>
            openProjectPath is not null &&
            string.Equals(System.IO.Path.GetFullPath(project.ProjectPath), System.IO.Path.GetFullPath(openProjectPath), StringComparison.OrdinalIgnoreCase));
        return new SyncFolderDialog(
            "Erk-S Sync фолдер болгох",
            folderPath,
            "Аль Studio төслийн синк хавтас болох вэ? (Нэг хавтас = нэг төсөл, гишүүд = төслийн баг.)",
            "Синк хавтас болгох",
            choices,
            Math.Max(0, selected),
            value => make((ProjectCatalogItem)value),
            emptyReason);
    }

    /// <summary>«Erk-S Sync фолдерт холбох»: pick the registered sync folder; the one at this path first.</summary>
    public static SyncFolderDialog ForJoin(string folderPath, IReadOnlyList<SyncServerFolder> folders, Func<SyncServerFolder, Task<StudioSyncOutcome>> join)
    {
        List<(string, object)> choices = folders
            .Select(folder => ($"{folder.ProjectCode} — {folder.CanonicalPath}", (object)folder))
            .ToList();
        int selected = folders.ToList().FindIndex(folder => SyncPath.Same(folder.CanonicalPath, folderPath));
        return new SyncFolderDialog(
            "Erk-S Sync фолдерт холбох",
            folderPath,
            "Таны багийн аль төслийн синк хавтсанд холбох вэ? Хавтасны зам бүртгэлтэй замтай яг ижил байх ёстой.",
            "Холбох",
            choices,
            Math.Max(0, selected),
            value => join((SyncServerFolder)value));
    }
}
