using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The working-drawing corner table drew «ЕГ шифр:» and «ТГ шифр:» - and a date
/// label - with nothing beside them. Three cells that read as a rendering fault
/// and were in fact three fields nobody had, on this side and in Revit alike.
///
/// All three are ENTERED. A cipher is issued outside Studio, and a sheet date
/// taken from the clock at draw time would change every time the album was
/// rebuilt, turning a document date into "whenever this was last regenerated".
/// </summary>
public sealed class ProjectSheetIdentityTests
{
    [Fact]
    public void AFreshProjectHasNoCipherAndNoSheetDate()
    {
        // Empty means "not entered", and the cell stays empty. Studio must not
        // invent an official code, and the project code is not one: it is
        // Studio's own number.
        var identity = new ProjectIdentity();

        Assert.Equal("", identity.GeneralDesignCipher);
        Assert.Equal("", identity.TechnicalDesignCipher);
        Assert.Null(identity.SheetDateUtc);
    }

    [Fact]
    public void TheCipherIsNotTheProjectCode()
    {
        // They were conflated in the tooltip - "this value is used for the ЕГ
        // шифр" - while the cell stayed blank. Separate fields, separate
        // meanings.
        var identity = new ProjectIdentity
        {
            Code = "STUDIO-20260722-1906",
            GeneralDesignCipher = "УБ-24/117",
        };

        Assert.NotEqual(identity.Code, identity.GeneralDesignCipher);
    }

    [Fact]
    public void TheSheetDateSurvivesASaveAndLoadUnchanged()
    {
        // The point of storing it: a rebuild must not move it.
        string path = Path.Combine(
            Path.GetTempPath(),
            "erks-sheet-identity-" + Guid.NewGuid().ToString("N"),
            "project.erksproject");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var chosen = new DateTimeOffset(2026, 3, 14, 0, 0, 0, TimeSpan.Zero);

        try
        {
            var project = new ProjectWorkspace();
            project.Identity.GeneralDesignCipher = "УБ-24/117";
            project.Identity.TechnicalDesignCipher = "ТГ-24/9";
            project.Identity.SheetDateUtc = chosen;
            ProjectWorkspaceStore.Save(project, path);

            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);

            Assert.Equal("УБ-24/117", loaded.Identity.GeneralDesignCipher);
            Assert.Equal("ТГ-24/9", loaded.Identity.TechnicalDesignCipher);
            Assert.Equal(chosen, loaded.Identity.SheetDateUtc);
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void AProjectFileWrittenBeforeTheseFieldsExistedStillLoads()
    {
        // Additive by construction: every project on disk predates them, and
        // must open with the three empty rather than fail to open at all.
        string path = Path.Combine(
            Path.GetTempPath(),
            "erks-sheet-identity-legacy-" + Guid.NewGuid().ToString("N"),
            "project.erksproject");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            File.WriteAllText(path, """
            {
              "projectId": "p1",
              "identity": { "name": "Хуучин төсөл", "code": "OLD-1" }
            }
            """);

            ProjectWorkspace loaded = ProjectWorkspaceStore.Load(path);

            Assert.Equal("OLD-1", loaded.Identity.Code);
            Assert.Equal("", loaded.Identity.GeneralDesignCipher);
            Assert.Null(loaded.Identity.SheetDateUtc);
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
