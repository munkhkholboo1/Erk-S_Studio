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
    public void ABOTSeesEVERYSurfaceAndIsStoppedAtTheACTION()
    {
        // 🔴 THE OWNER REVERSED THIS, DELIBERATELY AND TWICE (2026-09-12). First:
        // «компани цэсийг алга болгох заавал шаардлага байхгүй. идэвхгүй байхад л
        // болно». Then, watching the create button ask for a passport: «ботоос шинэ
        // төсөл үүсгэх дарж болж байна. гэхдээ үндсэн эзэмшигчээр нэвтрэхийг
        // шаардаж байна. энэ маш зөв үйлдэл». So the seat is stopped at the ACTION,
        // and the surface stays where it is.
        //
        // 🔴 ASSERTED AS EMPTY ON PURPOSE, WITH ITS REASON. An empty list makes
        // every other assertion here vacuous, which is exactly how somebody
        // «restores» the hiding next month and quietly undoes a decision. This test
        // is the record: the list is empty because the owner chose that.
        Assert.Empty(StudioBotSurfaceVisibility.HiddenFromABot);

        foreach (string page in StudioBotSurfaceVisibility.AllPages)
        {
            Assert.True(
                StudioBotSurfaceVisibility.IsVisible(actingAsBot: true, page),
                page + " is hidden from a seat, which the owner ruled against");
        }
    }

    [Fact]
    public void CONTENTIsStillWithheldAndTHATIsADifferentQuestion()
    {
        // ⚠ THE HALF THAT DID NOT CHANGE: «компани болон төслийн мэдээлэл нь л
        // харагдахгүй». The Companies page opens on a seat and holds no
        // organisations. Surface is not content - and the project LIST is the
        // content rule anyone can check from here.
        var assignedNothing = new HashSet<string>(StringComparer.Ordinal);

        Assert.False(
            StudioBotProjectVisibility.IsVisible(actingAsBot: true, assignedNothing, "p1"),
            "a seat assigned nothing was shown a project");
        Assert.True(
            StudioBotProjectVisibility.IsVisible(actingAsBot: false, null, "p1"),
            "an owner lost their own project");
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
                StudioBotSurfaceVisibility.IsVisible(actingAsBot: true, page),
                page + " is how a seat does its work and must not be hidden");
        }
    }

    [Fact]
    public void ANORDINARYMachineSeesEVERYTHING()
    {
        // The rule is about the bot, not about the page. An owner or a
        // collaborator loses nothing.
        foreach (string page in StudioBotSurfaceVisibility.AllPages)
            Assert.True(StudioBotSurfaceVisibility.IsVisible(actingAsBot: false, page));
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
        //
        // ⚠ VACUOUS TODAY - the list is empty by the owner's decision, asserted as
        // such in ABOTSeesEVERYSurfaceAndIsStoppedAtTheACTION. Kept because the day
        // one surface genuinely must go, a typo in its name would hide nothing and
        // nobody would notice; the check costs one line and waits.
        foreach (string hidden in StudioBotSurfaceVisibility.HiddenFromABot)
            Assert.Contains(hidden, StudioBotSurfaceVisibility.AllPages);
    }

    [Fact]
    public void THENavigationAsksTheRuleATTheONEPlaceItIsBuilt()
    {
        // Asked where every entry is made, not at each call site: thirteen
        // call sites means the fourteenth forgets.
        //
        // 🔴 AND IT IS ASKED WITH THE ACTOR, NOT THE MACHINE. «Seated» stays true
        // while the owner signs in on the same computer, so passing it here hid the
        // owner's own administration from the owner. This assertion changed from
        // SeatedAsBot to ActingAsBot on the day that was found; changing it back
        // would restore the fault.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private void AddNavItem(StudioPage page, string label, string iconAsset)");

        Assert.Contains(
            "StudioBotSurfaceVisibility.IsVisible(ActingAsBot, page.ToString())",
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
