namespace ErkS.Platform.Core;

/// <summary>
/// Project-document approval metadata. These entries describe what is printed
/// on an album; they are deliberately separate from Cloud ERA team membership.
/// </summary>
public sealed class ProjectApprovalWorkflow
{
    /// <summary>Загвар зургийн шатны БАТЛАВ болон ЗӨВШӨӨРӨЛЦСӨН хэсэг.</summary>
    public ConceptDesignApprovalRoster ConceptDesign { get; set; } = new();

    /// <summary>
    /// ХУУЧИН НЭР, зөвхөн уншихад. Шинэ бичилт <see cref="ConceptDesignApprovalRoster.ConcurredBy"/>
    /// руу орно, ба <see cref="Normalize"/> энд байсан мөрүүдийг тийш нь зөөнө.
    ///
    /// Нэр нь БУРУУ ШАТЫГ заасан байв: «ажлын зургийн» гэж. Хэрэглэгч
    /// 2026-09-06-нд тодруулав — онцгой байдал, эрүүл мэндийн байгууллагууд
    /// нь **загвар зургийг** зөвшилцдөг, ажлын зургийг биш. Нэр нь буруу
    /// байсан тул түүн дээр тулгуурласан бүх шийдвэр (нүүрэнд орох эсэх,
    /// аль шатны багцад хадгалагдах) буруу тийш нь чиглэсэн.
    ///
    /// Диск дээрх 24 төслийн аль нь ч энэ талбарыг агуулаагүй (хэмжсэн:
    /// approvalWorkflow түлхүүр огт бичигдээгүй) тул зөөх нь өгөгдөл
    /// алдагдуулахгүй. Уншилтын зам үлдээсэн нь энд харагдаагүй файлуудад
    /// зориулсан.
    /// </summary>
    public List<ProjectApprovalEntry> WorkingDrawingConsultedBy { get; set; } = [];

    public ProjectApprovalWorkflow Clone() => new()
    {
        ConceptDesign = ConceptDesign.Clone(),
        WorkingDrawingConsultedBy = WorkingDrawingConsultedBy
            .Select(entry => entry.Clone())
            .ToList(),
    };

    public void Normalize()
    {
        ConceptDesign ??= new ConceptDesignApprovalRoster();
        WorkingDrawingConsultedBy ??= [];

        // ONE-WAY MOVE, not a fallback. Rows written under the old name belong
        // to the concept roster; once moved they are gone from here, so the two
        // lists can never both hold a copy and disagree.
        if (WorkingDrawingConsultedBy.Count > 0)
        {
            ConceptDesign.ConcurredBy.AddRange(WorkingDrawingConsultedBy);
            WorkingDrawingConsultedBy.Clear();
        }

        ConceptDesign.Normalize();
    }

    internal static void NormalizeEntries(List<ProjectApprovalEntry> entries)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (entries[index] is null)
            {
                entries.RemoveAt(index);
                continue;
            }

            entries[index].Normalize();
        }
    }
}

public sealed class ConceptDesignApprovalRoster
{
    /// <summary>
    /// False means an older project has not configured this roster yet and the
    /// cover may derive a compatibility view from PlanningTask.AuthorityMembers.
    /// </summary>
    public bool IsConfigured { get; set; }

    /// <summary>БАТЛАВ: one or more chief architects, in printed order.</summary>
    public List<ProjectApprovalEntry> ApprovedBy { get; set; } = [];

    /// <summary>ЗӨВШӨӨРӨЛЦСӨН: concept-stage officials, in printed order.</summary>
    public List<ProjectApprovalEntry> EndorsedBy { get; set; } = [];

    /// <summary>
    /// ЗӨВШИЛЦСӨН: the bodies that concur with the CONCEPT design - emergency
    /// management, health, and where a protected area is used, environment.
    ///
    /// It sat outside this roster under a name that said "working drawing",
    /// and the user corrected that on 2026-09-06: these bodies concur with the
    /// concept, not the working drawings. The name decided where the rows were
    /// stored and whether they reached the cover, so both were wrong with it.
    /// </summary>
    public List<ProjectApprovalEntry> ConcurredBy { get; set; } = [];

    /// <summary>
    /// ХЯНАСАН: the right-hand table of the concept cover's upper pair - the
    /// urban-planning office's two officials.
    ///
    /// 🔴 THE TABLE WAS DRAWN AND STORED NOWHERE. The writer said so where it
    /// drew it empty, and the reason it stayed empty was sound: the nearest list,
    /// ЗӨВШӨӨРӨЛЦСӨН, means something else, and a form printed with the wrong
    /// parties is worse than one printed blank. What was missing was a list of
    /// its own, which is this.
    ///
    /// 🔴 TWO PLACES, AND THE VARYING COUNT IS STILL DEFERRED. The owner named
    /// exactly two - «нөгөө талд хот байгуулалтын газрын 2 албан тушаалтан тэгээд
    /// л болоо» - and the measured A3 agrees: twoTablePairs.top.rightRowHeightsMm
    /// is [20.0, 20.0]. So the sheet draws two. Nothing here builds the
    /// varying-row machinery that was deferred in c22dc93; when that question is
    /// answered it will be answered with a drawing, not from this field.
    ///
    /// ADDITIVE ONLY. A project file without this key reads as an empty list, no
    /// migration runs, and nothing already written moves. The cost is measured
    /// and stated rather than hidden: the key joins the serialised project, so
    /// AlbumBuildFingerprint changes once for every existing project and every
    /// album redraws ONCE. After that redraw the stored fingerprint matches again
    /// and №13's rule holds - no rebuild without a change.
    /// </summary>
    public List<ProjectApprovalEntry> ReviewedBy { get; set; } = [];

    public ConceptDesignApprovalRoster Clone() => new()
    {
        IsConfigured = IsConfigured,
        ApprovedBy = ApprovedBy.Select(entry => entry.Clone()).ToList(),
        EndorsedBy = EndorsedBy.Select(entry => entry.Clone()).ToList(),
        ConcurredBy = ConcurredBy.Select(entry => entry.Clone()).ToList(),
        ReviewedBy = ReviewedBy.Select(entry => entry.Clone()).ToList(),
    };

    public void Normalize()
    {
        ApprovedBy ??= [];
        EndorsedBy ??= [];
        ConcurredBy ??= [];
        ReviewedBy ??= [];
        ProjectApprovalWorkflow.NormalizeEntries(ApprovedBy);
        ProjectApprovalWorkflow.NormalizeEntries(EndorsedBy);
        ProjectApprovalWorkflow.NormalizeEntries(ConcurredBy);

        // 🔴 NOT TRUNCATED HERE. Two is what the sheet draws, but silently
        // dropping a third row somebody typed would destroy their work on save,
        // which is a worse answer than a row that does not fit. The cap belongs
        // where rows are entered, in sight of the person entering them.
        ProjectApprovalWorkflow.NormalizeEntries(ReviewedBy);
    }
}

public sealed class ProjectApprovalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationName { get; set; } = "";
    public string PositionTitle { get; set; } = "";
    public string PersonName { get; set; } = "";

    /// <summary>
    /// When true, this concept-stage endorsed official is printed in the
    /// facade-sheet HYaNAV block. BATLAV rows are resolved from ApprovedBy and
    /// do not need this flag.
    /// </summary>
    public bool IncludeInElevationHeader { get; set; }

    public ProjectApprovalEntry Clone() => new()
    {
        Id = Id,
        OrganizationName = OrganizationName,
        PositionTitle = PositionTitle,
        PersonName = PersonName,
        IncludeInElevationHeader = IncludeInElevationHeader,
    };

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");
        OrganizationName = OrganizationName?.Trim() ?? "";
        PositionTitle = PositionTitle?.Trim() ?? "";
        PersonName = PersonName?.Trim() ?? "";
    }
}

public static class ProjectApprovalRosterLimits
{
    public const int MinApprovedBy = 1;
    public const int MaxApprovedBy = 3;
    public const int MinEndorsedBy = 2;
    public const int MaxEndorsedBy = 6;

    /// <summary>
    /// ЗӨВШИЛЦСӨН has no floor: a project may need none, and the 2026 concept
    /// cover shows the table with two rows and with three. The ceiling is the
    /// drawing's own - its body is a fixed 40 mm, so past a handful of rows the
    /// text stops being readable rather than the table growing.
    /// </summary>
    public const int MinConcurredBy = 0;
    public const int MaxConcurredBy = 6;
}

public sealed record ConceptCoverApprovalSnapshot(
    IReadOnlyList<ProjectApprovalEntry> ApprovedBy,
    IReadOnlyList<ProjectApprovalEntry> EndorsedBy);

public sealed record ConceptElevationHeaderSnapshot(
    IReadOnlyList<ProjectApprovalEntry> ApprovedBy,
    IReadOnlyList<ProjectApprovalEntry> ReviewedBy);

/// <summary>
/// Resolves facade-sheet officials from the ATD roster. The cover's BATLAV
/// officials are reused verbatim; HYaNAV contains only endorsed officials the
/// user explicitly selected in the ATD editor.
/// </summary>
public static class ConceptElevationHeaderResolver
{
    public static ConceptElevationHeaderSnapshot Resolve(
        ProjectApprovalWorkflow? workflow,
        PlanningTaskInformation? planningTask)
    {
        ConceptCoverApprovalSnapshot cover = ConceptCoverApprovalResolver.Resolve(
            workflow,
            planningTask);
        return new ConceptElevationHeaderSnapshot(
            cover.ApprovedBy,
            cover.EndorsedBy
                .Where(entry => entry.IncludeInElevationHeader)
                .Select(entry => entry.Clone())
                .ToList());
    }
}

/// <summary>
/// Resolves the exact concept-cover rows while preserving legacy projects.
/// </summary>
public static class ConceptCoverApprovalResolver
{
    public static ConceptCoverApprovalSnapshot Resolve(
        ProjectApprovalWorkflow? workflow,
        PlanningTaskInformation? planningTask)
    {
        ConceptDesignApprovalRoster? configured = workflow?.ConceptDesign;
        if (configured?.IsConfigured == true)
        {
            return new ConceptCoverApprovalSnapshot(
                NormalizeCount(
                    configured.ApprovedBy,
                    ProjectApprovalRosterLimits.MinApprovedBy,
                    ProjectApprovalRosterLimits.MaxApprovedBy,
                    defaultPosition: "Ерөнхий архитектор"),
                NormalizeCount(
                    configured.EndorsedBy,
                    ProjectApprovalRosterLimits.MinEndorsedBy,
                    ProjectApprovalRosterLimits.MaxEndorsedBy));
        }

        List<ProjectMember> members = planningTask?.AuthorityMembers ?? [];
        List<ProjectMember> approvedMembers = members
            .Where(IsChiefArchitect)
            .Take(ProjectApprovalRosterLimits.MaxApprovedBy)
            .ToList();
        IReadOnlyList<ProjectApprovalEntry> approvedBy = NormalizeCount(
            approvedMembers.Select(ToLegacyEntry),
            ProjectApprovalRosterLimits.MinApprovedBy,
            ProjectApprovalRosterLimits.MaxApprovedBy,
            defaultPosition: "Ерөнхий архитектор");

        HashSet<string> approvedIds = approvedMembers
            .Select(member => member.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ProjectApprovalEntry> endorsedBy = NormalizeCount(
            members
                .Where(member => !approvedMembers.Contains(member) && !approvedIds.Contains(member.Id))
                .Where(member => member.Roles.Count > 0 || !string.IsNullOrWhiteSpace(member.FullName))
                .Select(ToLegacyEntry),
            ProjectApprovalRosterLimits.MinEndorsedBy,
            ProjectApprovalRosterLimits.MaxEndorsedBy);

        return new ConceptCoverApprovalSnapshot(approvedBy, endorsedBy);
    }

    public static string DisplayPosition(ProjectApprovalEntry? entry)
    {
        if (entry is null)
            return "";

        string organization = entry.OrganizationName?.Trim() ?? "";
        string position = entry.PositionTitle?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(organization))
            return position;
        if (string.IsNullOrWhiteSpace(position))
            return organization;
        if (organization.Contains(position, StringComparison.OrdinalIgnoreCase) ||
            position.Contains(organization, StringComparison.OrdinalIgnoreCase))
        {
            return organization.Length >= position.Length ? organization : position;
        }

        return organization + Environment.NewLine + position;
    }

    private static IReadOnlyList<ProjectApprovalEntry> NormalizeCount(
        IEnumerable<ProjectApprovalEntry>? source,
        int minimum,
        int maximum,
        string defaultPosition = "")
    {
        List<ProjectApprovalEntry> entries = (source ?? [])
            .Where(entry => entry is not null)
            .Take(maximum)
            .Select(entry => entry.Clone())
            .ToList();
        foreach (ProjectApprovalEntry entry in entries)
            entry.Normalize();

        while (entries.Count < minimum)
        {
            entries.Add(new ProjectApprovalEntry
            {
                PositionTitle = entries.Count == 0 ? defaultPosition : "",
            });
        }

        return entries;
    }

    /// <summary>
    /// Whether an authority member is the chief architect.
    ///
    /// The role CODE is tried first, through the same normaliser the rest of
    /// the product uses: "MajorArchitect" - the value actually stored for design
    /// company members - matched none of the spellings below, because they all
    /// carry a space. A matcher that only recognises display text is the defect
    /// PFR measured, where «Erk-S Стандарт» could never meet «Erk-S Standard».
    ///
    /// The display spellings stay, INCLUDING the Mongolian one, and that is
    /// deliberate: these are ATD authority members, and no project on this disk
    /// has any stored, so their vocabulary is unmeasured. Removing a matcher
    /// whose real inputs nobody has seen would be trading a known-safe widening
    /// for an unknown loss.
    /// </summary>
    private static bool IsChiefArchitect(ProjectMember member) => member.Roles.Any(role =>
        ProjectRoleSemantics.IsAppointedArchitect(role) ||
        role.Contains("Chief Architect", StringComparison.OrdinalIgnoreCase) ||
        role.Contains("Major Architect", StringComparison.OrdinalIgnoreCase) ||
        role.Contains("Ерөнхий архитектор", StringComparison.OrdinalIgnoreCase));

    private static ProjectApprovalEntry ToLegacyEntry(ProjectMember member) => new()
    {
        Id = string.IsNullOrWhiteSpace(member.Id) ? Guid.NewGuid().ToString("N") : member.Id,
        PositionTitle = string.Join(", ", member.Roles
            .Select(DisplayLegacyRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)),
        PersonName = MongolianPersonNameFormatter.ForDisplay(
            member.FamilyName,
            member.GivenName,
            member.FullName),
    };

    private static string DisplayLegacyRole(string role)
    {
        if (role.Contains("Chief Architect", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("Major Architect", StringComparison.OrdinalIgnoreCase))
            return "Ерөнхий архитектор";
        if (role.Contains("Department Head", StringComparison.OrdinalIgnoreCase))
            return "Хэлтсийн дарга";
        if (role.Contains("Authority Specialist", StringComparison.OrdinalIgnoreCase))
            return "Хот байгуулалтын мэргэжилтэн";
        return role;
    }
}
