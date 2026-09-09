using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Two places where a window went on showing what had already changed.
///
/// Both are the shape this day kept producing: something is written or released,
/// and the running program is never told. The owner met the first one as «суудлаа
/// сулалсан ч дэлгэц Бот: <нэр> гэж хэвлэсээр байна».
/// </summary>
public sealed class BotSeatManagementRefreshTests
{
    [Fact]
    public void CLOSINGSeatManagementRefreshesTheShellBehindIt()
    {
        // Releasing or deleting this machine's own seat inside that dialog
        // changes what this device is. The dialog reports no result, so the
        // state on disk is read back rather than assumed - and a seat that is
        // gone takes everything read for it with it.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task ShowBotManagementAsync()");

        int show = body.IndexOf("dialog.ShowDialog();", StringComparison.Ordinal);
        int read = body.IndexOf("StudioBotDeviceStateStore.Read();", StringComparison.Ordinal);
        int update = body.IndexOf("UpdateAccountUi();", StringComparison.Ordinal);

        Assert.True(show > 0, "the dialog is not shown");
        Assert.True(read > show, "the seat must be read back AFTER the dialog closes");
        Assert.True(update > read, "the account panel must be refreshed after that read");
        Assert.Contains("botAssignedProjectIds = null;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THESeatActionsAreDISABLEDUntilARowIsChosen()
    {
        // 🔴 «SELECT A SEAT FIRST» LIVED AT THE BOTTOM OF THE WINDOW. Six
        // buttons all act on the selected row; with none selected the press did
        // nothing visible and the reason sat where nobody looks. The file states
        // the principle itself - «the condition is on the button, not discovered
        // by pressing it» - and these six were the exception to it.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.Contains(
            "seatActionButtons = [reveal, change, unlock, invite, release, delete];",
            source,
            StringComparison.Ordinal);
        Assert.Contains("private void RefreshSeatActions()", source, StringComparison.Ordinal);
        Assert.Contains("button.IsEnabled = hasSeat;", source, StringComparison.Ordinal);

        // Set once when the window opens and again on every selection change -
        // a state applied only on change starts life wrong.
        Assert.Equal(3, Occurrences(source, "RefreshSeatActions()"));
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
