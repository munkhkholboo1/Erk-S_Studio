namespace ErkS.Platform.Core;

/// <summary>
/// The kind of body an official belongs to.
///
/// 🔴 NAMED FOR WHAT THE BODY IS, NEVER FOR WHERE IT PRINTS. Which block a kind
/// lands in is a product decision that has already changed once - the concurring
/// bodies sat under a name that said «working drawing» until the owner corrected
/// it on 2026-09-06 - and a value named after a block would have had to be
/// renamed with it, taking every stored row along.
///
/// 🔴 CLOSED ON PURPOSE, THOUGH THE DATA COMES FROM OUTSIDE. Each kind carries a
/// block placement, which is a decision nobody can make from a string in a
/// downloaded file. A directory that met a kind it does not know REFUSES the row
/// rather than storing it: a pass-through would put an unplaceable official in
/// the store and leave the sheet to discover it.
/// </summary>
public enum OfficialBodyKind
{
    /// <summary>Онцгой байдлын байгууллага.</summary>
    EmergencyManagement,

    /// <summary>Эрүүл мэндийн байгууллага - the product's own word for this body
    /// since 2026-09-06, kept rather than «эрүүл ахуй», which names the subject
    /// matter rather than the office.</summary>
    PublicHealth,

    /// <summary>Хот байгуулалтын газар.</summary>
    UrbanPlanning,
}

/// <summary>
/// Where an official's row is printed on the concept cover.
/// </summary>
public enum OfficialsBlock
{
    /// <summary>ЗӨВШИЛЦСӨН - stored in ConceptDesign.ConcurredBy.</summary>
    ConcurredBy,

    /// <summary>
    /// ХЯНАСАН - the right-hand table of the upper pair, stored in
    /// ConceptDesign.ReviewedBy.
    ///
    /// 🔴 THIS BLOCK HAD NOWHERE TO BE STORED UNTIL 2026-09-12. The sheet drew it
    /// empty and said so; an official resolved into it could be found and not
    /// kept, and the test that named the gap is the one that went red when the
    /// list arrived. That is what a named gap is for - the change finishes where
    /// the gap was recorded, instead of being discovered somewhere downstream.
    /// </summary>
    ReviewedBy,
}

/// <summary>
/// One official, as some source knows them.
/// </summary>
public sealed class OfficialsDirectoryEntry
{
    /// <summary>
    /// The administrative unit this official serves, at the five-digit level.
    /// </summary>
    public string UnitCode { get; set; } = "";

    public OfficialBodyKind Kind { get; set; }

    public string OrganizationName { get; set; } = "";
    public string PositionTitle { get; set; } = "";

    /// <summary>
    /// The person, by name. May be empty: an office that is known while the
    /// person holding it is not is a real state, and printing the office with a
    /// blank line to sign is what a paper form does anyway.
    /// </summary>
    public string PersonName { get; set; } = "";

    public void Normalize()
    {
        UnitCode = (UnitCode ?? "").Trim();
        OrganizationName = (OrganizationName ?? "").Trim();
        PositionTitle = (PositionTitle ?? "").Trim();
        PersonName = (PersonName ?? "").Trim();
    }

    public OfficialsDirectoryEntry Clone() => new()
    {
        UnitCode = UnitCode,
        Kind = Kind,
        OrganizationName = OrganizationName,
        PositionTitle = PositionTitle,
        PersonName = PersonName,
    };

    /// <summary>The roster row this becomes when somebody accepts it.</summary>
    public ProjectApprovalEntry ToApprovalEntry() => new()
    {
        OrganizationName = OrganizationName,
        PositionTitle = PositionTitle,
        PersonName = PersonName,
    };
}

/// <summary>
/// Where officials come from - the PLUG.
///
/// 🔴 THE SOURCE IS THE ONLY THING THAT CHANGES. Measured by Master on
/// 2026-09-11: there is no open source to read these from today. The emergency
/// service's site lists the capital's nine district offices but publishes no
/// name, no position, no API and no downloadable list; no open data for the
/// districts' health officials was found at all.
///
/// That does not block the work, because the KEY is already settled: an
/// administrative unit code, which the project address has carried since
/// 2026-09-07. So the address, the lookup and the writing are built against this
/// interface and tested against fixtures; the day a route exists, one class
/// appears behind it and nothing above changes. This is the same shape
/// <see cref="IAdministrativeUnitCatalogue"/> already proved on the unit list.
/// </summary>
public interface IOfficialsDirectory
{
    /// <summary>
    /// Which version of the directory this is, from whoever issued it. Null
    /// while nothing has been loaded - never invented here, for the same reason
    /// the unit catalogue does not invent its own: a choice made against a
    /// three-month-old copy and one made against live data would otherwise both
    /// say «today».
    /// </summary>
    DateTimeOffset? AsOfUtc { get; }

    /// <summary>
    /// The officials serving <paramref name="unitCode"/>, in the order the
    /// source gives them. Empty when the unit is unknown - which is the ordinary
    /// state, not an error.
    /// </summary>
    IReadOnlyList<OfficialsDirectoryEntry> Officials(string? unitCode);

    /// <summary>
    /// WHY there is nothing, in the reader's language; empty when there is
    /// something. The reason belongs here because only the source knows it, and
    /// «not filled in yet» and «the download failed» are acted on differently.
    /// </summary>
    string UnavailableReasonMn { get; }
}

/// <summary>
/// A directory held in memory: whatever some source last said, plus the reason
/// when it said nothing.
/// </summary>
public sealed class OfficialsDirectorySnapshot : IOfficialsDirectory
{
    private readonly Dictionary<string, List<OfficialsDirectoryEntry>> byUnit = new(StringComparer.Ordinal);

    /// <summary>
    /// A directory with nothing in it, carrying the reason - used for every
    /// not-loaded state, because the reason is what tells them apart and the
    /// empty list is what they have in common.
    /// </summary>
    public static OfficialsDirectorySnapshot Empty(string unavailableReasonMn) =>
        new([], null, unavailableReasonMn);

    public OfficialsDirectorySnapshot(
        IEnumerable<OfficialsDirectoryEntry> entries,
        DateTimeOffset? asOfUtc,
        string unavailableReasonMn = "")
    {
        ArgumentNullException.ThrowIfNull(entries);

        AsOfUtc = asOfUtc;
        UnavailableReasonMn = unavailableReasonMn ?? "";

        var kept = 0;
        foreach (OfficialsDirectoryEntry entry in entries)
        {
            if (entry is null)
                continue;

            OfficialsDirectoryEntry copy = entry.Clone();
            copy.Normalize();
            if (copy.UnitCode.Length == 0)
                continue;

            if (!byUnit.TryGetValue(copy.UnitCode, out List<OfficialsDirectoryEntry>? rows))
            {
                rows = [];
                byUnit[copy.UnitCode] = rows;
            }

            rows.Add(copy);
            kept++;
        }

        Count = kept;
    }

    public DateTimeOffset? AsOfUtc { get; }

    public string UnavailableReasonMn { get; }

    /// <summary>
    /// How many rows this directory actually KEPT.
    ///
    /// 🔴 A ROW THAT CANNOT BE ASKED FOR MUST NOT BE KEPT, AND THE COUNT IS HOW
    /// ANYBODY CAN TELL. Dropping a malformed row into the store under an empty
    /// key hides it perfectly: no lookup can reach it, so every test that asks
    /// «can I find it» passes while the operator who loaded thirty-six rows is
    /// quietly working with thirty-five. A mutation that stored those rows
    /// survived a whole suite built on lookups alone - this is what caught it.
    /// </summary>
    public int Count { get; }

    public IReadOnlyList<OfficialsDirectoryEntry> Officials(string? unitCode)
    {
        string key = (unitCode ?? "").Trim();
        return key.Length > 0 && byUnit.TryGetValue(key, out List<OfficialsDirectoryEntry>? rows)
            ? rows
            : [];
    }
}

/// <summary>
/// Turning a project's address into the one code the directory is keyed by.
/// </summary>
public static class OfficialsLookupKey
{
    /// <summary>
    /// The unit code to look officials up by, or empty when the address cannot
    /// answer it.
    ///
    /// 🔴 THE FIVE-DIGIT LEVEL, AND NO WALKING. A sum or district is where these
    /// offices exist: a ward has none, and a province has too many to choose
    /// between. Searching UPWARD from whatever happens to be filled in would
    /// answer a province's worth of officials for a project that named only its
    /// province - a wrong answer that looks exactly like a right one.
    ///
    /// A self-contradictory address answers nothing. It is already a state the
    /// product can be in - the chain check exists because of it - and officials
    /// resolved from half of a broken address would be printed beside the other
    /// half on the same sheet.
    /// </summary>
    public static string For(ProjectSiteLocation? location)
    {
        if (location is null || !location.ChainHoldsTogether)
            return "";

        return (location.DistrictCode ?? "").Trim();
    }
}

/// <summary>
/// One official offered to a roster, and whether that roster already has them.
/// </summary>
/// <param name="Entry">The official, as the directory knows them.</param>
/// <param name="Block">Where their row belongs on the sheet.</param>
/// <param name="AlreadyPresent">
/// Whether the roster already carries this organisation. Matched by ORGANISATION,
/// not by person: the person in the chair changes and the office does not, so
/// matching on the name would offer the same office again under a new occupant.
/// </param>
public sealed record OfficialsProposalRow(
    OfficialsDirectoryEntry Entry,
    OfficialsBlock Block,
    bool AlreadyPresent);

/// <summary>
/// What the address SUGGESTS, and nothing more.
///
/// 🔴 A PROPOSAL, NEVER A WRITE. The owner settled this in the same breath as
/// asking for the feature: «Онцгой байдлын ерөнхий газар болон эрүүл мэндийн
/// яаманд хандахаар бол СОНГОДОГ байна» - the automatic sits UNDER a manual
/// choice, it does not replace it. So this returns rows and mutates nothing;
/// there is no code path here that can overwrite what somebody typed, because
/// there is no code path here that writes at all.
/// </summary>
public static class OfficialsProposal
{
    /// <summary>Which block a kind of body prints in.</summary>
    public static OfficialsBlock BlockFor(OfficialBodyKind kind) => kind switch
    {
        OfficialBodyKind.EmergencyManagement => OfficialsBlock.ConcurredBy,
        OfficialBodyKind.PublicHealth => OfficialsBlock.ConcurredBy,
        OfficialBodyKind.UrbanPlanning => OfficialsBlock.ReviewedBy,

        // 🔴 NOT A DEFAULT. A kind that reaches here has no decided placement,
        // and putting it in whichever block is listed first would print a body
        // among parties it does not belong to. The sheet is the last place that
        // should discover a value nobody placed.
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "Энэ төрлийн байгууллага аль хүснэгтэд орохыг шийдээгүй байна."),
    };

    /// <summary>
    /// The officials this address offers, each marked with where it belongs and
    /// whether the roster already has it.
    /// </summary>
    /// <param name="found">What the directory answered for the address's code.</param>
    /// <param name="concurredBy">The ЗӨВШИЛЦСӨН rows the project already carries.</param>
    /// <param name="reviewedBy">The ХЯНАСАН rows the project already carries.</param>
    public static IReadOnlyList<OfficialsProposalRow> For(
        IReadOnlyList<OfficialsDirectoryEntry>? found,
        IReadOnlyList<ProjectApprovalEntry>? concurredBy,
        IReadOnlyList<ProjectApprovalEntry>? reviewedBy = null)
    {
        if (found is null || found.Count == 0)
            return [];

        // 🔴 EACH BLOCK IS ASKED ABOUT ITS OWN LIST. One combined set would let
        // an organisation entered on one table silence the offer on the other,
        // and the two tables are different questions about the same body.
        HashSet<string> concurring = Organisations(concurredBy);
        HashSet<string> reviewing = Organisations(reviewedBy);

        var rows = new List<OfficialsProposalRow>(found.Count);
        foreach (OfficialsDirectoryEntry entry in found)
        {
            if (entry is null)
                continue;

            OfficialsBlock block = BlockFor(entry.Kind);
            HashSet<string> present = block switch
            {
                OfficialsBlock.ConcurredBy => concurring,
                OfficialsBlock.ReviewedBy => reviewing,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(found),
                    block,
                    "Энэ хүснэгтийн одоогийн мөрүүдийг хаанаас уншихыг шийдээгүй байна."),
            };

            rows.Add(new OfficialsProposalRow(
                entry.Clone(),
                block,
                present.Contains((entry.OrganizationName ?? "").Trim())));
        }

        return rows;
    }

    private static HashSet<string> Organisations(IReadOnlyList<ProjectApprovalEntry>? entries)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectApprovalEntry entry in entries ?? [])
        {
            string organisation = (entry?.OrganizationName ?? "").Trim();
            if (organisation.Length > 0)
                names.Add(organisation);
        }

        return names;
    }

    /// <summary>
    /// Whether a proposed row can actually be kept if somebody accepts it.
    ///
    /// 🔴 THE ANSWER WAS «NOT ALL OF THEM» AND IS NOW «ALL OF THEM». ХЯНАСАН was
    /// drawn on the sheet and stored nowhere, so two of the four rows the design
    /// names could be found and not saved; this method existed to keep a screen
    /// from offering a button that did nothing. Both tables now have lists, so it
    /// answers true throughout - and it stays, because the next block added to
    /// the sheet will arrive the same way: drawn first, stored later.
    /// </summary>
    public static bool CanBeStored(OfficialsProposalRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Block is OfficialsBlock.ConcurredBy or OfficialsBlock.ReviewedBy;
    }
}
