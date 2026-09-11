using System.Text;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The officials directory, kept on this machine and never silent about it.
///
/// 🔴 THE SOURCE DECISION IS STILL THE OWNER'S AND IT DOES NOT BLOCK THIS. Nobody
/// publishes these officials today, and whether the list should be maintained on
/// the owner's machine or served by SRV is a question about WHO MAINTAINS IT. The
/// local implementation is complete either way; a served one would sit beside it.
///
/// 🔴 THREE STATES THAT LOOK IDENTICAL FROM A LOOKUP. «Nothing filled in yet»,
/// «the file would not read» and «some rows were refused» all answer «nobody
/// serves this district» - which is also what an ordinary correct answer looks
/// like. Each needs its own sentence or the person cannot act on any of them.
/// </summary>
public sealed class THEDirectoryLivesOnThisMachineTests : IDisposable
{
    private const string Bayangol = "01103";

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "erks-officials-tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(root, StudioOfficialsDirectory.FileName);

    public THEDirectoryLivesOnThisMachineTests() => Directory.CreateDirectory(root);

    [Fact]
    public void ADIRECTORYThatWasNeverFilledInSaysSOAndIsNotAFailure()
    {
        // The state the product ships in. Calling it an error would send somebody
        // looking for a broken file that is simply absent.
        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Load();

        Assert.Empty(directory.Officials(Bayangol));
        Assert.Equal(StudioOfficialsDirectory.NotFilledInMn, directory.UnavailableReasonMn);
        Assert.Equal("", directory.LossMn);
        Assert.NotEqual("", directory.SourceMn);
        Assert.Equal(0, directory.Count);
    }

    [Fact]
    public void AFILEThatCannotBeOPENEDIsNotCalledEmptyEither()
    {
        // 🔴 A SECOND WAY TO FAIL, AND IT HAD NO TEST. The one below writes
        // broken JSON, which travels through the document reader; THIS one cannot
        // be opened at all, which is a different branch entirely. A mutation that
        // made it report «you have not filled this in yet» survived the whole
        // suite, because every test was exercising the other path.
        //
        // The instruction matters: the owner's thirty-six rows may be perfectly
        // good and merely locked, and telling them to fill in a list they already
        // have is how somebody retypes work they never lost.
        File.WriteAllText(FilePath, "{ \"officials\": [] }", Encoding.UTF8);

        using (File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var locked = new StudioOfficialsDirectory(FilePath);
            locked.Load();

            Assert.NotEqual("", locked.UnavailableReasonMn);
            Assert.NotEqual(StudioOfficialsDirectory.NotFilledInMn, locked.UnavailableReasonMn);
            Assert.Contains(FilePath, locked.SourceMn, StringComparison.Ordinal);
        }

        // And once it can be opened again, the ordinary answer returns - or the
        // failure would be sticky and the owner would be chasing a fixed problem.
        var released = new StudioOfficialsDirectory(FilePath);
        released.Load();
        Assert.Equal("", released.UnavailableReasonMn);
    }

    [Fact]
    public void AFILEThatWillNotREADIsNotCalledEmpty()
    {
        // 🔴 «NOT FILLED IN» IS THE WRONG INSTRUCTION FOR A BROKEN FILE. The rows
        // may be perfectly good and unreachable, and telling the owner to fill in
        // a list they already filled in is how somebody retypes thirty-six rows
        // they still have.
        File.WriteAllText(FilePath, "{ this is not json", Encoding.UTF8);

        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Load();

        Assert.NotEqual("", directory.UnavailableReasonMn);
        Assert.NotEqual(StudioOfficialsDirectory.NotFilledInMn, directory.UnavailableReasonMn);
        Assert.Contains(FilePath, directory.SourceMn, StringComparison.Ordinal);
    }

    [Fact]
    public void AROUNDTripKeepsEveryFieldAndTheVersion()
    {
        var written = new List<OfficialsDirectoryEntry>
        {
            Entry(Bayangol, OfficialBodyKind.EmergencyManagement, "Онцгой байдлын хэлтэс", "Дарга", "Нэг"),
            Entry(Bayangol, OfficialBodyKind.UrbanPlanning, "Хот байгуулалтын алба", "Мэргэжилтэн", "Хоёр"),
        };
        var stamped = new DateTimeOffset(2026, 9, 12, 4, 5, 6, TimeSpan.Zero);

        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Save(written, stamped);

        Assert.Equal(stamped, directory.AsOfUtc);
        Assert.Equal(2, directory.Count);

        IReadOnlyList<OfficialsDirectoryEntry> read = directory.Officials(Bayangol);
        Assert.Equal("Онцгой байдлын хэлтэс", read[0].OrganizationName);
        Assert.Equal(OfficialBodyKind.UrbanPlanning, read[1].Kind);
        Assert.Equal("Хоёр", read[1].PersonName);

        // And a second reader on the same file sees the same thing - the file is
        // the record, not this instance's memory.
        var reopened = new StudioOfficialsDirectory(FilePath);
        reopened.Load();
        Assert.Equal(2, reopened.Count);
    }

    [Fact]
    public void SAVINGReportsWhatTheREADERWouldNotKeep()
    {
        // 🔴 THE SAVE RE-READS, AND THIS IS WHY. What is in memory after a save is
        // what the editor believed; what the next run sees is what the reader
        // keeps. A save that reported success from its own copy would let somebody
        // enter a row that is dropped and be told it was stored - the silence this
        // whole feature exists to avoid, arriving through the door marked success.
        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Save(
            [
                Entry(Bayangol, OfficialBodyKind.PublicHealth, "Эрүүл мэндийн төв", "Дарга", "Нэг"),
                // A ward code: stored in the file, findable by nothing.
                Entry("0110315", OfficialBodyKind.PublicHealth, "Хорооны эмнэлэг", "Эрхлэгч", "Хоёр"),
            ],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(1, directory.Count);
        Assert.NotEqual("", directory.LossMn);
        Assert.Contains("2-р мөр", directory.LossMn, StringComparison.Ordinal);
    }

    [Fact]
    public void ACLEANSaveLeavesNoStandingNotice()
    {
        // The positive control for the sentence above. A notice that is always
        // present is furniture, and furniture is not read.
        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Save(
            [Entry(Bayangol, OfficialBodyKind.PublicHealth, "Эрүүл мэндийн төв", "Дарга", "Нэг")],
            DateTimeOffset.UnixEpoch);

        Assert.Equal("", directory.LossMn);
        Assert.Equal("", directory.UnavailableReasonMn);
        Assert.Equal(1, directory.Count);
    }

    [Fact]
    public void ACOMPLETEDSaveLeavesTheFileWholeAndNoScraps()
    {
        // 🔴 A HALF-WRITTEN FILE READS AS «SOME OFFICIALS VANISHED». The write
        // goes beside the file and is moved into place, so the list on disk is
        // either the old one or the new one and never part of each.
        //
        // 🔴 THIS TEST WAS FIRST NAMED FOR A FAILURE IT NEVER CAUSED. It saved
        // successfully and checked the result, while its name claimed something
        // about an interrupted write - a stale claim that the next reader would
        // have believed. Crashing mid-move is not reachable from here; what IS
        // checkable is that a finished save leaves the file whole and the folder
        // clean, so that is what it says.
        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Save(
            [Entry(Bayangol, OfficialBodyKind.PublicHealth, "Анхны", "Дарга", "Нэг")],
            DateTimeOffset.UnixEpoch);

        // No temporary file may survive a completed save, or the folder fills
        // with copies nobody can tell apart from the real one.
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));

        // 🔴 READ BACK THROUGH THE READER, NOT BY MATCHING RAW TEXT. An earlier
        // version looked for the Mongolian name in the file's bytes and failed:
        // every store in this product escapes non-ASCII to \uXXXX, so the bytes
        // are not the place to ask whether a name survived. The reader is.
        var reopened = new StudioOfficialsDirectory(FilePath);
        reopened.Load();
        Assert.Equal("Анхны", Assert.Single(reopened.Officials(Bayangol)).OrganizationName);
    }

    [Fact]
    public void WHATIsWrittenIsWhatTheREADERAccepts()
    {
        // 🔴 THE WRITER AND THE READER ARE THE SAME CONTRACT, AND NOTHING ELSE
        // CHECKS THAT. They live in different projects and could drift apart
        // field by field, each correct on its own - and the first sign would be
        // an owner's list that saves cleanly and loads empty.
        string json = StudioOfficialsDirectory.Serialize(
            [Entry(Bayangol, OfficialBodyKind.EmergencyManagement, "Байгууллага", "Албан тушаал", "Хүн")],
            DateTimeOffset.UnixEpoch);

        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(json);

        Assert.True(read.IsUsable, read.ProblemMn);
        Assert.Empty(read.Rejected);
        OfficialsDirectoryEntry entry = Assert.Single(read.Entries);
        Assert.Equal(Bayangol, entry.UnitCode);
        Assert.Equal(OfficialBodyKind.EmergencyManagement, entry.Kind);
        Assert.Equal("Албан тушаал", entry.PositionTitle);
    }

    [Fact]
    public void EVERYKindTheProductCanStoreSurvivesAROUNDTrip()
    {
        // Derived from the enum: a kind the writer can produce and the reader
        // refuses would be a body the owner can select and cannot keep.
        var entries = Enum.GetValues<OfficialBodyKind>()
            .Select(kind => Entry(Bayangol, kind, "Байгууллага " + kind, "Албан тушаал", "Хүн"))
            .ToList();

        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Save(entries, DateTimeOffset.UnixEpoch);

        Assert.Equal("", directory.LossMn);
        Assert.Equal(entries.Count, directory.Count);
        Assert.Equal(
            Enum.GetValues<OfficialBodyKind>().ToHashSet(),
            directory.Officials(Bayangol).Select(entry => entry.Kind).ToHashSet());
    }

    [Fact]
    public void ANEmptySaveIsALISTWithNobodyInIt()
    {
        // Clearing the directory is a thing somebody may do, and it must not read
        // afterwards as «never filled in» - which would tell them to do the work
        // they just undid.
        var directory = new StudioOfficialsDirectory(FilePath);
        directory.Save([], DateTimeOffset.UnixEpoch);

        Assert.Equal(0, directory.Count);
        Assert.Equal("", directory.UnavailableReasonMn);
        Assert.Equal("", directory.LossMn);
    }

    private static OfficialsDirectoryEntry Entry(
        string unitCode,
        OfficialBodyKind kind,
        string organizationName,
        string positionTitle,
        string personName) => new()
        {
            UnitCode = unitCode,
            Kind = kind,
            OrganizationName = organizationName,
            PositionTitle = positionTitle,
            PersonName = personName,
        };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
