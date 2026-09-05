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
    private readonly IAdministrativeUnitCatalogue administrativeUnits =
        StudioAdministrativeUnitCatalogue.Unavailable;
    private AdministrativeUnitPicker? sitePicker;
    private bool siteLocationBinding;

    private UIElement BuildSiteLocationEditor()
    {
        var panel = new StackPanel();
        panel.Children.Add(siteLocationMessage);
        panel.Children.Add(StudioWidgets.CreateFormRow("Хот, аймаг", siteProvinceBox));
        panel.Children.Add(siteDistrictLabel);
        panel.Children.Add(siteDistrictBox);
        panel.Children.Add(siteWardLabel);
        panel.Children.Add(siteWardBox);

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

            Fill(siteProvinceBox, sitePicker.ProvinceChoices(), sitePicker.Province);
            Fill(siteDistrictBox, sitePicker.DistrictChoices(), sitePicker.District, siteDistrictLabel);
            Fill(siteWardBox, sitePicker.WardChoices(), sitePicker.Ward, siteWardLabel);
        }
        finally
        {
            siteLocationBinding = false;
        }

        static void Fill(
            ComboBox box,
            AdministrativeUnitChoices choices,
            AdministrativeUnit? selected,
            TextBlock? label = null)
        {
            if (label is not null)
            {
                label.Text = SiteLocationLabels.HeadingFor(choices);
                label.Visibility = SiteLocationLabels.HeadingIsShown(choices)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
