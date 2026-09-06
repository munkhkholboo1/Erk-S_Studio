using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Three combo boxes over <see cref="AdministrativeUnitPicker"/>, and nothing
/// else.
///
/// EVERY DECISION IS ON THE OTHER SIDE. What a level is called, which units it
/// offers, what a change to a higher level clears, whether there is a catalogue
/// at all - all of it is answered by the picker and simply displayed here. That
/// is deliberate rather than tidy: rules written into view-assembly code on this
/// platform have been wrong four times, because nothing can reach them to
/// measure them. If this file ever needs to decide something, the decision goes
/// to Core and this file asks for it.
/// </summary>
internal sealed partial class ShellView
{
    private readonly ComboBox siteProvinceBox = new();
    private readonly ComboBox siteDistrictBox = new();
    private readonly ComboBox siteWardBox = new();
    private readonly TextBlock siteDistrictLabel = StudioWidgets.CreateHint("");
    private readonly TextBlock siteWardLabel = StudioWidgets.CreateHint("");
    private readonly TextBlock siteLocationMessage = StudioWidgets.CreateHint("");
    private readonly TextBlock siteLocationSource = StudioWidgets.CreateHint("");
    private readonly TextBlock siteLocationStoredProblem = StudioWidgets.CreateHint("");
    private readonly TextBlock siteDistrictEmptyNotice = StudioWidgets.CreateHint("");
    private readonly TextBlock siteWardEmptyNotice = StudioWidgets.CreateHint("");
    private readonly StudioAdministrativeUnitCatalogue administrativeUnits =
        StudioAdministrativeUnitCatalogue.Live;
    private AdministrativeUnitPicker? sitePicker;
    private bool siteLocationBinding;
    private bool administrativeUnitsLoading;

    private UIElement BuildSiteLocationEditor()
    {
        var panel = new StackPanel();
        panel.Children.Add(siteLocationMessage);
        panel.Children.Add(siteLocationSource);
        panel.Children.Add(siteLocationStoredProblem);
        panel.Children.Add(StudioWidgets.CreateFormRow("Хот, аймаг", siteProvinceBox));
        panel.Children.Add(siteDistrictLabel);
        panel.Children.Add(siteDistrictBox);
        panel.Children.Add(siteDistrictEmptyNotice);
        panel.Children.Add(siteWardLabel);
        panel.Children.Add(siteWardBox);
        panel.Children.Add(siteWardEmptyNotice);

        siteProvinceBox.SelectionChanged += (_, _) => OnSiteLevelChosen(
            unit => sitePicker?.ChooseProvince(unit),
            siteProvinceBox);
        siteDistrictBox.SelectionChanged += (_, _) => OnSiteLevelChosen(
            unit => sitePicker?.ChooseDistrict(unit),
            siteDistrictBox);
        siteWardBox.SelectionChanged += (_, _) => OnSiteLevelChosen(
            unit => sitePicker?.ChooseWard(unit),
            siteWardBox);
        return panel;
    }

    private void OnSiteLevelChosen(Action<AdministrativeUnit?> choose, ComboBox source)
    {
        if (siteLocationBinding)
            return;
        choose(source.SelectedItem as AdministrativeUnit);
        RefreshSiteLocationEditor();
    }

    private void BindSiteLocationEditor()
    {
        sitePicker = new AdministrativeUnitPicker(administrativeUnits);
        sitePicker.Restore(state.HasOpenProject
            ? state.Project.Foundation.InitiationBasis.SiteLocation
            : null);
        RefreshSiteLocationEditor();
        _ = EnsureAdministrativeUnitsAsync();
    }

    /// <summary>
    /// Fetches the catalogue the first time an editor needs it, and REBUILDS the
    /// picker afterwards.
    ///
    /// The rebuild is the part that is easy to leave out. A stored project keeps
    /// codes; restoring them means finding those codes in a list, and the list
    /// arrived after the picker was built - so a picker that is not rebuilt shows
    /// three empty boxes over a catalogue that is now fully loaded, which reads
    /// exactly like a download that failed.
    ///
    /// A failed attempt is not remembered as final. The flag guards only against
    /// two requests at once, so opening another project tries again - the usual
    /// cure for "the laptop was not on the network a minute ago" - while a
    /// catalogue already in hand stops any further request.
    /// </summary>
    private async Task EnsureAdministrativeUnitsAsync()
    {
        if (administrativeUnitsLoading || administrativeUnits.HasUnits)
            return;

        administrativeUnitsLoading = true;
        try
        {
            await administrativeUnits.LoadAsync(account.SuggestedServerUrl).ConfigureAwait(true);
        }
        finally
        {
            administrativeUnitsLoading = false;
        }

        if (administrativeUnits.HasUnits)
            BindSiteLocationEditor();
        else
            RefreshSiteLocationEditor();
    }

    private void RefreshSiteLocationEditor()
    {
        if (sitePicker is null)
            return;

        siteLocationBinding = true;
        try
        {
            siteLocationMessage.Text = sitePicker.UnavailableMessageMn;
            siteLocationMessage.Visibility = sitePicker.CatalogueIsAvailable
                ? Visibility.Collapsed
                : Visibility.Visible;

            // Shown WHENEVER there is something to say, including - especially -
            // when the boxes are full. A cached list that looks live is the one
            // state where nothing else on screen would mention it.
            siteLocationSource.Text = administrativeUnits.SourceNoticeMn;
            siteLocationSource.Visibility = siteLocationSource.Text.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible;

            // A stored chain whose levels disagree is KEPT rather than cleared,
            // and refuses to be used. Without this line the person sees three
            // empty pickers and no reason - which is what the deletion would
            // have shown them, only with the record gone as well.
            siteLocationStoredProblem.Text = state.HasOpenProject
                ? state.Project.Foundation.InitiationBasis.SiteLocation.ProblemMn
                : "";
            siteLocationStoredProblem.Visibility = siteLocationStoredProblem.Text.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible;

            Fill(siteProvinceBox, sitePicker.ProvinceChoices(), sitePicker.Province);
            Fill(
                siteDistrictBox,
                sitePicker.DistrictChoices(),
                sitePicker.District,
                siteDistrictLabel,
                siteDistrictEmptyNotice,
                SiteLocationLabels.DistrictProvisionalMn);
            Fill(
                siteWardBox,
                sitePicker.WardChoices(),
                sitePicker.Ward,
                siteWardLabel,
                siteWardEmptyNotice,
                SiteLocationLabels.WardProvisionalMn);
        }
        finally
        {
            siteLocationBinding = false;
        }

        static void Fill(
            ComboBox box,
            AdministrativeUnitChoices choices,
            AdministrativeUnit? selected,
            TextBlock? label = null,
            TextBlock? emptyNotice = null,
            string provisionalHeading = "")
        {
            if (emptyNotice is not null)
            {
                emptyNotice.Text = SiteLocationLabels.EmptyNoticeFor(choices);
                emptyNotice.Visibility = emptyNotice.Text.Length == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (label is not null)
            {
                // The published heading when there is one, a provisional pair of
                // words when nothing above has been chosen yet. Both come from
                // SiteLocationLabels - this file decides nothing.
                label.Text = SiteLocationLabels.DisplayHeadingFor(choices, provisionalHeading);
                label.Visibility = label.Text.Length == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            box.ItemsSource = choices.Units;
            box.DisplayMemberPath = nameof(AdministrativeUnit.NameMn);
            box.SelectedItem = selected;
            box.IsEnabled = choices.Units.Count > 0;
        }
    }

    private ProjectSiteLocation CaptureSiteLocationDraft() =>
        sitePicker?.ToLocation() ?? new ProjectSiteLocation();
}
