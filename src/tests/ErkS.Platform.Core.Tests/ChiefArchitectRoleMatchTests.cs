using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Cyrillic - or any display text - used as a matching key is a defect PFR
/// measured for real: «Erk-S Стандарт» could never meet «Erk-S Standard», so a
/// title block picked the wrong organisation. The same shape was here: the
/// chief-architect check knew three DISPLAY spellings and not the role CODE.
///
/// Every role stored on this machine is a code - MajorArchitect, Architect,
/// ProjectAdmin, ProjectViewer - and "MajorArchitect" matched none of the three,
/// because each spelling carries a space.
/// </summary>
public sealed class ChiefArchitectRoleMatchTests
{
    [Theory]
    [InlineData("MajorArchitect")]
    [InlineData("majorarchitect")]
    [InlineData("Major Architect")]
    [InlineData("Major-Architect")]
    public void TheRoleCODEIsRecognised_HoweverItIsPunctuated(string role)
    {
        // The stored value is the bare code. Punctuation and case are the kinds
        // of difference a normaliser is for; a Contains over display text is not
        // one.
        Assert.True(ProjectRoleSemantics.IsAppointedArchitect(role), role);
    }

    [Fact]
    public void AnotherRoleIsNotMistakenForIt()
    {
        Assert.False(ProjectRoleSemantics.IsAppointedArchitect("Architect"));
        Assert.False(ProjectRoleSemantics.IsAppointedArchitect("ProjectAdmin"));
        Assert.False(ProjectRoleSemantics.IsAppointedArchitect(""));
        Assert.False(ProjectRoleSemantics.IsAppointedArchitect(null));
    }

    [Fact]
    public void TheApprovalRosterACTUALLYUsesTheNormaliser()
    {
        // The rule above is only worth having where it is asked. The matcher in
        // ProjectApprovalWorkflow knew three display spellings and none of them
        // was the stored code, so it never recognised a real chief architect -
        // and a unit test of the normaliser alone would have stayed green
        // through all of it.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (directory is not null && source is null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Core", "ProjectApprovalWorkflow.cs");
            if (File.Exists(candidate))
                source = candidate;
            directory = directory.Parent;
        }

        Assert.NotNull(source);
        string workflow = File.ReadAllText(source!);
        int matcher = workflow.IndexOf("private static bool IsChiefArchitect", StringComparison.Ordinal);
        Assert.True(matcher >= 0, "IsChiefArchitect was renamed; check this test with it");
        string body = workflow[matcher..(workflow.IndexOf(';', matcher) + 1)];
        Assert.Contains("ProjectRoleSemantics.IsAppointedArchitect", body, StringComparison.Ordinal);
    }
}
