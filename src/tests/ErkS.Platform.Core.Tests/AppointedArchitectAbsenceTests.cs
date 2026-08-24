namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The corner table's architect line when nobody has been appointed.
///
/// The client asked for the appointment to be a deliberate act: «Захирал төсөл
/// үүсгэхдээ ерөнхий архитектороор шууд томилогддоггүй болгочих. Томилохоор
/// бол өөрөө тохируулчихаж чадна.» Filling the line with whoever created the
/// project was convenient and put a name against a responsibility that person
/// had not accepted.
///
/// Leaving it blank is the honest answer. Leaving it blank silently is not:
/// nobody notices an empty cell until the printed set is in somebody's hands.
/// </summary>
public sealed class AppointedArchitectAbsenceTests
{
    [Fact]
    public void NobodyAppointedGivesAnEmptyLineRatherThanAGuess()
    {
        Assert.Equal("", AppointedArchitectResolver.ForDocument([]));
    }

    [Fact]
    public void SomebodyOnTheTeamWithAnotherRoleIsNotPromotedToArchitect()
    {
        // The creator is on every project. Reading "a participant" as "the
        // architect" is exactly the shortcut being removed.
        ProjectParticipant[] team =
        [
            new() { FullName = "С.Захирал", Role = "ProjectManager" },
            new() { FullName = "Б.Инженер", Role = "Engineer" },
        ];

        Assert.Equal("", AppointedArchitectResolver.ForDocument(team));
    }

    [Fact]
    public void TheAppointedArchitectIsUsedWhenThereIsOne()
    {
        ProjectParticipant[] team =
        [
            new() { FullName = "С.Захирал", Role = "ProjectManager" },
            new() { FamilyName = "Гантулга", GivenName = "Болд", Role = "MajorArchitect" },
        ];

        Assert.NotEqual("", AppointedArchitectResolver.ForDocument(team));
    }

    [Fact]
    public void AnAppointmentWithNoNameIsNotAnAppointment()
    {
        // A role on a record with nobody in it would print an empty line while
        // claiming somebody holds the post.
        ProjectParticipant[] team = [new() { Role = "MajorArchitect" }];

        Assert.Equal("", AppointedArchitectResolver.ForDocument(team));
    }
}
