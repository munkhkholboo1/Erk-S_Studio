using System.IO;
using System.Text;
using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Studio's officials directory, kept on this machine.
///
/// 🔴 THE SOURCE IS THE ONLY OPEN QUESTION, AND IT IS NOT THIS CLASS'S. Measured
/// by Master on 2026-09-11: nothing publishes these officials today. Whether the
/// list should live on the owner's machine or be served by SRV is the owner's
/// call - «who maintains it» - and it is still with them. This is the LOCAL
/// implementation of <see cref="IOfficialsDirectory"/>, complete on its own, and
/// a served one would be a SECOND implementation beside it rather than a rewrite
/// of anything above.
///
/// The shape is taken from <see cref="StudioAdministrativeUnitCatalogue"/>, which
/// proved it: every rule above the plug was finished and tested against fixtures
/// before the first request existed, and none of it changed when the route
/// arrived.
///
/// 🔴 AND THE STATE IS NEVER SILENT. Three things can be true at once - nothing
/// has been filled in, the file would not read, some rows were refused - and
/// they are acted on differently. Each has its own sentence; none of them is an
/// empty list on its own, because an empty list is also what «this district has
/// nobody» looks like.
/// </summary>
internal sealed class StudioOfficialsDirectory : IOfficialsDirectory
{
    public const string FileName = "officials.json";

    internal static readonly string NotFilledInMn =
        "Албан тушаалтны лавлах хараахан бөглөгдөөгүй байна. " +
        "Хаягаар албан тушаалтан санал болгохын тулд нэг удаа бөглөнө.";

    private readonly string filePath;
    private OfficialsDirectorySnapshot current =
        OfficialsDirectorySnapshot.Empty(NotFilledInMn);

    /// <summary>
    /// The one the application uses. A single instance, so the file is read once
    /// per run and every screen sees the same list, the same version and the same
    /// account of what was lost.
    /// </summary>
    public static StudioOfficialsDirectory Live { get; } = new();

    private StudioOfficialsDirectory()
        : this(Path.Combine(StudioAccountService.AccountDataRoot, FileName))
    {
    }

    internal StudioOfficialsDirectory(string filePath) => this.filePath = filePath;

    public DateTimeOffset? AsOfUtc => current.AsOfUtc;

    public string UnavailableReasonMn => current.UnavailableReasonMn;

    public string LossMn => current.LossMn;

    /// <summary>How many officials are actually answerable.</summary>
    public int Count => current.Count;

    /// <summary>
    /// Every row the file gave and the reader kept - the EDITOR's question,
    /// never a lookup's. Copies, so a screen that edits them changes nothing
    /// until it saves.
    /// </summary>
    public IReadOnlyList<OfficialsDirectoryEntry> All => current.All;

    /// <summary>
    /// Where the list on screen came from, in words - empty before anything has
    /// been attempted.
    ///
    /// Kept rather than worked out at the moment somebody asks: by then the only
    /// visible fact is that a list exists, and «the file on this machine» and
    /// «nothing has been read yet» would be a guess between two true-sounding
    /// sentences. The unit catalogue learned this the same way.
    /// </summary>
    public string SourceMn { get; private set; } = "";

    public IReadOnlyList<OfficialsDirectoryEntry> Officials(string? unitCode) =>
        current.Officials(unitCode);

    /// <summary>
    /// Read the file. Never throws: a sentence in the reader's language is the
    /// product here, the same rule the catalogue holds.
    /// </summary>
    public void Load()
    {
        string json;
        try
        {
            if (!File.Exists(filePath))
            {
                // 🔴 NOT AN ERROR. This is the state the product ships in - the
                // owner has not filled the list in yet - and calling it a failure
                // would send somebody looking for a broken file that is simply
                // absent.
                current = OfficialsDirectorySnapshot.Empty(NotFilledInMn);
                SourceMn = "Энэ төхөөрөмж дээр лавлах үүсээгүй байна.";
                return;
            }

            json = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that exists and cannot be read is NOT «not filled in»: the
            // rows may be perfectly good and unreachable, and telling the owner
            // to fill in a list they already filled in is the wrong instruction.
            current = OfficialsDirectorySnapshot.Empty(
                "Албан тушаалтны лавлах уншигдсангүй: " + exception.Message);
            SourceMn = "Файл уншигдсангүй: " + filePath;
            return;
        }

        OfficialsDirectoryRead read = OfficialsDirectoryDocument.Read(json);
        current = OfficialsDirectorySnapshot.From(read);
        SourceMn = read.IsUsable
            ? "Энэ төхөөрөмж дээрх лавлахаас уншсан."
            : "Файл олдсон ч уншигдсангүй: " + filePath;
    }

    /// <summary>
    /// Write the list back, then READ IT AGAIN.
    ///
    /// 🔴 THE RE-READ IS THE POINT, NOT A PRECAUTION. What is in memory after a
    /// save is what the editor believed; what the next run will see is what the
    /// file says, and the reader refuses rows. A save that reported success from
    /// the editor's own copy would let somebody enter a row the reader will drop
    /// and be told it was kept - the silence this whole feature was built to
    /// avoid, arriving through the one door that looks like success.
    /// </summary>
    public void Save(IReadOnlyList<OfficialsDirectoryEntry> entries, DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string json = Serialize(entries, asOfUtc);
        string directory = Path.GetDirectoryName(filePath) ?? "";
        if (directory.Length > 0)
            Directory.CreateDirectory(directory);

        // Written beside and moved into place: a crash halfway through a direct
        // write leaves a truncated list that reads as «some officials vanished».
        string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, filePath, overwrite: true);

        Load();
    }

    internal static string Serialize(
        IReadOnlyList<OfficialsDirectoryEntry> entries,
        DateTimeOffset asOfUtc)
    {
        var rows = new List<object>(entries.Count);
        foreach (OfficialsDirectoryEntry entry in entries)
        {
            if (entry is null)
                continue;

            OfficialsDirectoryEntry copy = entry.Clone();
            copy.Normalize();
            rows.Add(new
            {
                unitCode = copy.UnitCode,

                // The enum's own name, in one spelling. A second accepted
                // spelling is a second thing to keep in step, and the reader
                // refuses what it does not know rather than guessing.
                kind = copy.Kind.ToString(),
                organizationName = copy.OrganizationName,
                positionTitle = copy.PositionTitle,
                personName = copy.PersonName,
            });
        }

        return JsonSerializer.Serialize(
            new { asOfUtc, officials = rows },
            new JsonSerializerOptions { WriteIndented = true });
    }
}
