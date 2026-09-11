using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// What the seat dialog promises about leaving bot state must be what the code
/// does.
///
/// 🔴 IT PROMISED «буцаах ганц зам нь дахин нэвтрэх» - the only way back is to
/// sign in again - AND SIGNING IN DOES NOT DO IT. The owner signed in, saw
/// themselves signed in, and their machine was still a seat: their own projects
/// were filtered away and every sync went up as the bot. They said it plainly:
/// «яг үнэндээ бот төлөвөөсөө хэзээ ч гараагүй».
///
/// A day of diagnosis went into the symptoms that promise produced. The sentence
/// was not merely wrong - it told everybody the machine was already out, so
/// nobody looked at whether it was.
/// </summary>
public sealed class BotStateExitPromiseTests
{
    [Fact]
    public void ONLYTwoPathsLeaveBotStateAndSIGNINGINIsNeither()
    {
        // 🔴 DERIVED FROM THE CALL GRAPH, NOT FROM THE PROSE. The device's seat
        // record is erased in exactly two places, and the sign-in path is not
        // one of them - which is the whole of why the promise was false.
        IReadOnlyCollection<string> owners =
            OwnersAcrossTheApp("StudioBotDeviceStateStore.Clear()");

        Assert.Equal(
            new[]
            {
                // The owner released the seat on the server; the device notices.
                "ResumeAsBotAsync",
                // The shared helper behind both deliberate exits - the menu's
                // leave, and the recovery for a machine the server still holds.
                "ForgetLocalSeatTraces",
            }.Order(),
            owners.Order());
    }

    [Fact]
    public void SIGNINGINAsTheOwnerKeepsTheSeatOnPurpose()
    {
        // The code half is deliberate and stays: an owner has to be able to work
        // beside a seat without giving it up - that is what the passport entry
        // is for. So the sentence was the half that had to move.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"), "private async Task ResumeAsOwnerNowAsync()");

        Assert.DoesNotContain("StudioBotDeviceStateStore.Clear()", body, StringComparison.Ordinal);
        Assert.Contains("ApplyDeviceSeat();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEDialogNamesTheACTIONThatActuallyLeaves()
    {
        // 🔴 THE SENTENCE AND THE MENU ENTRY ARE LOCKED TOGETHER. If the entry is
        // ever renamed, this goes red rather than leaving a dialog pointing at a
        // line nobody can find - which is worse than saying nothing, because it
        // reads as the application being broken.
        string dialog = ReadAppSource("BotSeatDialogs.cs");
        string menu = ReadAppSource("ShellView.BotSeat.cs");

        const string entry = "Ботын суудлыг сулалж, төхөөрөмжийг чөлөөлөх";
        Assert.Contains(entry, dialog, StringComparison.Ordinal);
        Assert.Contains(entry, menu, StringComparison.Ordinal);
    }

    [Fact]
    public void THEFalsePromiseIsGone()
    {
        // Named exactly, because this is the sentence that cost the day.
        Assert.DoesNotContain(
            "буцаах ганц зам нь дахин нэвтрэх",
            CodeOnly(ReadAppSource("BotSeatDialogs.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEDialogSaysTheMachineSTAYSSeatedAfterSigningIn()
    {
        // The positive control for the removal above: deleting the false line
        // would satisfy it while leaving a person with no idea what signing in
        // does. The replacement has to state the actual outcome.
        string dialog = ReadAppSource("BotSeatDialogs.cs");

        Assert.Contains("суудалд ХЭВЭЭР үлдэнэ", dialog, StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<string> OwnersAcrossTheApp(string needle)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? app = null;
        while (directory is not null && app is null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
                app = new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.NotNull(app);
        var found = new List<string>();
        foreach (FileInfo file in app.GetFiles("*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(file.FullName, Encoding.UTF8);
            for (int at = source.IndexOf(needle, StringComparison.Ordinal);
                at >= 0;
                at = source.IndexOf(needle, at + 1, StringComparison.Ordinal))
            {
                string owner = EnclosingMethod(source, at);
                if (!found.Contains(owner, StringComparer.Ordinal))
                    found.Add(owner);
            }
        }

        Assert.NotEmpty(found);
        return found;
    }

    private static string EnclosingMethod(string source, int index)
    {
        int best = -1;
        foreach (string opening in new[]
        {
            "\n    private ", "\n    public ", "\n    internal ", "\n    protected ",
        })
        {
            int at = source.LastIndexOf(opening, index, StringComparison.Ordinal);
            if (at > best)
                best = at;
        }

        Assert.True(best > 0, "no enclosing member was found for the call at " + index);
        int paren = source.IndexOf('(', best);
        int arrow = source.IndexOf("=>", best, StringComparison.Ordinal);
        int head = paren < 0 || (arrow >= 0 && arrow < paren) ? arrow : paren;
        Assert.True(head > best, "the enclosing declaration could not be read");

        string[] words = source[best..head]
            .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(words);
        return words[^1];
    }

    private static string CodeOnly(string source) =>
        string.Join(
            "\n",
            source.Split('\n').Where(line =>
            {
                string text = line.TrimStart();
                return !text.StartsWith("//", StringComparison.Ordinal) &&
                    !text.StartsWith("*", StringComparison.Ordinal);
            }));

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
        int start = normalised.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = normalised.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return normalised[start..end];
    }

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
