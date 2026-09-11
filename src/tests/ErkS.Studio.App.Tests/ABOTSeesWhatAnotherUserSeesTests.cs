using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A bot seat is another user on the platform, not a locked-down owner.
///
/// 🔴 THE OWNER CUT A WHOLE DEVICE-LOCK DESIGN DOWN TO ONE SENTENCE. We had built
/// a bot «state» with its own rules, its own exits and its own reconciliation
/// between machine and server - and every trap of the day came out of that layer.
/// They named the alternative in one breath: «Зүгээр л үндсэн хэрэглэгчийн
/// удирдлагын хэсгийг нуучихдаг болгочихмоор байхын. Компани болон төслийн
/// мэдээлэл нь л харагдахгүй. Нэг үгээр бол ӨӨР ХЭРЭГЛЭГЧ ШИГ Л БАЙНА.»
///
/// «Өөр хэрэглэгчид миний байгууллагын жагсаалт байхгүйтай адил» - so this is the
/// collaborator model the platform already has, with the administration left out.
/// </summary>
public sealed class ABOTSeesWhatAnotherUserSeesTests
{
    [Fact]
    public void ABOTDoesNotSeeTheOWNERSAdministration()
    {
        // The organisation library and the project's own information: the two
        // the owner named, and nothing else.
        Assert.False(StudioBotSurfaceVisibility.IsVisible(seatedAsBot: true, "Companies"));
        Assert.False(StudioBotSurfaceVisibility.IsVisible(seatedAsBot: true, "Foundation"));
    }

    [Fact]
    public void ABOTSTILLSeesTheSurfacesItWorksOn()
    {
        // 🔴 THE HALF THAT MAKES A SEAT WORTH HAVING. A bot exists to bring
        // sources in and get them into an album; hiding those would leave a
        // machine that can do nothing - which is the trap we spent the day
        // climbing out of, rebuilt in a different shape.
        foreach (string page in new[] { "Projects", "Sources", "Albums", "Portfolio", "Boards" })
        {
            Assert.True(
                StudioBotSurfaceVisibility.IsVisible(seatedAsBot: true, page),
                page + " is how a seat does its work and must not be hidden");
        }
    }

    [Fact]
    public void ANORDINARYMachineSeesEVERYTHING()
    {
        // The rule is about the bot, not about the page. An owner or a
        // collaborator loses nothing.
        foreach (string page in StudioBotSurfaceVisibility.AllPages)
            Assert.True(StudioBotSurfaceVisibility.IsVisible(seatedAsBot: false, page));
    }

    [Fact]
    public void EVERYPageTheShellCanShowHasBeenThoughtAbout()
    {
        // 🔴 DERIVED FROM THE SHELL'S OWN ENUM. A page added later and not
        // considered here would simply appear on a bot's screen - which is how a
        // management surface leaks in, silently, months after the rule was
        // written. This goes red instead.
        string source = ReadAppSource("ShellView.cs").Replace("\r\n", "\n");
        int at = source.IndexOf("private enum StudioPage", StringComparison.Ordinal);
        Assert.True(at > 0, "the shell's page list was not found");
        int open = source.IndexOf('{', at);
        int close = source.IndexOf('}', open);

        string[] declared = source[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        // The instrument first: a parse that found nothing would pass everything.
        Assert.True(declared.Length >= 10, "only " + declared.Length + " pages were parsed");
        Assert.Equal(
            declared.Order(StringComparer.Ordinal),
            StudioBotSurfaceVisibility.AllPages.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EVERYHiddenPageIsAREALPage()
    {
        // The other direction: a name misspelled here hides nothing and would
        // never be noticed, because the page simply keeps appearing.
        foreach (string hidden in StudioBotSurfaceVisibility.HiddenFromABot)
            Assert.Contains(hidden, StudioBotSurfaceVisibility.AllPages);
    }

    [Fact]
    public void THENavigationAsksTheRuleATTheONEPlaceItIsBuilt()
    {
        // Asked where every entry is made, not at each call site: thirteen
        // call sites means the fourteenth forgets.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private void AddNavItem(StudioPage page, string label, string iconAsset)");

        Assert.Contains(
            "StudioBotSurfaceVisibility.IsVisible(SeatedAsBot, page.ToString())",
            body,
            StringComparison.Ordinal);
    }

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
