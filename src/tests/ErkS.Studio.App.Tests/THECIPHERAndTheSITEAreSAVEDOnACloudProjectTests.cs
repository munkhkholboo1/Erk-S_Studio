using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The general and technical design ciphers, the sheet date and the site
/// location are typed on the project page - and on a Cloud project they were
/// thrown away on «Хадгалах».
///
/// 🔴 MEASURED ON BATNASAN, 2026-10-20: the name, the cipher АХО-24/2025 and
/// «Улаанбаатар › Баянзүрх › 22-р хороо» were entered and saved; the project
/// file kept the name and nothing else. The Cloud save builds its draft from
/// what goes to the server, and these four never go there - they were written
/// only by CollectUiToProject, which that page does not call. The page then
/// re-read the project and the typed values vanished from the form too.
///
/// The view is WPF, so what it does is read from its source, as its neighbours
/// do; the rule is that both save paths call the ONE method that writes them.
/// </summary>
public sealed class THECIPHERAndTheSITEAreSAVEDOnACloudProjectTests
{
    [Fact]
    public void TheFoundationSaveWritesThemThroughTheSharedMethod()
    {
        string shell = ReadView("ShellView.cs");
        string save = Body(shell, "private async Task SaveFoundationChangesAsync()");

        Assert.Contains("ApplySelectedIdentityDetails()", save, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGeneralCollectorUsesTheSameMethod()
    {
        string collect = Body(ReadView("ShellView.cs"), "private void CollectUiToProject()");

        Assert.Contains("ApplySelectedIdentityDetails()", collect, StringComparison.Ordinal);
        Assert.DoesNotContain("project.Identity.GeneralDesignCipher =", collect, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSharedMethodWritesAllFour()
    {
        string apply = Body(ReadView("ShellView.cs"), "private bool ApplySelectedIdentityDetails()");

        Assert.Contains("generalDesignCipherBox.Text.Trim()", apply, StringComparison.Ordinal);
        Assert.Contains("project.Identity.GeneralDesignCipher = generalCipher;", apply, StringComparison.Ordinal);
        Assert.Contains("technicalDesignCipherBox.Text.Trim()", apply, StringComparison.Ordinal);
        Assert.Contains("project.Identity.TechnicalDesignCipher = technicalCipher;", apply, StringComparison.Ordinal);
        Assert.Contains("project.Identity.SheetDateUtc = sheetDate;", apply, StringComparison.Ordinal);
        Assert.Contains("basis.SiteLocation = after;", apply, StringComparison.Ordinal);
    }

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " was renamed; check this test with it");
        int next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return source[start..(next < 0 ? source.Length : next)];
    }

    private static string ReadView(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8).Replace("\r\n", "\n");
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
