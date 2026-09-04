using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    /// <summary>
    /// Applies this machine's seat, if it holds one, to the runtime identity.
    ///
    /// Called wherever the account UI is refreshed, because that is also where
    /// the signed-in person changes - and the seat must survive that. A machine
    /// seated to an organisation keeps owning and receiving for the seat while
    /// somebody signs in with their own account; without this call the two
    /// would collapse back into one and the seat would stop receiving the
    /// moment an employee looked at their own projects.
    /// </summary>
    private void ApplyDeviceSeat()
    {
        StudioBotDeviceState? seat = StudioBotDeviceStateStore.Read();
        state.ConfigureDeviceSeat(seat?.SeatIdentity);
    }

    private static bool IsSeatedAsBot => StudioBotDeviceStateStore.Read() is not null;

    /// <summary>
    /// Builds the bot entries of the account menu. Both are hidden while signed
    /// out, and "make this a bot" is hidden on a machine that already holds a
    /// seat - one device, one seat.
    ///
    /// Hiding is not the protection: the server refuses an unlicensed create
    /// and names the refusal. The menu shows the road that works.
    /// </summary>
    private IEnumerable<MenuItem> BuildBotMenuItems()
    {
        var manage = new MenuItem { Header = "Ботын удирдлага…" };
        manage.Click += async (_, _) => await ShowBotManagementAsync();
        yield return manage;

        if (!IsSeatedAsBot)
        {
            var seat = new MenuItem { Header = "Энэ төхөөрөмжийг бот болгох…" };
            seat.Click += async (_, _) => await SeatThisDeviceAsync();
            yield return seat;
        }
        else
        {
            var leave = new MenuItem { Header = "Ботын төлөвөөс гарах…" };
            leave.Click += async (_, _) => await LeaveBotStateAsync();
            yield return leave;
        }
    }

    private async Task<StudioCloudOrganization?> PickOrganizationAsync()
    {
        if (!await EnsureSignedInAsync())
            return null;
        try
        {
            IReadOnlyList<StudioCloudOrganization> organizations =
                await account.ListOrganizationsAsync();
            if (organizations.Count == 0)
            {
                SetStatus("Ботын суудал үүсгэхэд байгууллага шаардлагатай.");
                return null;
            }
            return organizations[0];
        }
        catch (Exception exception)
        {
            SetStatus("Байгууллагын жагсаалт уншигдсангүй: " + exception.Message);
            return null;
        }
    }

    private async Task ShowBotManagementAsync()
    {
        StudioCloudOrganization? organization = await PickOrganizationAsync();
        if (organization is null)
            return;
        var dialog = new BotSeatManagementDialog(account, organization)
        {
            Owner = Window.GetWindow(Root),
        };
        dialog.ShowDialog();
    }

    private async Task SeatThisDeviceAsync()
    {
        if (!await EnsureSignedInAsync())
            return;
        IReadOnlyList<StudioCloudOrganization> organizations;
        try
        {
            organizations = await account.ListOrganizationsAsync();
        }
        catch (Exception exception)
        {
            SetStatus("Байгууллагын жагсаалт уншигдсангүй: " + exception.Message);
            return;
        }
        if (organizations.Count == 0)
        {
            SetStatus("Ботын суудал үүсгэхэд байгууллага шаардлагатай.");
            return;
        }

        var dialog = new BotSeatCreateDialog(account, organizations)
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true || dialog.Seated is null)
            return;

        // Only written once the server seated the device AND this machine's
        // owner credential was erased - the dialog does not report success
        // before both.
        StudioBotDeviceStateStore.Write(dialog.Seated);
        ApplyDeviceSeat();
        UpdateAccountUi();
        SetStatus(
            $"Энэ төхөөрөмж «{dialog.Seated.DisplayName}» ботын суудал боллоо. " +
            "Эзэмшигчийн нэвтрэлт энэ машинаас устсан.");
    }

    private async Task LeaveBotStateAsync()
    {
        StudioBotDeviceState? seat = StudioBotDeviceStateStore.Read();
        if (seat is null)
            return;
        if (StudioMessageDialog.Show(
                Window.GetWindow(Root),
                $"«{seat.DisplayName}» ботын төлөвөөс гарах уу? Гарахад эзэмшигч " +
                "дахин нэвтрэх шаардлагатай — орох, гарах нэг хаалга.",
                "Ботын төлөвөөс гарах",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        if (!await EnsureSignedInAsync())
        {
            SetStatus("Ботын төлөвөөс гарахад эзэмшигч нэвтрэх шаардлагатай.");
            return;
        }
        try
        {
            await account.LeaveBotStateAsync(seat.OrganizationId, seat.BotId);
            StudioBotDeviceStateStore.Clear();
            ApplyDeviceSeat();
            UpdateAccountUi();
            SetStatus("Ботын төлөвөөс гарлаа.");
        }
        catch (Exception exception)
        {
            SetStatus("Ботын төлөвөөс гарч чадсангүй: " + exception.Message);
        }
    }
}
