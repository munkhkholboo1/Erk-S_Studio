using System.Windows.Shapes;
using ErkS.Platform.Core.ProjectTypes;
using ErkS.Platform.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// What a project looks like the moment it opens. A form of empty boxes reads
/// as a settings dialog; this says which project this is - its name, its stage,
/// the practice that draws it, the cover it has reached - before it offers
/// anything to fill in.
/// </summary>
internal sealed partial class ShellView
{
    /// <summary>
    /// The box the album's first page is fitted into, whatever shape it is.
    /// </summary>
    /// <remarks>
    /// This used to be a width plus a hard-coded A4 height, and the page was
    /// painted with <see cref="Stretch.UniformToFill"/> - which fills the box
    /// and cuts off whatever does not fit. An album page is not always A4
    /// portrait, so a landscape cover lost its sides. Now the page is fitted
    /// whole inside these bounds and the frame takes the page's own
    /// proportions.
    /// </remarks>
    private const double ProjectCoverMaxWidth = 230d;
    private const double ProjectCoverMaxHeight = 330d;

    /// <summary>Shape of the empty placeholder, before any page is drawn.</summary>
    private const double ProjectCoverWidth = 200d;

    private readonly Border projectOverviewBanner = new()
    {
        Background = StudioTheme.PanelBrush,
        BorderBrush = StudioTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(26, 22, 22, 22),
        Margin = new Thickness(0, 0, 0, 14),
    };
    private readonly Border projectOverviewStageBadge = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(64, 91, 156, 246)),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(11, 4, 11, 4),
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 14),
    };
    private readonly TextBlock projectOverviewStageText = new()
    {
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = StudioTheme.AccentSoftBrush,
    };
    private readonly TextBlock projectOverviewName = new()
    {
        FontSize = 29,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        LineHeight = 36,
        MaxWidth = 620,
    };
    private readonly TextBlock projectOverviewIdentity = new()
    {
        FontSize = 12.5,
        Foreground = StudioTheme.MutedTextBrush,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 620,
        Margin = new Thickness(0, 9, 0, 0),
    };
    private readonly Image projectOverviewCompanyLogo = new()
    {
        Stretch = Stretch.Uniform,
        Width = 78,
        Height = 78,
    };
    private readonly Border projectOverviewCompanyCrest = new()
    {
        Width = 78,
        Height = 78,
        CornerRadius = new CornerRadius(39),
        Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
    };
    private readonly TextBlock projectOverviewCompanyMonogram = new()
    {
        FontSize = 27,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock projectOverviewCompanyName = new()
    {
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 520,
    };
    private readonly TextBlock projectOverviewCompanyRole = new()
    {
        FontSize = 11.5,
        Foreground = StudioTheme.MutedTextBrush,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 5, 0, 0),
    };
    private readonly Border projectOverviewCover = new()
    {
        Width = ProjectCoverWidth,
        Background = StudioTheme.InputBrush,
        BorderBrush = StudioTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        VerticalAlignment = VerticalAlignment.Center,
        ClipToBounds = true,
    };
    private readonly WrapPanel projectOverviewFacts = new() { Margin = new Thickness(0, 0, 0, 16) };
    private readonly WrapPanel projectTeamPanel = new();
    private readonly TextBlock projectTeamSummary = new()
    {
        FontSize = 12,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 3, 0, 0),
    };
    private readonly Dictionary<string, Image> teamAvatarTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (TextBlock Target, MemberRow Member)> teamRoleTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (StackPanel Target, MemberRow Member)> teamPresenceTargets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource> teamAvatarCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> projectRoleLabels =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel projectRecordView = new();
    private TabControl? foundationEditTabs;
    private UIElement? projectRecordHost;

    private string overviewCoverPdfPath = "";

    /// <summary>
    /// The project's own masthead: what it is on the left, the cover its album
    /// has reached on the right.
    /// </summary>
    private UIElement BuildProjectOverview()
    {
        var stack = new StackPanel();

        var layout = new DockPanel();

        // A4 proportions until a page is actually drawn. Once one is, the
        // frame takes that page's shape instead - see ApplyProjectOverviewCover.
        // The placeholder cannot match a shape nobody has measured yet.
        projectOverviewCover.Height = ProjectCoverWidth * 297d / 210d;
        var coverLayers = new Grid();
        coverLayers.Children.Add(new Image
        {
            Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")),
            Width = 40,
            Height = 40,
            Opacity = 0.16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        projectOverviewCover.Child = coverLayers;
        projectOverviewCover.Margin = new Thickness(24, 0, 0, 0);
        DockPanel.SetDock(projectOverviewCover, Dock.Right);
        layout.Children.Add(projectOverviewCover);

        // Composed like the title page of an album: whose work this is at the
        // top, then what the work is. Centred in the space the cover leaves.
        var words = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var crest = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        projectOverviewCompanyCrest.Child = projectOverviewCompanyMonogram;
        crest.Children.Add(projectOverviewCompanyCrest);
        crest.Children.Add(projectOverviewCompanyLogo);
        words.Children.Add(crest);
        words.Children.Add(projectOverviewCompanyName);
        words.Children.Add(projectOverviewCompanyRole);
        words.Children.Add(new Border
        {
            Height = 1,
            Width = 72,
            Background = StudioTheme.BorderBrush,
            Margin = new Thickness(0, 20, 0, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        projectOverviewStageBadge.Child = projectOverviewStageText;
        words.Children.Add(projectOverviewStageBadge);
        words.Children.Add(projectOverviewName);
        words.Children.Add(projectOverviewIdentity);
        layout.Children.Add(words);

        projectOverviewBanner.Child = layout;
        stack.Children.Add(projectOverviewBanner);
        stack.Children.Add(projectOverviewFacts);
        stack.Children.Add(BuildProjectTeamSection());
        return stack;
    }

    /// <summary>
    /// Who is on this project. A project is people before it is files, so the
    /// team is named on the page the project opens on rather than only behind
    /// the Оролцогчид tab.
    /// </summary>
    private UIElement BuildProjectTeamSection()
    {
        var section = new StackPanel { Margin = new Thickness(0, 4, 0, 16) };

        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        Button all = StudioWidgets.CreateGlyphTextButton("", "Оролцогчид");
        all.VerticalAlignment = VerticalAlignment.Center;
        all.Click += (_, _) => SelectPage(StudioPage.Participants);
        DockPanel.SetDock(all, Dock.Right);
        heading.Children.Add(all);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = "Төслийн баг",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
        });
        words.Children.Add(projectTeamSummary);
        heading.Children.Add(words);
        section.Children.Add(heading);
        section.Children.Add(projectTeamPanel);
        return section;
    }

    /// <summary>
    /// Rebuilds the team strip. The names, roles and states come from the
    /// project itself; the photographs are asked of Cloud ERA and filled in
    /// when they arrive, so a member is shown at once with their initials
    /// rather than waiting on the network.
    /// </summary>
    private void RefreshProjectTeamOverview()
    {
        projectTeamPanel.Children.Clear();
        // The cards are about to be rebuilt, so the elements these point at are
        // gone. Leaving them would refill panels no longer on screen.
        teamPresenceTargets.Clear();
        if (!state.HasOpenProject)
        {
            projectTeamSummary.Text = "";
            return;
        }

        IReadOnlyList<MemberRow> members = ActiveProjectMemberRows();
        projectTeamSummary.Text = members.Count == 0
            ? "Багийн гишүүн бүртгэгдээгүй байна"
            : $"{members.Count} гишүүн";
        foreach (MemberRow member in members)
            projectTeamPanel.Children.Add(BuildProjectTeamCard(member));

        _ = LoadProjectTeamDetailAsync();
    }

    private UIElement BuildProjectTeamCard(MemberRow member)
    {
        var card = new Border
        {
            Width = 268,
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 12, 12),
            ToolTip = member.Email,
        };
        var layout = new DockPanel();

        var avatar = new Grid
        {
            Width = 44,
            Height = 44,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        avatar.Children.Add(new Ellipse
        {
            Width = 44,
            Height = 44,
            Fill = new SolidColorBrush(Color.FromRgb(44, 105, 83)),
        });
        avatar.Children.Add(new TextBlock
        {
            Text = StudioOrganizationCrest.Initials(member.Name),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var photo = new Image
        {
            Width = 44,
            Height = 44,
            Stretch = Stretch.UniformToFill,
            Clip = new EllipseGeometry(new Rect(0, 0, 44, 44)),
            Visibility = Visibility.Collapsed,
        };
        avatar.Children.Add(photo);
        teamAvatarTargets[member.Email.Trim().ToLowerInvariant()] = photo;
        DockPanel.SetDock(avatar, Dock.Left);
        layout.Children.Add(avatar);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = member.Name,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var roleText = new TextBlock
        {
            Text = ProjectRoleLabels(member),
            FontSize = 11.5,
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        teamRoleTargets[member.Email.Trim().ToLowerInvariant()] = (roleText, member);
        words.Children.Add(roleText);
        words.Children.Add(BuildTeamMemberSourcesLine(member));

        words.Children.Add(BuildTeamMemberPresenceLine(member));
        layout.Children.Add(words);

        card.Child = layout;
        return card;
    }

    /// <summary>
    /// A member's roles in the words the server uses for them, falling back to
    /// the stored codes until the role catalogue has been read.
    /// </summary>
    private string ProjectRoleLabels(MemberRow member)
    {
        string[] codes = member.RoleCodes ?? [];
        if (codes.Length == 0)
            return string.IsNullOrWhiteSpace(member.Roles) ? "Үүрэг тодорхойгүй" : member.Roles;

        return string.Join(
            ", ",
            codes.Select(code =>
                projectRoleLabels.TryGetValue(code.Trim(), out string? label) ? label : code.Trim()));
    }

    /// <summary>
    /// Reads the role catalogue and the members' photographs once per project,
    /// then fills them into the cards already on screen.
    /// </summary>
    /// <summary>
    /// How recently someone has to have been heard from to count as present.
    /// </summary>
    /// <remarks>
    /// The server owns this because it may need changing - a busier server
    /// might want a longer window - and changing it there reaches everyone
    /// without anyone installing anything. An unreachable server or a rule this
    /// build does not recognise leaves the default standing, which is how an
    /// older Studio behaves anyway.
    /// </remarks>
    private TimeSpan presenceOnlineWithin = MemberPresence.DefaultOnlineWithin;
    private bool presenceRuleLoaded;

    private async Task RefreshPresenceRuleAsync()
    {
        if (presenceRuleLoaded)
            return;

        IReadOnlyList<StudioServerRule> rules = await account.GetServerRulesAsync();
        if (rules.Count == 0)
        {
            // Nothing came back - a dropped request, or a server with no rules
            // yet. The default stands, and the next visit tries again rather
            // than pinning it for the rest of the session on one bad moment.
            return;
        }

        presenceRuleLoaded = true;
        StudioServerRule? presence = rules.FirstOrDefault(rule =>
            rule.Id.Equals("presence", StringComparison.OrdinalIgnoreCase));
        if (presence is null ||
            !presence.Values.TryGetValue("onlineWithinSeconds", out long seconds) ||
            seconds <= 0)
        {
            // The server answered and simply has no presence rule, or one this
            // build cannot use. That is an answer, so stop asking.
            return;
        }

        presenceOnlineWithin = TimeSpan.FromSeconds(seconds);

        // How often to ask again, also the server's to decide - kept below the
        // online window so nobody blinks offline between fetches.
        presenceRefreshEvery = MemberPresence.RefreshInterval(
            presenceOnlineWithin,
            presence.Values.TryGetValue("heartbeatIntervalSeconds", out long interval)
                ? TimeSpan.FromSeconds(interval)
                : null);
    }

    /// <summary>
    /// Asks the server who is present, and fills the dots in.
    /// </summary>
    /// <remarks>
    /// Presence used to arrive only as a side effect of a full cloud sync,
    /// which happens on its own schedule and often after the team cards have
    /// already been drawn - so on a freshly opened project every colleague read
    /// as never-heard-from, including whoever was sitting in front of it. Asked
    /// for here, it is there when the page is looked at.
    ///
    /// The call also counts as this device being heard from, so opening a
    /// project is itself a sign of life.
    /// </remarks>
    private DateTimeOffset lastPresenceFetchUtc = DateTimeOffset.MinValue;
    private TimeSpan presenceRefreshEvery = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Keeps the dots current while somebody is looking at them.
    /// </summary>
    /// <remarks>
    /// Presence was fetched once, when the project opened, and the cards are
    /// rebuilt on any refresh of the page - so the same timestamps were being
    /// judged against a later and later "now". Past the threshold the whole
    /// team turned red at once, including the person reading the screen, who
    /// was plainly present. Old facts re-evaluated are worse than no facts:
    /// they look like an answer.
    ///
    /// This rides the notification timer rather than adding one. That timer is
    /// already talking to the server every 45 seconds; the only new cost is one
    /// request, spaced by what the server's rules ask for.
    /// </remarks>
    private async Task RefreshTeamPresenceIfVisibleAsync()
    {
        if (!projectWorkspaceOpen ||
            activePage != StudioPage.Foundation ||
            !state.HasOpenProject ||
            !account.IsSignedIn)
        {
            return;
        }

        string projectId = state.Project.Cloud.ServerProjectId;
        if (projectId.Length == 0)
            return;

        if (DateTimeOffset.UtcNow - lastPresenceFetchUtc < presenceRefreshEvery)
            return;

        try
        {
            await RefreshTeamPresenceAsync(projectId);
        }
        catch (Exception exception) when (
            exception is StudioAccountException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            // A missed refresh leaves the dots as they were, which is the last
            // thing actually known. Saying so on screen every minute would be
            // noise; the next tick tries again.
        }
    }

    private async Task RefreshTeamPresenceAsync(string projectId)
    {
        lastPresenceFetchUtc = DateTimeOffset.UtcNow;
        StudioCloudProjectDetail detail = await account.GetProjectAsync(projectId);
        state.ParticipantPresence.Clear();
        foreach (StudioCloudParticipant participant in detail.Participants ?? [])
        {
            if (participant is null || string.IsNullOrWhiteSpace(participant.AccountEmail))
                continue;

            state.ParticipantPresence[participant.AccountEmail.Trim()] = new ParticipantPresenceInfo(
                participant.LastSeenAtUtc,
                participant.ProfileImageUrl,
                participant.Initials);
        }

        foreach ((StackPanel target, MemberRow member) in teamPresenceTargets.Values)
            FillTeamMemberPresenceLine(target, member);
    }

    private async Task LoadProjectTeamDetailAsync()
    {
        if (!account.IsSignedIn || !state.HasOpenProject)
            return;

        string projectId = state.Project.Cloud.ServerProjectId;
        if (projectId.Length == 0)
            return;

        try
        {
            await RefreshPresenceRuleAsync();
            if (projectRoleLabels.Count == 0)
            {
                foreach (StudioProjectRole role in await account.ListProjectRolesAsync())
                    projectRoleLabels[role.Code] = role.Label;
                foreach ((TextBlock target, MemberRow member) in teamRoleTargets.Values)
                    target.Text = ProjectRoleLabels(member);
            }

            await RefreshTeamPresenceAsync(projectId);
            StudioProjectChatResponse chat = await account.GetProjectChatAsync(projectId, take: 1);
            foreach (StudioProjectChatParticipant participant in chat.Participants)
            {
                string key = participant.Email.Trim().ToLowerInvariant();
                if (participant.ProfileImageUrl.Length == 0 ||
                    !teamAvatarTargets.TryGetValue(key, out Image? target))
                {
                    continue;
                }

                await ApplyTeamAvatarAsync(participant.ProfileImageUrl, target);
            }
        }
        catch (Exception exception) when (
            exception is StudioAccountException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            // The strip already names everyone; a photograph is the only thing
            // a failed read costs.
        }
    }

    private async Task ApplyTeamAvatarAsync(string imageUrl, Image target)
    {
        if (!teamAvatarCache.TryGetValue(imageUrl, out ImageSource? source))
        {
            byte[]? bytes = await account.DownloadProjectChatAssetAsync(imageUrl);
            if (bytes is null || bytes.Length == 0)
                return;

            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            source = bitmap;
            teamAvatarCache[imageUrl] = source;
        }

        target.Source = source;
        target.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The project's record, read rather than typed. In view mode a form of
    /// empty input boxes reads as a settings dialog and says nothing about the
    /// project; the same fields are shown here as written values, and the form
    /// appears only once Засварлах is pressed.
    /// </summary>
    /// <summary>
    /// Whether this colleague is there, and anything the project needs to say
    /// about their membership.
    /// </summary>
    /// <remarks>
    /// The dot used to be green and read "Идэвхтэй" for everybody, which said
    /// nothing: the member list is already filtered to active members upstream,
    /// so the only possible value was the one shown. Read as presence - which
    /// is how a green dot reads - it claimed knowledge the product did not
    /// have. Now it shows what the server actually reports, and shows nothing
    /// when the server has never heard from someone.
    /// </remarks>
    private UIElement BuildTeamMemberPresenceLine(MemberRow member)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 7, 0, 0),
        };

        // Registered so the line can be filled in again when presence arrives.
        // The card is built when the project opens; what the server knows about
        // who is present arrives afterwards, over the network. Without this the
        // card keeps whatever it was built with - which, on a freshly opened
        // project, is nothing, and every colleague reads as unheard-from.
        // The avatars beside them already work this way.
        teamPresenceTargets[member.Email.Trim().ToLowerInvariant()] = (line, member);
        FillTeamMemberPresenceLine(line, member);
        return line;
    }

    private void FillTeamMemberPresenceLine(StackPanel line, MemberRow member)
    {
        line.Children.Clear();
        state.ParticipantPresence.TryGetValue(
            member.Email.Trim(),
            out ParticipantPresenceInfo? presence);
        MemberPresenceState presenceState = MemberPresence.Resolve(
            presence?.LastSeenAtUtc,
            DateTimeOffset.UtcNow,
            presenceOnlineWithin);
        string presenceTooltip = MongolianRelativeTime.DescribeLastSeen(
            presence?.LastSeenAtUtc,
            DateTimeOffset.UtcNow);

        // Nothing is drawn for Unknown. A grey dot would still read as a state
        // somebody chose; an absent dot reads as what it is.
        if (presenceState != MemberPresenceState.Unknown)
        {
            line.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = presenceState == MemberPresenceState.Online
                    ? StudioTheme.SuccessBrush
                    : StudioTheme.DangerBrush,
                ToolTip = presenceState == MemberPresenceState.Online
                    ? "Studio-д одоо холбогдсон"
                    : presenceTooltip,
            });
        }

        // The membership note is separate information and outranks presence:
        // somebody on their way out of the project matters more than whether
        // their Studio happens to be open.
        // The fact, not the sentence. This used to compare the displayed text
        // against "Идэвхтэй" - so rewording the label, or one look-alike
        // letter in it, would have marked everybody as leaving.
        bool leaving = member.IsLeaving;
        if (leaving)
        {
            line.Children.Add(new TextBlock
            {
                Text = member.Status,
                FontSize = 11,
                Foreground = StudioTheme.WarningBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            return;
        }

        line.Children.Add(new TextBlock
        {
            Text = presenceState switch
            {
                MemberPresenceState.Online => "Studio-д онлайн",
                MemberPresenceState.Offline => presenceTooltip,
                _ => "Мэдээлэл алга",
            },
            FontSize = 11,
            Foreground = presenceState == MemberPresenceState.Online
                ? StudioTheme.SuccessBrush
                : StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = presenceTooltip,
        });
        return;
    }

    /// <summary>
    /// What this person has put into the project.
    /// </summary>
    /// <remarks>
    /// Only cloud-registered sources carry the person who registered them, so
    /// a member who has only ever added a source on their own machine reads as
    /// having registered none. That is the honest answer rather than a blank
    /// space, which reads as "still loading".
    /// </remarks>
    private TextBlock BuildTeamMemberSourcesLine(MemberRow member)
    {
        ProjectMemberSourceSummary summary = state.HasOpenProject
            ? ProjectMemberSources.For(state.Project.Cloud.SharedSources, member.Email)
            : ProjectMemberSourceSummary.None;

        var line = new TextBlock
        {
            FontSize = 11,
            Foreground = StudioTheme.MutedTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 5, 0, 0),
        };

        if (!summary.Any)
        {
            line.Text = "Эх үүсвэр бүртгүүлээгүй";
            return line;
        }

        line.Text = summary.SheetCount > 0
            ? $"{summary.Count} эх үүсвэр · {summary.SheetCount} хуудас"
            : $"{summary.Count} эх үүсвэр";
        line.ToolTip = string.Join("\n", summary.Names);
        return line;
    }

    private UIElement BuildProjectRecordView()
    {
        projectRecordView.Margin = new Thickness(0, 4, 0, 0);
        // One width for every section, so the record reads as one document
        // rather than as cards that each stopped where their text did.
        projectRecordView.MaxWidth = 980;
        projectRecordView.HorizontalAlignment = HorizontalAlignment.Left;
        projectRecordHost = StudioWidgets.CreateScrollHost(projectRecordView);
        return projectRecordHost;
    }

    /// <summary>Shows the record or the form, never both.</summary>
    private void ApplyFoundationPresentation(bool editing)
    {
        if (projectRecordHost is null || foundationEditTabs is null)
            return;

        projectRecordHost.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        foundationEditTabs.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (!editing)
            RefreshProjectRecordView();
    }

    /// <summary>
    /// One definition of what the record holds, so the read view and the form
    /// can never drift apart: the value is read from the very control the form
    /// edits.
    /// </summary>
    private IReadOnlyList<(string Section, string Label, Func<string> Value)> ProjectRecordFields =>
    [
        ("Үндэслэл", "Төслийн код", () => projectCodeBox.Text),
        ("Үндэслэл", "Төслийн нэр", () => projectNameBox.Text),
        ("Үндэслэл", "Төслийн төрөл", () => ComboText(projectTypeBox)),
        ("Үндэслэл", "Үе шат", () => ComboText(projectStageBox)),
        ("Үндэслэл", "Үндэслэлийн төрөл", () => basisSourceBox.Text),
        ("Үндэслэл", "Хүсэлтийн дугаар", () => requestNumberBox.Text),
        ("Үндэслэл", "Төслийн хаяг", () => siteAddressBox.Text),
        ("Үндэслэл", "Газрын холбоос", () => landReferenceBox.Text),
        ("Үндэслэл", "Эх байгууллага", () => basisSourceOrganizationBox.Text),
        ("Үндэслэл", "Товч мэдээлэл", () => basisSummaryBox.Text),
        // The banner already says whether the project is on Cloud ERA; this is
        // the address behind that word, so it belongs with the rest of the
        // record rather than in a card of its own.
        ("Үндэслэл", "Cloud ERA", () => cloudLinkText.Text),
        ("Уялдаа, баталгаажуулалт", "АТД дугаар", () => atdNumberBox.Text),
        ("Уялдаа, баталгаажуулалт", "Олгосон байгууллага", () => atdAuthorityBox.Text),
        ("Уялдаа, баталгаажуулалт", "Төлөв", () => atdStatusBox.Text),
        ("Уялдаа, баталгаажуулалт", "Шаардлага, нөхцөл", () => atdSummaryBox.Text),
    ];

    private static string ComboText(ComboBox box) =>
        box.SelectedItem switch
        {
            IStudioProjectTypeDefinition type => type.Label,
            StudioProjectStageDefinition stage => stage.Label,
            null => "",
            var value => value.ToString() ?? "",
        };

    private void RefreshProjectRecordView()
    {
        projectRecordView.Children.Clear();
        if (!state.HasOpenProject)
            return;

        string openSection = "";
        StackPanel? rows = null;
        foreach ((string section, string label, Func<string> value) in ProjectRecordFields)
        {
            if (!section.Equals(openSection, StringComparison.Ordinal))
            {
                openSection = section;
                var card = new Border
                {
                    Background = StudioTheme.PanelBrush,
                    BorderBrush = StudioTheme.BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(20, 16, 20, 8),
                    Margin = new Thickness(0, 0, 0, 12),
                };
                var body = new StackPanel();
                body.Children.Add(new TextBlock
                {
                    Text = section,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = StudioTheme.TextBrush,
                    Margin = new Thickness(0, 0, 0, 12),
                });
                rows = new StackPanel();
                body.Children.Add(rows);
                card.Child = body;
                projectRecordView.Children.Add(card);
            }

            rows!.Children.Add(BuildProjectRecordRow(label, value()));
        }
    }

    private static UIElement BuildProjectRecordRow(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 16, 0),
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        string text = (value ?? "").Trim();
        var written = new TextBlock
        {
            // An em dash rather than an empty line: the field is known to be
            // part of the record and known to be unfilled.
            Text = text.Length == 0 ? "—" : text,
            FontSize = 13,
            Foreground = text.Length == 0 ? StudioTheme.FaintTextBrush : StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(written, 1);
        row.Children.Add(written);
        return row;
    }

    /// <summary>
    /// Fills the masthead from the open project. Called wherever the project or
    /// its edit mode moves, so it can never describe a project that is closed.
    /// </summary>
    private void RefreshProjectOverview()
    {
        if (!state.HasOpenProject)
        {
            projectOverviewBanner.Visibility = Visibility.Collapsed;
            projectOverviewFacts.Visibility = Visibility.Collapsed;
            return;
        }

        projectOverviewBanner.Visibility = Visibility.Visible;
        projectOverviewFacts.Visibility = Visibility.Visible;

        ProjectWorkspace project = state.Project;
        projectOverviewName.Text = string.IsNullOrWhiteSpace(project.Identity.Name)
            ? "Нэргүй төсөл"
            : project.Identity.Name;
        projectOverviewStageText.Text =
            ProjectStageLabel(project.Identity.StageName).ToUpperInvariant();

        // Read from the control the form edits rather than resolved again here,
        // so the banner and the record can never name different types.
        projectOverviewIdentity.Text = string.Join(
            " · ",
            new[] { project.Code, ComboText(projectTypeBox) }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        RefreshProjectOverviewCompany();
        RefreshProjectOverviewFacts();
        RefreshProjectTeamOverview();
        // The record reads its values out of the form's own controls, so it is
        // rebuilt here too - this runs when a project is bound to the shell,
        // which is when those controls take their values.
        RefreshProjectRecordView();
        _ = RefreshProjectOverviewCoverAsync();
    }

    private void RefreshProjectOverviewCompany()
    {
        CompanyProfile company = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        string name = CompanyDisplayName(company);
        projectOverviewCompanyName.Text = string.IsNullOrWhiteSpace(name)
            ? "Зураг төслийн байгууллага сонгогдоогүй"
            : name;
        projectOverviewCompanyRole.Text = "Зураг төсөл боловсруулагч байгууллага";
        projectOverviewCompanyMonogram.Text = StudioOrganizationCrest.Initials(name);

        ImageSource? logo = LoadLogoImage(company.LogoPath);
        projectOverviewCompanyLogo.Source = logo;
        projectOverviewCompanyCrest.Visibility = logo is null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// What the project is made of, each figure a way into the page that owns
    /// it. Read from the open workspace, so nothing here can disagree with the
    /// page it leads to.
    /// </summary>
    private void RefreshProjectOverviewFacts()
    {
        projectOverviewFacts.Children.Clear();
        ProjectWorkspace project = state.Project;

        AddProjectFact(
            "Эх үүсвэр",
            project.Sources.Count.ToString(),
            "Revit, AutoCAD болон бусад эх үүсвэр",
            StudioPage.Sources);
        AddProjectFact(
            "Альбумын хуудас",
            state.Album.Pages.Count.ToString(),
            "Одоогийн альбум дахь хуудас",
            StudioPage.Albums);
        if (project.BuildingGroups.Count > 0)
        {
            AddProjectFact(
                "Барилга",
                project.BuildingGroups.Count.ToString(),
                "Төслийн барилгын бүрдэл",
                StudioPage.Sources);
        }

        bool linked = project.Cloud.Origin.Equals(
            ProjectOrigins.Cloud,
            StringComparison.OrdinalIgnoreCase);
        AddProjectFact(
            "Cloud ERA",
            linked ? "Холбогдсон" : "Локал",
            linked
                ? "Cloud ERA төсөлтэй холбоотой"
                : "Зөвхөн энэ төхөөрөмж дээр",
            StudioPage.Participants);
    }

    private void AddProjectFact(string title, string value, string detail, StudioPage page)
    {
        var card = new Border
        {
            Width = 214,
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(0, 0, 12, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = detail,
        };
        var words = new StackPanel();
        words.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = StudioTheme.MutedTextBrush,
        });
        words.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            Margin = new Thickness(0, 6, 0, 0),
        });
        card.Child = words;
        card.MouseLeftButtonUp += (_, _) => SelectPage(page);
        card.MouseEnter += (_, _) => card.BorderBrush = StudioTheme.BorderHoverBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = StudioTheme.BorderBrush;
        projectOverviewFacts.Children.Add(card);
    }

    /// <summary>
    /// The album's first page, drawn as the project's cover. Rendered once per
    /// PDF, because this refresh runs on every edit-mode change.
    /// </summary>
    private async Task RefreshProjectOverviewCoverAsync()
    {
        string path = ResolveCurrentProjectAlbumPath() ?? "";
        if (path.Length == 0 || !File.Exists(path))
        {
            overviewCoverPdfPath = "";
            ApplyProjectOverviewCover(null);
            return;
        }

        if (path.Equals(overviewCoverPdfPath, StringComparison.OrdinalIgnoreCase))
            return;

        overviewCoverPdfPath = path;
        try
        {
            System.Windows.Media.Imaging.BitmapSource? page =
                await projectThumbnailImages.GetPageAsync(
                    path,
                    pageNumber: 1,
                    pixelWidth: 520,
                    CancellationToken.None);
            if (path.Equals(overviewCoverPdfPath, StringComparison.OrdinalIgnoreCase))
                ApplyProjectOverviewCover(page);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ApplyProjectOverviewCover(null);
        }
    }

    private void ApplyProjectOverviewCover(ImageSource? cover)
    {
        if (cover is null)
        {
            projectOverviewCover.Background = StudioTheme.InputBrush;
            projectOverviewCover.Width = ProjectCoverWidth;
            projectOverviewCover.Height = ProjectCoverWidth * 297d / 210d;
            return;
        }

        // The frame takes the page's proportions rather than assuming A4, so
        // the whole page shows with no empty bars beside it and nothing cut
        // off. A landscape sheet ends up short and wide, a portrait one tall.
        (double Width, double Height)? box = ScaledFit.Within(
            cover.Width,
            cover.Height,
            ProjectCoverMaxWidth,
            ProjectCoverMaxHeight);
        projectOverviewCover.Width = box?.Width ?? ProjectCoverWidth;
        projectOverviewCover.Height = box?.Height ?? ProjectCoverWidth * 297d / 210d;

        // Painted as the border's background so it is clipped to the rounded
        // corners; an Image child would square them off.
        projectOverviewCover.Background = new ImageBrush(cover)
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
    }

}
