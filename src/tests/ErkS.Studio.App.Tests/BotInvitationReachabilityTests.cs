namespace ErkS.Studio.App.Tests;

/// <summary>
/// Four invitation methods sat in StudioAccountService, fully written and
/// correct, and NOTHING called them - not the UI, not a test. So a bot seat
/// could send an invitation that the invited person had no way to see or
/// answer, and every check passed: the code existed, it compiled, its types
/// lined up. "The method is there" is not "the flow works".
///
/// This reads the source rather than the compiled assembly on purpose. A call
/// the compiler kept could still be inside another unreachable method; what is
/// wanted here is cheap and blunt - somebody outside the service layer names it.
/// Brittle to renames, deliberately: a rename that loses the caller is exactly
/// the event worth failing on.
/// </summary>
public sealed class BotInvitationReachabilityTests
{
    private static string AppSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        // Loudly, not by passing. A test that cannot find what it measures has
        // measured nothing, and "no files scanned" must never read as "green".
        throw new DirectoryNotFoundException(
            "Could not find src/src/ErkS.Studio.App above " + AppContext.BaseDirectory);
    }

    private static IReadOnlyList<string> SourcesOutsideAccountService()
    {
        string[] files = [.. Directory.EnumerateFiles(AppSourceDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !Path.GetFileName(path).Equals("StudioAccountService.cs", StringComparison.OrdinalIgnoreCase))];

        Assert.True(files.Length > 20, $"only {files.Length} source files were scanned; the search is wrong");
        return files;
    }

    /// <summary>
    /// Every public bot method on the service layer, found by REFLECTION rather
    /// than from a list somebody remembers to extend. A hand-kept list protects
    /// exactly the methods that were already noticed; the next one added
    /// silently goes dark the same way these did.
    /// </summary>
    public static TheoryData<string> PublicBotServiceMethods()
    {
        var data = new TheoryData<string>();
        foreach (string name in typeof(StudioAccountService)
            .GetMethods(System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Where(name => name.Contains("Bot", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal))
        {
            data.Add(name);
        }

        Assert.True(data.Count > 5, $"only {data.Count} bot methods were found; the reflection is wrong");
        return data;
    }

    [Theory]
    [MemberData(nameof(PublicBotServiceMethods))]
    public void EveryPublicBotMethodIsReachedFromOutsideTheServiceLayer(string method)
    {
        List<string> callers = [.. SourcesOutsideAccountService()
            .Where(path => File.ReadAllText(path).Contains(method + "(", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path) ?? path)];

        Assert.True(
            callers.Count > 0,
            $"{method} is written but nothing outside StudioAccountService calls it - " +
            "the flow it belongs to cannot run.");
    }

    [Fact]
    public void TheInvitationRolesComeFromTheServerCatalogue_NotFromTypedText()
    {
        // Roles were a comma-separated TextBox defaulting to "Member" - a code
        // that is not in the server's catalogue at all - while the real
        // catalogue and its picker were a few lines away in the same product.
        // SRV confirmed nothing validates the field yet, so whatever was typed
        // became the record.
        string dialogs = File.ReadAllText(
            Path.Combine(AppSourceDirectory(), "BotSeatDialogs.cs"));

        Assert.Contains("ListProjectRolesAsync", dialogs, StringComparison.Ordinal);
        Assert.Contains("ProjectMemberRoleDialog", dialogs, StringComparison.Ordinal);
        Assert.DoesNotContain("rolesBox", dialogs, StringComparison.Ordinal);

        // And the invitation no longer sets them at all: a project and roles
        // belong to the SEAT's assignment, and the server refuses an invitation
        // that carries them.
        Assert.DoesNotContain("selectedRoleCodes", dialogs, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInviteeSideIsReachedFromTheNotificationRail()
    {
        // Not just "somewhere": the invitation has to surface where a person
        // actually looks. Sending was wired for weeks while this half was not,
        // and nothing said so.
        string collaboration = File.ReadAllText(
            Path.Combine(AppSourceDirectory(), "ShellView.Collaboration.cs"));

        Assert.Contains("ListMyBotInvitationsAsync", collaboration, StringComparison.Ordinal);
    }
}
