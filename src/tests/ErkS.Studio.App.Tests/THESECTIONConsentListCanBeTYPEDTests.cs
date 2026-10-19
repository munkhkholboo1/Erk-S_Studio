using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The working sheets' «ЗӨВШӨӨРӨЛЦСӨН» table is filled from
/// workingDrawingSectionConsent - and until the editor had a section for it,
/// that was a list the album read and nobody could write. A table drawn from
/// a list with no way in prints empty forever.
///
/// The view is WPF, so what it builds is read from its source, as its
/// neighbour ConcurredByEditorReachabilityTests does; the comparison that
/// decides whether a save happens is tested directly.
/// </summary>
public sealed class THESECTIONConsentListCanBeTYPEDTests
{
    [Fact]
    public void TheSectionHasRowsAndAWayToAddThem()
    {
        string view = ReadApprovalsView();

        Assert.Contains("sectionConsentRowsPanel", view, StringComparison.Ordinal);
        Assert.Contains("AddApprovalRow(ApprovalRosterKind.SectionConsent)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsTypedIsREADBackSAVEDAndLOADED()
    {
        string view = ReadApprovalsView().Replace("\r\n", "\n");
        string shell = ReadView("ShellView.cs").Replace("\r\n", "\n");

        Assert.Contains("SectionConsent = CaptureSectionConsentDraft()", shell, StringComparison.Ordinal);
        Assert.Contains("ApplySectionConsentDraft(draft.SectionConsent);", shell, StringComparison.Ordinal);
        // Both checks - "is there anything to save" and "did the save change
        // anything" - must see the list, or editing only an engineer saves nothing.
        string[] differs = shell.Split("WorkingDrawingSectionConsentList.Differs(\n");
        Assert.Equal(3, differs.Length);
        Assert.All(differs.Take(2), before => Assert.EndsWith(") ||\n            ", before, StringComparison.Ordinal));
        Assert.Contains(".WorkingDrawingSectionConsent = ", view, StringComparison.Ordinal);
        Assert.Contains("ApprovalRosterKind.SectionConsent,\n", view, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RowsFor")]
    [InlineData("PanelFor")]
    [InlineData("MinimumFor")]
    [InlineData("MaximumFor")]
    [InlineData("RosterLabel")]
    [InlineData("RefusesBeyondMaximum")]
    public void EveryPerRosterRuleNamesTheNewRosterEXPLICITLY(string ruleName)
    {
        string view = ReadApprovalsView().Replace("\r\n", "\n");
        int start = view.IndexOf(ruleName + "(ApprovalRosterKind kind)", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{ruleName} was renamed; check this test with it");

        string body = view[start..Math.Min(view.Length, start + 1200)];
        Assert.Contains("ApprovalRosterKind.SectionConsent =>", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEDITToAMarkOrANameIsAChange()
    {
        List<WorkingDrawingSectionConsentEntry> saved =
            [new() { Id = "a", SectionMark = "ББ", PersonName = "А.Буянжаргал" }];

        Assert.False(WorkingDrawingSectionConsentList.Differs(saved, [Copy(saved[0])]));
        Assert.True(WorkingDrawingSectionConsentList.Differs(
            saved, [new() { Id = "a", SectionMark = "ХАС", PersonName = "А.Буянжаргал" }]));
        Assert.True(WorkingDrawingSectionConsentList.Differs(
            saved, [new() { Id = "a", SectionMark = "ББ", PersonName = "Э.Ариунцэцэг" }]));
        Assert.True(WorkingDrawingSectionConsentList.Differs(saved, []));
    }

    [Fact]
    public void AnEMPTYTypedRowIsNotKept()
    {
        // The editor always offers a blank line to type into; saving it must
        // not print an empty row with a signature line under no name.
        IReadOnlyList<WorkingDrawingSectionConsentEntry> kept = WorkingDrawingSectionConsentList.Clean(
        [
            new() { SectionMark = " ", PersonName = "" },
            new() { SectionMark = "ДГ", PersonName = "Б.Бат-Эрдэнэ" },
        ]);

        Assert.Equal("ДГ", Assert.Single(kept).SectionMark);
    }

    private static WorkingDrawingSectionConsentEntry Copy(WorkingDrawingSectionConsentEntry entry) =>
        new() { Id = entry.Id, SectionMark = entry.SectionMark, PersonName = entry.PersonName };

    private static string ReadApprovalsView() => ReadView("ShellView.Approvals.cs");

    private static string ReadView(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail($"{fileName} was not found; this test reads it from source");
        return "";
    }
}
