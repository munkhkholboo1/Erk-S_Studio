namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Owner, 2026-10-26: «Erk-S Sync олон төсөл синк хийх шаардлагатай болох тул синк дээрх мэдээллийг
/// цомхон ойлгомжтой хураангуй болгоорой … эхлээд хураангуйгаар хардаг байна». One line for all
/// projects (the tray), one per project; no member names, no member file counts.
/// </summary>
public sealed class THESummaryCOMESFirstTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThreeProjectsInStepSayJustThat()
    {
        SyncState state = State(Folder("A"), Folder("B"), Folder("C"));

        Assert.Equal("3 төсөл · бүгд бүрэн", SyncSummary.Line(SyncSummary.Projects(state, Now)));
    }

    [Fact]
    public void OneSyncingAndOneNeedingAPersonAreCounted()
    {
        SyncFolderState coming = Folder("B") with
        {
            Peers = [Online() with { NeededFiles = 3, NeededBytes = 55, HereBytes = 45, Transferring = true, TransferBytesPerSecond = 12 << 20, TransferSecondsLeft = 180 }],
        };
        SyncFolderState conflict = Folder("C") with { ConflictCount = 1 };
        IReadOnlyList<SyncProjectSummary> projects = SyncSummary.Projects(State(Folder("A"), coming, conflict), Now);

        Assert.Equal("3 төсөл · 1 синк хийж байна 45% · 12 МБ/с · ~3 мин · ⚠ 1 анхаарах", SyncSummary.Line(projects));
        Assert.Equal(["✓ Бүрэн", "↻ 45% · 12 МБ/с · ~3 мин", "⚠ 1 анхаарах"], projects.Select(project => project.StateWords));
    }

    [Fact]
    public void WhatOthersWaitForAndOfflineMembersDoNotLowerIt()
    {
        SyncPeerState waitsForMine = Online() with { DeviceKeyId = "B", AccountEmail = "member@example.mn", SyncedPercent = 97.5, OnlyHereCount = 36 };
        SyncPeerState offline = new() { DeviceKeyId = "C", ExchangedAtUtc = Now.AddDays(-1), ServerLastSeenAtUtc = Now.AddDays(-1), NeededFiles = 77, NeededBytes = 3_000_000, HereBytes = 100 };
        string line = SyncSummary.Line(SyncSummary.Projects(State(Folder("A") with { Peers = [waitsForMine, offline] }), Now));

        Assert.Equal("1 төсөл · бүгд бүрэн", line);
        Assert.DoesNotContain("member", line);
    }

    [Fact]
    public void AKeptChangesetCopyOrARedactedContentIsNoDecisionForAPerson()
    {
        var ledger = new SyncVersionLedger();
        ledger.Keep(Path.Combine(".erks-collab", "file-1", "log", "w", "00000001.cgacs"), "dd9a");
        ledger.Keep("plan.dwg", "theirs");
        ledger.Keep("note.pdf", "redacted-old");

        ledger.ForgetKept(SyncCollab.IsChangesetPath, new HashSet<string> { "redacted-old" });

        Assert.Equal(["plan.dwg"], ledger.Kept.Keys);
    }

    private static SyncPeerState Online() => new() { DeviceKeyId = "A", ExchangedAtUtc = Now, ServerLastSeenAtUtc = Now };

    private static SyncFolderState Folder(string code) => new() { FolderId = code, ProjectCode = code, PresentHere = true, Peers = [Online()] };

    private static SyncState State(params SyncFolderState[] folders) => new() { ServerStatus = "ok", Folders = folders };
}
