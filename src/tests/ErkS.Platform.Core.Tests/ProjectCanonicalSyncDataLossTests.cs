namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A sync must never cost someone their work.
///
/// It did. Taking the server's snapshot used to overwrite every local field
/// unconditionally, blanks included, so a server that simply does not carry a
/// field erased whatever had been typed into it. Two harmless-looking rules met
/// - the server accepts three foundation fields it never stores, and its
/// concurrency token covers the whole project so a user's own album upload
/// invalidates their own queued edit - and between them a project's information
/// was wiped every time sync was pressed.
///
/// The rule now compares the incoming snapshot with the one the mirror was
/// built from. A field the server changed is taken; a field it merely restated
/// leaves the local value alone, because the only thing that can have altered
/// that is the person in front of it. These pin both halves: work is kept, and
/// a colleague's accepted edit still arrives.
/// </summary>
public sealed class ProjectCanonicalSyncDataLossTests
{
    private const string ServerProjectId = "c59b2a4ce1cd4657b025a826223c6a5a";

    [Fact]
    public void AnEmptyServerValueNeverErasesSomethingLocal()
    {
        // The server carries no site address at all. A blank arriving from it
        // is a field it does not hold, not an erasure, and reading it as one is
        // how the wipe happened.
        ProjectWorkspace project = LinkedProject();
        project.Foundation.InitiationBasis.SiteAddress = "УБ, БЗД, 5-р хороо";
        project.Foundation.PlanningTask.AtdNumber = "АТД-2026-114";

        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        Assert.Equal("УБ, БЗД, 5-р хороо", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("АТД-2026-114", project.Foundation.PlanningTask.AtdNumber);
    }

    [Fact]
    public void TheThreeFieldsTheServerAcceptsButNeverStoresAreKept()
    {
        // Measured on the server: /information takes siteAddress, basisSummary
        // and atdAuthorityName, cleans them, and never saves them - answering
        // 200 with the old values. Studio rightly concludes the edit was not
        // accepted; it must not then destroy the edit.
        ProjectWorkspace project = LinkedProject();
        project.Foundation.InitiationBasis.SiteAddress = "Эмээлт";
        project.Foundation.InitiationBasis.Summary = "Үндэслэл";
        project.Foundation.PlanningTask.IssuingAuthorityName = "Ерөнхий архитектор";

        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        Assert.Equal("Эмээлт", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("Үндэслэл", project.Foundation.InitiationBasis.Summary);
        Assert.Equal("Ерөнхий архитектор", project.Foundation.PlanningTask.IssuingAuthorityName);
    }

    [Fact]
    public void TheQueuedEditIsStillWaitingAfterwards()
    {
        // The refusal leaves the edit queued so the next sync can send it. It
        // must survive the snapshot being applied, or the retry has nothing to
        // retry with.
        ProjectWorkspace project = LinkedProject();
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            ProjectCode = "MAD-2026-ЕТ/03",
            Name = "Эмээлт",
            ClientName = "З.Нэр",
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Foundation = new ProjectServerFoundationUpdate
            {
                IsAvailable = true,
                SiteAddress = "Эмээлт, 3-р хэсэг",
                AtdNumber = "АТД-2026-114",
            },
        };

        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        Assert.NotNull(project.Cloud.PendingProjectInformation);
        Assert.Equal("Эмээлт, 3-р хэсэг", project.Cloud.PendingProjectInformation!.Foundation.SiteAddress);
    }

    [Fact]
    public void AServerValueThatGenuinelyChangedStillWins()
    {
        // A colleague's accepted edit has to reach this screen. Without that,
        // two people each see their own reality and never learn they disagree -
        // which is worse than either of them losing a field.
        ProjectWorkspace project = LinkedProject();
        ProjectCanonicalSyncService.Apply(project, NamedServerSnapshot("Анхны захиалагч"));
        Assert.Equal("Анхны захиалагч", project.Foundation.InitiationBasis.ClientName);

        ProjectCanonicalSyncService.Apply(project, NamedServerSnapshot("Хамтрагчийн шинэчилсэн"));

        Assert.Equal("Хамтрагчийн шинэчилсэн", project.Foundation.InitiationBasis.ClientName);
    }

    [Fact]
    public void AValueTheServerDeliberatelyClearedIsCleared()
    {
        // Once a snapshot has been recorded there is a base to compare against,
        // and a genuine clearing on the server is visible as a change. The rule
        // protects work; it does not make the mirror stubborn.
        ProjectWorkspace project = LinkedProject();
        ProjectCanonicalSyncService.Apply(project, NamedServerSnapshot("Анхны захиалагч"));

        ProjectCanonicalSyncService.Apply(project, NamedServerSnapshot(""));

        Assert.Equal("", project.Foundation.InitiationBasis.ClientName);
    }

    [Fact]
    public void AnEditMadeAfterTheLastSyncOutlivesTheNextOne()
    {
        // The user's actual sequence: sync, type something, sync again. The
        // server never learned about it, so it says nothing about it, so it
        // stays.
        ProjectWorkspace project = LinkedProject();
        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());
        project.Foundation.InitiationBasis.SiteAddress = "Эмээлт, 3-р хэсэг";
        project.Foundation.PlanningTask.AtdNumber = "АТД-2026-114";

        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        Assert.Equal("Эмээлт, 3-р хэсэг", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("АТД-2026-114", project.Foundation.PlanningTask.AtdNumber);
    }

    [Fact]
    public void AProjectThatWasNeverFilledInStaysEmpty()
    {
        // The rule must not invent content.
        ProjectWorkspace project = LinkedProject();

        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        Assert.Equal("", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("", project.Foundation.PlanningTask.AtdNumber);
    }

    [Fact]
    public void ARepeatedSyncDoesNotDriftTheProject()
    {
        // A conflict gets retried, and people press sync more than once. The
        // second pass has to land where the first did.
        ProjectWorkspace project = LinkedProject();
        project.Foundation.InitiationBasis.SiteAddress = "Эмээлт";
        project.Foundation.PlanningTask.AtdNumber = "АТД-114";

        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());
        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());
        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        Assert.Equal("Эмээлт", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("АТД-114", project.Foundation.PlanningTask.AtdNumber);
    }

    [Fact]
    public void A412RefreshDoesNotWipeTheEditThatCausedIt()
    {
        // The conflict path downloads the server's current state and applies it
        // before telling the user anything. That refresh runs through here, so
        // the rejection used to destroy the very edit it was rejecting - the
        // user pressed sync, was told there was a conflict, and found the form
        // blank.
        ProjectWorkspace project = LinkedProject();
        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        project.Foundation.InitiationBasis.SiteAddress = "Эмээлт, 3-р хэсэг";
        project.Foundation.InitiationBasis.ClientName = "З.Нэр";
        project.Foundation.PlanningTask.AtdNumber = "АТД-2026-114";
        project.Cloud.PendingProjectInformation = new PendingProjectInformationUpdate
        {
            BaseConcurrencyToken = "93F34CA5B8A06BD9EEA6248C",
            ProjectCode = "MAD-2026-ЕТ/03",
            Name = "Эмээлт",
            ClientName = "З.Нэр",
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Foundation = new ProjectServerFoundationUpdate
            {
                IsAvailable = true,
                SiteAddress = "Эмээлт, 3-р хэсэг",
                AtdNumber = "АТД-2026-114",
            },
        };

        // What the refresh after a 412 hands back: the server, unchanged,
        // because it rejected the write.
        ProjectCanonicalSyncService.Apply(project, EmptyServerSnapshot());

        Assert.Equal("Эмээлт, 3-р хэсэг", project.Foundation.InitiationBasis.SiteAddress);
        Assert.Equal("З.Нэр", project.Foundation.InitiationBasis.ClientName);
        Assert.Equal("АТД-2026-114", project.Foundation.PlanningTask.AtdNumber);
        Assert.NotNull(project.Cloud.PendingProjectInformation);
    }

    private static ProjectWorkspace LinkedProject()
    {
        var project = new ProjectWorkspace();
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = ServerProjectId;
        project.ProjectId = ServerProjectId;
        return project;
    }

    /// <summary>
    /// The shape the user's own server actually returned: available, and blank
    /// throughout.
    /// </summary>
    private static ProjectServerSnapshot EmptyServerSnapshot() => new()
    {
        ProjectId = ServerProjectId,
        ProjectCode = "MAD-2026-ЕТ/03",
        Name = "",
        ClientName = "",
        Foundation = new ProjectServerFoundation
        {
            IsAvailable = true,
            Version = 1,
            InitiationBasis = new ProjectServerInitiationBasis(),
            PlanningTask = new ProjectServerPlanningTask(),
        },
        Information = new ProjectServerInformation
        {
            ProjectId = ServerProjectId,
            ProjectCode = "MAD-2026-ЕТ/03",
        },
    };

    private static ProjectServerSnapshot NamedServerSnapshot(string clientName)
    {
        ProjectServerSnapshot snapshot = EmptyServerSnapshot();
        snapshot.ClientName = clientName;
        return snapshot;
    }
}
