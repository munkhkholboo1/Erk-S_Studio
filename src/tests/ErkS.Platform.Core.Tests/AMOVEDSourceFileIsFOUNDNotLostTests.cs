using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// 2026-10-24: the owner renamed «D:\Cloud Work\TananTsamkhag\Nairamdal» to
/// «…\NairamdalApartment\RVT» and the project kept looking for its two Revit files at the old
/// place - «эх файл олдсонгүй». A moved file is found by its name under the nearest folder that
/// still exists; one match is the answer, several are a question, nothing is touched on disk.
/// </summary>
public sealed class AMOVEDSourceFileIsFOUNDNotLostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "erks-relocation-tests", Guid.NewGuid().ToString("N"));

    public AMOVEDSourceFileIsFOUNDNotLostTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void AFileMovedIntoARenamedFolderIsFoundOnce()
    {
        string moved = Write(@"TananTsamkhag\NairamdalApartment\RVT\NP-Apartment-Sheet.rvt");
        Write(@"TananTsamkhag\NairamdalApartment\RVT\NP-Apartment-Sheet_backup\NP-Apartment-Sheet.rvt"); // Revit's own copy

        IReadOnlyList<string> found = ProjectSourceRelocation.FindCandidates(Path.Combine(_root, @"TananTsamkhag\Nairamdal\NP-Apartment-Sheet.rvt"));

        Assert.Equal([moved], found);
    }

    [Fact]
    public void TwoSameNamedFilesAreAQuestionNotAnAnswer()
    {
        Write(@"TananTsamkhag\A\Plan.dwg");
        Write(@"TananTsamkhag\B\Plan.dwg");

        Assert.Equal(2, ProjectSourceRelocation.FindCandidates(Path.Combine(_root, @"TananTsamkhag\Old\Plan.dwg")).Count);
    }

    [Fact]
    public void AFileStillWhereItWasNeedsNothing()
    {
        string here = Write(@"P\Plan.dwg");

        Assert.Empty(ProjectSourceRelocation.FindCandidates(here));
    }

    [Fact]
    public void TheProjectsSyncFolderIsSearchedToo()
    {
        string sync = Directory.CreateDirectory(Path.Combine(_root, "Sync")).FullName;
        string moved = Write(@"Sync\RVT\Model.rvt");

        IReadOnlyList<string> found = ProjectSourceRelocation.FindCandidates(@"Q:\gone\Model.rvt", [sync]);

        Assert.Equal([moved], found);
    }

    private string Write(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }
}
