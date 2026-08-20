using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly StackPanel albumPageRolePanel = new();
    private readonly ComboBox albumPageArchitectBox = new();
    private readonly ComboBox albumPagePreparedByBox = new();
    private readonly ComboBox albumPageCheckedByBox = new();
    private readonly TextBlock albumPageRoleSummaryText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 3, 0, 7),
    };
    private readonly Button applyAlbumPageRolesButton =
        StudioWidgets.CreatePrimaryButton("Сонгосон хуудсуудад хэрэглэх");

    private UIElement BuildAlbumPageRolePanel()
    {
        albumPageRolePanel.Children.Add(StudioWidgets.CreateSectionHeader("Хуудасны role"));
        albumPageRolePanel.Children.Add(albumPageRoleSummaryText);
        albumPageRolePanel.Children.Add(
            StudioWidgets.CreateFormRow("Архитектор", albumPageArchitectBox, 92));
        albumPageRolePanel.Children.Add(
            StudioWidgets.CreateFormRow("Гүйцэтгэсэн", albumPagePreparedByBox, 92));
        albumPageRolePanel.Children.Add(
            StudioWidgets.CreateFormRow("Шалгасан", albumPageCheckedByBox, 92));
        applyAlbumPageRolesButton.Margin = new Thickness(0, 6, 0, 7);
        applyAlbumPageRolesButton.Click += (_, _) => ApplySelectedAlbumPageRoles();
        albumPageRolePanel.Children.Add(applyAlbumPageRolesButton);
        albumPageRolePanel.Children.Add(StudioWidgets.CreateHint(
            "Ctrl эсвэл Shift дарж ЕТ, ИДБ-ийн хэд хэдэн хуудсыг сонгоод нэг удаа хэрэглэнэ. " +
            "Нэрийг гараар бичихгүй; зөвхөн төслийн багийн гишүүдээс сонгоно."));
        albumPageArchitectBox.SelectionChanged += (_, _) => RefreshAlbumPageRoleApplyButton();
        albumPagePreparedByBox.SelectionChanged += (_, _) => RefreshAlbumPageRoleApplyButton();
        albumPageCheckedByBox.SelectionChanged += (_, _) => RefreshAlbumPageRoleApplyButton();
        return albumPageRolePanel;
    }

    private void BindAlbumPageRoleControls(bool canEditProjectContent)
    {
        bool supported = string.Equals(
            state.Album.TemplateId,
            UrbanPlanningAlbumTemplate.PartialPlanTemplateId,
            StringComparison.OrdinalIgnoreCase);
        albumPageRolePanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        if (!supported)
            return;

        IReadOnlyList<IAlbumPageRoleOwner> targets = GetSelectedAlbumPageRoleTargets();
        List<AlbumPageRoleParticipantChoice> team = BuildAlbumPageRoleParticipantChoices();
        BindAlbumPageRoleChoice(
            albumPageArchitectBox,
            AlbumPageRoleCodes.Architect,
            targets,
            team);
        BindAlbumPageRoleChoice(
            albumPagePreparedByBox,
            AlbumPageRoleCodes.PreparedBy,
            targets,
            team);
        BindAlbumPageRoleChoice(
            albumPageCheckedByBox,
            AlbumPageRoleCodes.CheckedBy,
            targets,
            team);

        bool enabled = canEditProjectContent && targets.Count > 0;
        albumPageArchitectBox.IsEnabled = enabled;
        albumPagePreparedByBox.IsEnabled = enabled;
        albumPageCheckedByBox.IsEnabled = enabled;
        albumPageRoleSummaryText.Text = targets.Count switch
        {
            0 => "Role тохируулах хуудас сонгоно уу.",
            1 => "1 хуудас сонгосон · төслийн багаас хүн сонгоно.",
            _ => $"{targets.Count} хуудас сонгосон · сонголт бүх хуудсанд зэрэг үйлчилнэ.",
        };
        RefreshAlbumPageRoleApplyButton();
    }

    private IReadOnlyList<IAlbumPageRoleOwner> GetSelectedAlbumPageRoleTargets()
    {
        var result = new List<IAlbumPageRoleOwner>();
        var seen = new HashSet<IAlbumPageRoleOwner>(ReferenceEqualityComparer.Instance);
        foreach (AlbumPageWorkspaceItem item in albumPagesWorkspaceList.SelectedItems
                     .OfType<AlbumPageWorkspaceItem>()
                     .Where(CanAssignAlbumPageRoles))
        {
            if (item.RoleOwner is IAlbumPageRoleOwner owner && seen.Add(owner))
                result.Add(owner);
        }
        return result;
    }

    private static bool CanAssignAlbumPageRoles(AlbumPageWorkspaceItem item) =>
        item.Page is not null ||
        item.Component is
        {
            Kind: AlbumCompositionKind.Generated,
            GeneratedPageKind: not AlbumGeneratedPageKind.Cover,
        };

    private List<AlbumPageRoleParticipantChoice> BuildAlbumPageRoleParticipantChoices()
    {
        var choices = new List<AlbumPageRoleParticipantChoice>
        {
            AlbumPageRoleParticipantChoice.Inherit(),
        };
        choices.AddRange(state.Project.Foundation.DesignCompany.Members
            .Where(member =>
                !string.IsNullOrWhiteSpace(member.Id) &&
                (!string.IsNullOrWhiteSpace(member.FullName) ||
                 !string.IsNullOrWhiteSpace(member.FamilyName) ||
                 !string.IsNullOrWhiteSpace(member.GivenName)))
            .GroupBy(member => member.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(member => member.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(member => AlbumPageRoleParticipantChoice.TeamMember(member)));
        return choices;
    }

    private static void BindAlbumPageRoleChoice(
        ComboBox comboBox,
        string roleCode,
        IReadOnlyList<IAlbumPageRoleOwner> targets,
        IReadOnlyList<AlbumPageRoleParticipantChoice> team)
    {
        StudioAlbumPageRoleSelectionState selection =
            StudioAlbumPageRoleSelection.Resolve(targets, roleCode);
        var choices = team.ToList();
        AlbumPageRoleParticipantChoice selected;
        if (selection.IsMixed)
        {
            selected = AlbumPageRoleParticipantChoice.KeepExisting(
                "Олон утгатай - өөрчлөхгүй");
            choices.Insert(0, selected);
        }
        else if (!string.IsNullOrWhiteSpace(selection.ParticipantId))
        {
            selected = choices.FirstOrDefault(choice => string.Equals(
                           choice.ParticipantId,
                           selection.ParticipantId,
                           StringComparison.OrdinalIgnoreCase))
                       ?? AlbumPageRoleParticipantChoice.KeepExisting(
                           "Багаас хасагдсан сонголтыг хадгалах");
            if (!choices.Contains(selected))
                choices.Insert(0, selected);
        }
        else
        {
            selected = choices[0];
        }

        comboBox.ItemsSource = choices;
        comboBox.SelectedItem = selected;
    }

    private void RefreshAlbumPageRoleApplyButton()
    {
        bool hasTargets = GetSelectedAlbumPageRoleTargets().Count > 0;
        applyAlbumPageRolesButton.IsEnabled =
            CanEditProjectContent() &&
            hasTargets &&
            new[]
            {
                albumPageArchitectBox.SelectedItem,
                albumPagePreparedByBox.SelectedItem,
                albumPageCheckedByBox.SelectedItem,
            }.OfType<AlbumPageRoleParticipantChoice>().Any(choice => !choice.KeepCurrentValue);
    }

    private void ApplySelectedAlbumPageRoles()
    {
        if (!EnsureProjectContentPermission())
            return;

        IReadOnlyList<IAlbumPageRoleOwner> targets = GetSelectedAlbumPageRoleTargets();
        if (targets.Count == 0)
        {
            SetStatus("Role тохируулах хуудас сонгоно уу.");
            return;
        }

        int changed = 0;
        changed += ApplyAlbumPageRoleChoice(
            targets,
            AlbumPageRoleCodes.Architect,
            albumPageArchitectBox.SelectedItem as AlbumPageRoleParticipantChoice);
        changed += ApplyAlbumPageRoleChoice(
            targets,
            AlbumPageRoleCodes.PreparedBy,
            albumPagePreparedByBox.SelectedItem as AlbumPageRoleParticipantChoice);
        changed += ApplyAlbumPageRoleChoice(
            targets,
            AlbumPageRoleCodes.CheckedBy,
            albumPageCheckedByBox.SelectedItem as AlbumPageRoleParticipantChoice);
        if (changed == 0)
        {
            SetStatus("Сонгосон хуудасны role өөрчлөгдөөгүй.");
            return;
        }

        state.SaveProject();
        bindingAlbumPage = true;
        BindAlbumPageRoleControls(canEditProjectContent: true);
        bindingAlbumPage = false;
        UpdateAlbum(
            silent: false,
            statusPrefix: $"{targets.Count} хуудасны role шинэчлэгдлээ");
    }

    private static int ApplyAlbumPageRoleChoice(
        IReadOnlyList<IAlbumPageRoleOwner> targets,
        string roleCode,
        AlbumPageRoleParticipantChoice? choice)
    {
        if (choice is null || choice.KeepCurrentValue)
            return 0;
        return AlbumPageRoleAssignmentService.Apply(targets, roleCode, choice.Member);
    }

    private sealed record AlbumPageRoleParticipantChoice(
        string ParticipantId,
        string Label,
        ProjectMember? Member,
        bool KeepCurrentValue)
    {
        public static AlbumPageRoleParticipantChoice Inherit() => new(
            "",
            "Төслийн үндсэн role-ийг дагах",
            null,
            false);

        public static AlbumPageRoleParticipantChoice KeepExisting(string label) => new(
            "",
            label,
            null,
            true);

        public static AlbumPageRoleParticipantChoice TeamMember(ProjectMember member)
        {
            string name = MongolianPersonNameFormatter.ForDocument(
                member.FamilyName,
                member.GivenName,
                member.FullName);
            string roles = string.Join(", ", member.Roles.Where(role =>
                !string.IsNullOrWhiteSpace(role)));
            return new AlbumPageRoleParticipantChoice(
                member.Id,
                string.IsNullOrWhiteSpace(roles) ? name : $"{name} · {roles}",
                member,
                false);
        }

        public override string ToString() => Label;
    }
}
