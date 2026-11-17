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
    private readonly Button applyAlbumDefaultRolesButton =
        StudioWidgets.CreateButton("Бүх хуудсанд (шинэ хуудас ч өвлөнө)");

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
        applyAlbumDefaultRolesButton.Margin = new Thickness(0, 0, 0, 7);
        applyAlbumDefaultRolesButton.Click += (_, _) => ApplyAlbumDefaultRolesFromPanel();
        albumPageRolePanel.Children.Add(applyAlbumDefaultRolesButton);
        albumPageRolePanel.Children.Add(StudioWidgets.CreateHint(
            "Ctrl эсвэл Shift дарж ЕТ, ИДБ-ийн хэд хэдэн хуудсыг сонгоод нэг удаа хэрэглэнэ. " +
            "«Бүх хуудсанд» нь альбумын үндсэн role болж, дараа нэмэгдэх хуудас ч түүнийг өвлөнө. " +
            "Төсөлд нэгдсэн гишүүнийг жагсаалтаас сонгоно; нэгдээгүй хүний нэрийг (ж.нь М.Баасанжав) талбарт гараар бичнэ."));
        foreach (ComboBox box in new[] { albumPageArchitectBox, albumPagePreparedByBox, albumPageCheckedByBox })
        {
            // Editable: a person who has not joined the project (or is not on
            // the platform at all) is written by hand, as the owner's rule asks.
            box.IsEditable = true;
            box.IsTextSearchEnabled = true;
            box.SelectionChanged += (_, _) => RefreshAlbumPageRoleApplyButton();
            box.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((_, _) => RefreshAlbumPageRoleApplyButton()));
        }
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

        IReadOnlyList<IAlbumPageRoleOwner> selectedPages = GetSelectedAlbumPageRoleTargets();
        IReadOnlyList<IAlbumPageRoleOwner> targets = selectedPages.Count > 0
            ? selectedPages
            : [new AlbumDefaultRoleOwner(state.Album)];
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

        bool enabled = canEditProjectContent;
        albumPageArchitectBox.IsEnabled = enabled;
        albumPagePreparedByBox.IsEnabled = enabled;
        albumPageCheckedByBox.IsEnabled = enabled;
        albumPageRoleSummaryText.Text = selectedPages.Count switch
        {
            0 => "Хуудас сонгоогүй · альбумын үндсэн role (бүх хуудас, шинэ хуудас ч өвлөнө).",
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
        else if (!string.IsNullOrWhiteSpace(selection.ManualName))
        {
            selected = AlbumPageRoleParticipantChoice.Manual(selection.ManualName);
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
        bool anyChoice = new[]
        {
            ReadAlbumPageRoleChoice(albumPageArchitectBox),
            ReadAlbumPageRoleChoice(albumPagePreparedByBox),
            ReadAlbumPageRoleChoice(albumPageCheckedByBox),
        }.OfType<AlbumPageRoleParticipantChoice>().Any(choice => !choice.KeepCurrentValue);
        applyAlbumPageRolesButton.IsEnabled = CanEditProjectContent() && hasTargets && anyChoice;
        applyAlbumDefaultRolesButton.IsEnabled = CanEditProjectContent() && anyChoice;
    }

    private void ApplyAlbumDefaultRolesFromPanel()
    {
        if (!EnsureProjectContentPermission())
            return;
        ApplyAlbumDefaultRoles(
            ReadAlbumPageRoleChoice(albumPageArchitectBox),
            ReadAlbumPageRoleChoice(albumPagePreparedByBox),
            ReadAlbumPageRoleChoice(albumPageCheckedByBox));
    }

    /// <summary>
    /// «Бүх хуудсанд»: the choice becomes the album's default and every page
    /// gives up its own name for that role, so the whole album - and every page
    /// that arrives later - signs with the one set of names. The panel's button
    /// and the bridge's «album-roles pages=all» both end here.
    /// </summary>
    private int ApplyAlbumDefaultRoles(
        AlbumPageRoleParticipantChoice? architect,
        AlbumPageRoleParticipantChoice? preparedBy,
        AlbumPageRoleParticipantChoice? checkedBy)
    {
        IReadOnlyList<IAlbumPageRoleOwner> albumDefault = [new AlbumDefaultRoleOwner(state.Album)];
        IReadOnlyList<IAlbumPageRoleOwner> pages = GetAllAlbumPageRoleTargets();
        int changed = 0;
        foreach ((string role, AlbumPageRoleParticipantChoice? choice) in new[]
                 {
                     (AlbumPageRoleCodes.Architect, architect),
                     (AlbumPageRoleCodes.PreparedBy, preparedBy),
                     (AlbumPageRoleCodes.CheckedBy, checkedBy),
                 })
        {
            if (choice is null || choice.KeepCurrentValue)
                continue;
            changed += ApplyAlbumPageRoleChoice(albumDefault, role, choice);
            // Pages inherit: an own assignment would hide the album's name.
            changed += AlbumPageRoleAssignmentService.Apply(pages, role, null);
        }
        if (changed == 0)
        {
            SetStatus("Альбумын үндсэн role өөрчлөгдөөгүй.");
            return 0;
        }

        state.SaveProject();
        bindingAlbumPage = true;
        BindAlbumPageRoleControls(canEditProjectContent: true);
        bindingAlbumPage = false;
        UpdateAlbum(
            silent: false,
            statusPrefix: "Альбумын үндсэн role шинэчлэгдлээ (бүх хуудас)");
        return changed;
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

        ApplyAlbumPageRoles(
            targets,
            ReadAlbumPageRoleChoice(albumPageArchitectBox),
            ReadAlbumPageRoleChoice(albumPagePreparedByBox),
            ReadAlbumPageRoleChoice(albumPageCheckedByBox));
    }

    /// <summary>
    /// The one place a page role changes - the panel's button and the bridge's
    /// «album-roles» both end here, so the two can never disagree.
    /// </summary>
    private int ApplyAlbumPageRoles(
        IReadOnlyList<IAlbumPageRoleOwner> targets,
        AlbumPageRoleParticipantChoice? architect,
        AlbumPageRoleParticipantChoice? preparedBy,
        AlbumPageRoleParticipantChoice? checkedBy)
    {
        int changed = 0;
        changed += ApplyAlbumPageRoleChoice(targets, AlbumPageRoleCodes.Architect, architect);
        changed += ApplyAlbumPageRoleChoice(targets, AlbumPageRoleCodes.PreparedBy, preparedBy);
        changed += ApplyAlbumPageRoleChoice(targets, AlbumPageRoleCodes.CheckedBy, checkedBy);
        if (changed == 0)
        {
            SetStatus("Сонгосон хуудасны role өөрчлөгдөөгүй.");
            return 0;
        }

        state.SaveProject();
        bindingAlbumPage = true;
        BindAlbumPageRoleControls(canEditProjectContent: true);
        bindingAlbumPage = false;
        UpdateAlbum(
            silent: false,
            statusPrefix: $"{targets.Count} хуудасны role шинэчлэгдлээ");
        return changed;
    }

    /// <summary>
    /// What a role box holds: the picked item, or - when the text was typed and
    /// matches no item - a hand-written name.
    /// </summary>
    private static AlbumPageRoleParticipantChoice? ReadAlbumPageRoleChoice(ComboBox box)
    {
        var selected = box.SelectedItem as AlbumPageRoleParticipantChoice;
        string typed = (box.Text ?? "").Trim();
        if (selected is not null && string.Equals(selected.Label, typed, StringComparison.Ordinal))
            return selected;
        if (typed.Length == 0)
            return selected;
        AlbumPageRoleParticipantChoice? match =
            (box.ItemsSource as IEnumerable<AlbumPageRoleParticipantChoice> ?? [])
            .FirstOrDefault(choice => string.Equals(choice.Label, typed, StringComparison.Ordinal));
        return match ?? AlbumPageRoleParticipantChoice.Manual(typed);
    }

    /// <summary>
    /// Every page a role can be set on - the album's sheets and Studio's own
    /// generated pages other than the cover - whether or not it is selected.
    /// </summary>
    private IReadOnlyList<IAlbumPageRoleOwner> GetAllAlbumPageRoleTargets()
    {
        var result = new List<IAlbumPageRoleOwner>();
        result.AddRange(state.Album.Pages);
        result.AddRange(state.Album.Composition.Where(item =>
            item.Kind == AlbumCompositionKind.Generated &&
            item.GeneratedPageKind != AlbumGeneratedPageKind.Cover));
        return result;
    }

    /// <summary>
    /// A bridge value for one role: "inherit" follows the project's default, a
    /// team member's document name (Э.Мөнххолбоо), full name or e-mail picks
    /// that member, anything else is written by hand.
    /// </summary>
    private AlbumPageRoleParticipantChoice? ResolveAlbumPageRoleArgument(string? value)
    {
        if (value is null)
            return null;
        string text = value.Trim();
        if (text.Length == 0 || text.Equals("inherit", StringComparison.OrdinalIgnoreCase))
            return AlbumPageRoleParticipantChoice.Inherit();
        AlbumPageRoleParticipantChoice? member = BuildAlbumPageRoleParticipantChoices()
            .Where(choice => choice.Member is not null)
            .FirstOrDefault(choice =>
                string.Equals(choice.DocumentName, text, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(choice.Member!.FullName?.Trim(), text, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(choice.Member!.Email?.Trim(), text, StringComparison.OrdinalIgnoreCase));
        return member ?? AlbumPageRoleParticipantChoice.Manual(text);
    }

    /// <summary>
    /// «album-roles»: the panel's «Сонгосон хуудсуудад хэрэглэх», over every
    /// page (or the selected ones). Without role arguments it only reports.
    /// </summary>
    private StudioBridgeResponse BridgeAlbumRoles(StudioBridgeRequest request)
    {
        string scope = (request.Arg("pages") ?? "all").Trim().ToLowerInvariant();
        if (scope is not ("all" or "selected"))
            return StudioBridgeProtocol.Refuse(request.Id, "bad_request", "pages=all|selected заана уу.");
        IReadOnlyList<IAlbumPageRoleOwner> targets = scope == "selected"
            ? GetSelectedAlbumPageRoleTargets()
            : GetAllAlbumPageRoleTargets();

        AlbumPageRoleParticipantChoice? architect = ResolveAlbumPageRoleArgument(request.Arg("architect"));
        AlbumPageRoleParticipantChoice? preparedBy = ResolveAlbumPageRoleArgument(request.Arg("prepared"));
        AlbumPageRoleParticipantChoice? checkedBy = ResolveAlbumPageRoleArgument(request.Arg("checked"));
        int changed = 0;
        if (architect is not null || preparedBy is not null || checkedBy is not null)
        {
            if (!EnsureProjectContentPermission())
                return StudioBridgeProtocol.Refuse(request.Id, "no_permission", "Төслийн агуулга засах эрх алга.");
            if (scope == "all")
            {
                changed = ApplyAlbumDefaultRoles(architect, preparedBy, checkedBy);
            }
            else
            {
                if (targets.Count == 0)
                    return StudioBridgeProtocol.Refuse(request.Id, "no_pages", "Role тохируулах хуудас алга.");
                changed = ApplyAlbumPageRoles(targets, architect, preparedBy, checkedBy);
            }
        }

        List<ProjectParticipant> participants = state.Project.Foundation.DesignCompany.Members
            .Select(member => new ProjectParticipant
            {
                ParticipantId = member.Id ?? "",
                FamilyName = member.FamilyName ?? "",
                GivenName = member.GivenName ?? "",
                FullName = member.FullName ?? "",
            })
            .ToList();
        var albumDefault = new AlbumDefaultRoleOwner(state.Album);
        string Own(IAlbumPageRoleOwner owner, string role) =>
            AlbumPageRoleAssignmentResolver.ResolveDocumentName(owner.RoleAssignments, role, participants) ?? "";
        // What the page signs with: its own name, else the album's default.
        string Name(IAlbumPageRoleOwner owner, string role)
        {
            string own = Own(owner, role);
            return own.Length > 0 ? own : Own(albumDefault, role);
        }
        return StudioBridgeProtocol.Accept(
            request.Id,
            new
            {
                pages = targets.Count,
                changed,
                albumDefault = new
                {
                    architect = Own(albumDefault, AlbumPageRoleCodes.Architect),
                    preparedBy = Own(albumDefault, AlbumPageRoleCodes.PreparedBy),
                    checkedBy = Own(albumDefault, AlbumPageRoleCodes.CheckedBy),
                },
                roles = targets.Select(owner => new
                {
                    page = owner switch
                    {
                        AlbumPageDefinition page => page.SheetKey,
                        AlbumCompositionItem item => string.IsNullOrWhiteSpace(item.Title) ? item.Id : item.Title,
                        _ => "",
                    },
                    architect = Name(owner, AlbumPageRoleCodes.Architect),
                    preparedBy = Name(owner, AlbumPageRoleCodes.PreparedBy),
                    checkedBy = Name(owner, AlbumPageRoleCodes.CheckedBy),
                }),
            },
            changed > 0 ? statusText.Text : $"{targets.Count} хуудас.");
    }

    private static int ApplyAlbumPageRoleChoice(
        IReadOnlyList<IAlbumPageRoleOwner> targets,
        string roleCode,
        AlbumPageRoleParticipantChoice? choice)
    {
        if (choice is null || choice.KeepCurrentValue)
            return 0;
        if (choice.Member is null && !string.IsNullOrWhiteSpace(choice.ManualName))
            return AlbumPageRoleAssignmentService.ApplyManual(targets, roleCode, choice.ManualName);
        return AlbumPageRoleAssignmentService.Apply(targets, roleCode, choice.Member);
    }

    private sealed record AlbumPageRoleParticipantChoice(
        string ParticipantId,
        string Label,
        ProjectMember? Member,
        bool KeepCurrentValue,
        string ManualName = "",
        string DocumentName = "")
    {
        /// <summary>A person who is not a project member, named as typed.</summary>
        public static AlbumPageRoleParticipantChoice Manual(string name) => new(
            "",
            name.Trim(),
            null,
            false,
            ManualName: name.Trim(),
            DocumentName: name.Trim());

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
                false,
                DocumentName: name);
        }

        public override string ToString() => Label;
    }
}
