using System.Reflection;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The dialog a user meets on the worst day this product has.
///
/// The old one showed three fields out of twenty and told the user their work
/// had been preserved. On the project that prompted this, all three were empty,
/// so it asserted a rescue and then showed a blank comparison - and the user
/// concluded, reasonably, that everything they had typed was gone. These pin
/// that it now reads every field the pending update carries and separates what
/// is still on screen from what a colleague's edit replaced there.
/// </summary>
public sealed class ProjectInformationConflictReportTests
{
    private const string ServerProjectId = "c59b2a4ce1cd4657b025a826223c6a5a";

    /// <summary>
    /// Machinery rather than something anyone typed.
    /// </summary>
    private static readonly string[] NotUserContent = ["BaseConcurrencyToken"];

    /// <summary>
    /// Classification chosen when the project was created, shown elsewhere and
    /// absent from the server snapshot - a row for it could only ever compare a
    /// value against nothing.
    /// </summary>
    private static readonly string[] NotOnTheInformationPage =
        ["ProjectType", "StageType", "CapacityUnit"];

    /// <summary>
    /// Second names for a field already reported. Each of these lands in the
    /// same local box as its partner, so reporting both would put two rows on
    /// screen that can never disagree.
    /// </summary>
    private static readonly string[] SecondNameForAnotherField =
        ["Location", "BuildingPurpose", "AtdAuthorityName"];

    [Fact]
    public void EveryFieldOfThePendingUpdateIsReportedOrDeliberatelyNot()
    {
        // The failure last time was a comparison that quietly covered a
        // fraction of the data. Counting by reflection means a field added
        // later cannot slip past unreported: it either gets a row or it gets
        // named in one of the three lists above, with a reason.
        PendingProjectInformationUpdate pending = FilledPending();
        IReadOnlyList<ProjectInformationConflictField> fields =
            ProjectInformationConflictReport.Compare(pending, Project(), Snapshot());

        List<string> carried =
        [
            .. TextProperties(typeof(PendingProjectInformationUpdate)).Select(p => p.Name),
            .. TextProperties(typeof(ProjectServerFoundationUpdate)).Select(p => p.Name),
        ];
        string[] accountedFor =
            [.. NotUserContent, .. NotOnTheInformationPage, .. SecondNameForAnotherField];

        // A misspelling in those lists would silently change the arithmetic
        // and let a real field go unreported, which is the exact bug again.
        foreach (string name in accountedFor)
            Assert.Contains(name, carried);

        Assert.Equal(carried.Count - accountedFor.Length, fields.Count);
    }

    [Fact]
    public void AFieldFilledUnderItsSecondNameStillReaches()
    {
        // Older queued updates carry the address as Location rather than
        // Foundation.SiteAddress. Reading only one of the two names would drop
        // exactly the value this dialog exists to account for.
        var pending = new PendingProjectInformationUpdate
        {
            Location = "Эмээлт, 3-р хэсэг",
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Foundation = new ProjectServerFoundationUpdate { IsAvailable = true },
        };
        ProjectWorkspace project = Project();
        project.Foundation.InitiationBasis.SiteAddress = "Эмээлт, 3-р хэсэг";

        string message = ProjectInformationConflictReport.Describe(pending, project, Snapshot());

        Assert.Contains("Талбайн хаяг: Эмээлт, 3-р хэсэг", message);
    }

    [Fact]
    public void WhatTheUserTypedAndStillSeesIsNamed()
    {
        PendingProjectInformationUpdate pending = FilledPending();
        ProjectWorkspace project = Project();
        project.Foundation.InitiationBasis.SiteAddress = "Эмээлт, 3-р хэсэг";
        project.Foundation.PlanningTask.AtdNumber = "АТД-2026-114";

        string message = ProjectInformationConflictReport.Describe(pending, project, Snapshot());

        Assert.Contains("Талбайн хаяг: Эмээлт, 3-р хэсэг", message);
        Assert.Contains("АТД-ийн дугаар: АТД-2026-114", message);
    }

    [Fact]
    public void AValueAColleagueReplacedOnScreenIsNamedAsReplaced()
    {
        // This is the one thing the old dialog could never say, and the only
        // thing that explains a page which no longer matches what was typed.
        PendingProjectInformationUpdate pending = FilledPending();
        ProjectWorkspace project = Project();
        project.Foundation.InitiationBasis.ClientName = "Хамтрагчийн оруулсан";

        string message = ProjectInformationConflictReport.Describe(pending, project, Snapshot());

        Assert.Contains("ДЭЛГЭЦЭД СЕРВЕРИЙНХЭЭР СОЛИГДСОН", message);
        Assert.Contains("Захиалагч: таных «З.Нэр» → одоо «Хамтрагчийн оруулсан»", message);
        Assert.Contains("устаагүй", message);
    }

    [Fact]
    public void AReplacedValueIsNotAlsoCountedAsKept()
    {
        PendingProjectInformationUpdate pending = FilledPending();
        ProjectWorkspace project = Project();
        project.Foundation.InitiationBasis.ClientName = "Хамтрагчийн оруулсан";

        ProjectInformationConflictField client = Assert.Single(
            ProjectInformationConflictReport.Compare(pending, project, Snapshot()),
            field => field.Label == "Захиалагч");

        Assert.True(client.Replaced);
        Assert.False(client.Kept);
    }

    [Fact]
    public void AnEmptyPendingUpdateSaysSoRatherThanShowingAnEmptyList()
    {
        // The user's own project reached exactly this state: a version clash
        // with nothing filled in behind it. Promising a rescue here and then
        // listing nothing is what made the old dialog read as data loss.
        var pending = new PendingProjectInformationUpdate
        {
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Foundation = new ProjectServerFoundationUpdate { IsAvailable = true },
        };

        string message = ProjectInformationConflictReport.Describe(pending, Project(), Snapshot());

        Assert.Contains("бөглөсөн талбар алга", message);
        Assert.DoesNotContain("ХЭВЭЭР БАЙГАА", message);
    }

    [Fact]
    public void TheStatusLineCountsInsteadOfReassuring()
    {
        PendingProjectInformationUpdate pending = FilledPending();
        ProjectWorkspace project = Project();
        project.Foundation.InitiationBasis.SiteAddress = "Эмээлт, 3-р хэсэг";
        project.Foundation.PlanningTask.AtdNumber = "АТД-2026-114";
        project.Foundation.InitiationBasis.ClientName = "Хамтрагчийн оруулсан";

        string status = ProjectInformationConflictReport.Summarize(pending, project, Snapshot());

        Assert.Contains("талбар дэлгэц дээр хэвээр", status);
        Assert.Contains("1 талбар серверийнхээр солигдсон", status);
    }

    [Fact]
    public void AFieldTheUserLeftEmptyIsNotPresentedAsRescuedWork()
    {
        // Twenty rows of "(хоосон)" would bury the two that matter.
        var pending = new PendingProjectInformationUpdate
        {
            Name = "Эмээлт",
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Foundation = new ProjectServerFoundationUpdate { IsAvailable = true },
        };
        ProjectWorkspace project = Project();
        project.Identity.Name = "Эмээлт";

        string message = ProjectInformationConflictReport.Describe(pending, project, Snapshot());

        Assert.Contains("ТАНЫ БИЧСЭН, ХЭВЭЭР БАЙГАА (1)", message);
        Assert.DoesNotContain("Газрын лавлагаа", message);
    }

    private static List<PropertyInfo> TextProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .ToList();

    private static PendingProjectInformationUpdate FilledPending() => new()
    {
        ProjectCode = "MAD-2026-ЕТ/03",
        Name = "Эмээлт",
        ClientName = "З.Нэр",
        DesignOrganizationName = "Монгол Архитектур Дизайн",
        QueuedAtUtc = DateTimeOffset.UtcNow,
        Foundation = new ProjectServerFoundationUpdate
        {
            IsAvailable = true,
            SiteAddress = "Эмээлт, 3-р хэсэг",
            AtdNumber = "АТД-2026-114",
        },
    };

    private static ProjectWorkspace Project()
    {
        var project = new ProjectWorkspace();
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = ServerProjectId;
        project.ProjectId = ServerProjectId;
        project.Identity.Code = "MAD-2026-ЕТ/03";
        project.Identity.Name = "Эмээлт";
        project.Foundation.InitiationBasis.ClientName = "З.Нэр";
        project.Foundation.DesignCompany.OrganizationName = "Монгол Архитектур Дизайн";
        return project;
    }

    /// <summary>
    /// The shape the user's own server returned: available, and blank
    /// throughout.
    /// </summary>
    private static ProjectServerSnapshot Snapshot() => new()
    {
        ProjectId = ServerProjectId,
        ProjectCode = "MAD-2026-ЕТ/03",
        Foundation = new ProjectServerFoundation { IsAvailable = true, Version = 1 },
        Information = new ProjectServerInformation
        {
            ProjectId = ServerProjectId,
            ProjectCode = "MAD-2026-ЕТ/03",
        },
    };
}
