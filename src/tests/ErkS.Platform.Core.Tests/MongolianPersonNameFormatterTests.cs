using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class MongolianPersonNameFormatterTests
{
    [Theory]
    [InlineData("Энхбаатар Мөнххолбоо", "Э.Мөнххолбоо")]
    [InlineData("  Энхбаатар   Мөнххолбоо  ", "Э.Мөнххолбоо")]
    [InlineData("Э.Мөнххолбоо", "Э.Мөнххолбоо")]
    [InlineData("Мөнххолбоо", "Мөнххолбоо")]
    [InlineData("munkhkholboo@gmail.com", "munkhkholboo@gmail.com")]
    public void ForDocumentFormatsRegisteredProfileName(string source, string expected)
    {
        Assert.Equal(expected, MongolianPersonNameFormatter.ForDocument(source));
    }

    [Fact]
    public void StructuredProfileFieldsAreAuthoritativeForDisplayAndDocumentNames()
    {
        Assert.Equal(
            "Энхбаатар Мөнххолбоо",
            MongolianPersonNameFormatter.ForDisplay(
                "Энхбаатар",
                "Мөнххолбоо",
                "Мөнххолбоо Энхбаатар"));
        Assert.Equal(
            "Э.Мөнххолбоо",
            MongolianPersonNameFormatter.ForDocument(
                "Энхбаатар",
                "Мөнххолбоо",
                "Мөнххолбоо Энхбаатар"));
    }

    [Fact]
    public void StructuredFormatterDoesNotGuessLegacyDisplayNameOrder()
    {
        Assert.Equal(
            "Мөнххолбоо Энхбаатар",
            MongolianPersonNameFormatter.ForDocument(
                familyName: "",
                givenName: "",
                fallbackDisplayName: "Мөнххолбоо Энхбаатар"));
    }

    [Theory]
    [InlineData("MajorArchitect", true)]
    [InlineData("Major architect", true)]
    [InlineData("Architect", false)]
    [InlineData("ChiefArchitect", false)]
    public void AppointedArchitectRoleIsExplicit(string role, bool expected)
    {
        Assert.Equal(expected, ProjectRoleSemantics.IsAppointedArchitect(role));
    }

    [Fact]
    public void TitleBlockArchitectUsesOnlyAppointedMajorArchitect()
    {
        ProjectParticipant[] participants =
        [
            new() { Role = "Architect", FullName = "Дорж Бат" },
            new() { Role = "MajorArchitect", FullName = "Энхбаатар Мөнххолбоо" },
        ];

        Assert.Equal("Э.Мөнххолбоо", AppointedArchitectResolver.ForDocument(participants));
        Assert.Equal(
            "",
            AppointedArchitectResolver.ForDocument(
                [new ProjectParticipant { Role = "Architect", FullName = "Дорж Бат" }]));
    }

    [Fact]
    public void TitleBlockArchitectUsesStructuredProfileFieldsOverLegacyFullName()
    {
        ProjectParticipant[] participants =
        [
            new()
            {
                Role = "MajorArchitect",
                FamilyName = "Энхбаатар",
                GivenName = "Мөнххолбоо",
                FullName = "Мөнххолбоо Энхбаатар",
            },
        ];

        Assert.Equal("Э.Мөнххолбоо", AppointedArchitectResolver.ForDocument(participants));
    }
}
