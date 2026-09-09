using System.Text;
using System.Text.RegularExpressions;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A seat belongs to the person whose licence pays for it, not to a company.
///
/// 🔴 THE OWNER SAID IT IN ONE LINE AND THE PRODUCT CONTRADICTED IT EVERY DAY:
/// «Эзэмшигч ямар ч байгууллагад харьяалуулахгүйгээр ботыг үүсгэнэ». The first
/// field of the seating window was a company picker, so a seat could not exist
/// without one; every seat call carried /organizations/{id}/ in its path; and an
/// owner holding three companies saw their own seats split into three lists with
/// a filter above them that could not be turned off.
///
/// This whole change moved thirteen calls and two windows and NOT ONE TEST WENT
/// RED, because none of it was covered. The gap is the finding, so the rules go
/// down here - and the route shape first, because a wrong path is what breaks
/// against a server that has already shipped.
/// </summary>
public sealed class SeatBelongsToTheOwnerTests
{
    [Fact]
    public void NOSeatRouteNamesAnORGANISATION()
    {
        // Derived from the source rather than listed: the point is to catch the
        // call nobody has thought about yet, and a hand-written list of twelve
        // stays true on the day a thirteenth is added.
        string source = ReadAppSource("StudioAccountService.cs");
        var offenders = new List<string>();
        foreach (Match match in Regex.Matches(source, "\"(/api/cloud-era/v1/[^\"]*)\""))
        {
            string route = match.Groups[1].Value;
            if (route.Contains("bot-seat", StringComparison.Ordinal) &&
                route.Contains("organizations", StringComparison.Ordinal))
            {
                offenders.Add(route);
            }
        }

        Assert.Empty(offenders);

        // The positive control: this test must actually be reading seat routes.
        // With none found, "no offenders" would be true of an empty file.
        Assert.Contains("\"/api/cloud-era/v1/bot-seats\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "\"/api/cloud-era/v1/bot-seats/\" + Uri.EscapeDataString(botId)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THESeatPathIsBuiltFromTheSEATAlone()
    {
        string source = ReadAppSource("StudioAccountService.cs");

        Assert.Contains("private static string SeatPath(string botId)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SeatPath(organizationId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NOSeatCallStillTakesAnOrganisation()
    {
        // Every method that reaches a seat route, read out of the file with the
        // first line of its signature. None may name an organisation.
        string source = ReadAppSource("StudioAccountService.cs");
        var offenders = new List<string>();
        foreach (string method in new[]
        {
            "ListBotAssignmentsAsync", "AssignBotProjectAsync", "ChangeBotAssignmentRolesAsync",
            "RemoveBotAssignmentAsync", "ListBotSeatsAsync", "CreateBotSeatAsync", "SetBotPinAsync",
            "RevealBotPinAsync", "UnlockBotPinAsync", "LeaveBotStateAsync", "InviteBotMemberAsync",
            "DeleteBotSeatAsync", "EnterBotStateAsync",
        })
        {
            int at = source.IndexOf(" " + method + "(", StringComparison.Ordinal);
            Assert.True(at > 0, method + " was not found; this test is reading nothing");
            int close = source.IndexOf(')', at);
            if (source[at..close].Contains("organizationId", StringComparison.Ordinal))
                offenders.Add(method);
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void THEListIsUNFILTEREDRatherThanFilteredByDefault()
    {
        // 🔴 THE SERVER STILL ACCEPTS AN organizationId AS A FILTER, and sending
        // one by default would have kept the old behaviour under a new route:
        // the work would look done and the owner would still see a third of
        // their seats. Unfiltered is the answer to «which seats do I have».
        string source = ReadAppSource("StudioAccountService.cs");
        string body = MethodBody(
            source,
            "public async Task<StudioCloudBotSeatListResponse> ListBotSeatsAsync(");

        Assert.Contains("\"/api/cloud-era/v1/bot-seats\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("organizationId", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SEATINGAMachineNoLongerNEEDSACompany()
    {
        // An owner with no company was refused outright. The fetch, the failure
        // it could produce and the refusal it fed are gone rather than made
        // optional - a company was never what the seat was spent against.
        string source = ReadAppSource("ShellView.BotSeat.cs");

        Assert.DoesNotContain(
            "Ботын суудал үүсгэхэд байгууллага шаардлагатай.",
            source,
            StringComparison.Ordinal);
        Assert.Contains("new BotSeatCreateDialog(account)", source, StringComparison.Ordinal);
        Assert.Contains("new BotSeatManagementDialog(account)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NEITHERSeatWindowAsksWhichCompany()
    {
        // The picker is REMOVED, not defaulted or hidden behind a count. A
        // window that still holds one has a rule about when to show it, and
        // that rule is the thing that put a company in front of a seat.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.DoesNotContain("rganization", source, StringComparison.Ordinal);
    }

    [Fact]
    public void THEMemberColumnShowsAPersonsNAME()
    {
        // 🔴 THE COLUMN IS HEADED «Гишүүн» AND PRINTED AN ADDRESS. The name now
        // arrives on the seat itself, so the table fills from the list it
        // already has - N lookups to fill a column is how a list becomes slow
        // enough that nobody opens it.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.Contains("seat.MemberDisplayName", source, StringComparison.Ordinal);
        Assert.Contains("StudioAccountDisplay.NameOrFallback(", source, StringComparison.Ordinal);

        // The name has to exist on the model, or the line above reads "" for
        // everyone and the column silently goes back to addresses.
        Assert.Contains(
            "public string MemberDisplayName { get; set; }",
            ReadAppSource("StudioCloudContracts.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NOMemberAndAMemberWithNoNameStayTELLABLEApart()
    {
        // 🔴 SRV ASKED FOR THIS EXPLICITLY: never fill an empty name with the
        // address, because then «nobody is on this seat» and «somebody is on it
        // whose account has no name» print the same thing. They are different
        // facts and the owner acts differently on each.
        //
        // Falling back to the address is a CLIENT decision and is made once,
        // here - the server never writes an address into the name field.
        string nobody = StudioAccountDisplay.NameOrFallback("", "", "—");
        string namelessMember = StudioAccountDisplay.NameOrFallback(
            "", "gerlee@erk-s.mn", "gerlee@erk-s.mn");
        string namedMember = StudioAccountDisplay.NameOrFallback(
            "Гэрлээ Б.", "gerlee@erk-s.mn", "gerlee@erk-s.mn");

        Assert.Equal("—", nobody);
        Assert.Equal("gerlee@erk-s.mn", namelessMember);
        Assert.Equal("Гэрлээ Б.", namedMember);
        Assert.NotEqual(nobody, namelessMember);
    }

    [Fact]
    public void THEListSaysWHOSESeatsItIsCounting()
    {
        // The occupancy figure beside it is the ACCOUNT's total now, across
        // every company - the same «7 / 10» that used to mean one company's.
        // An owner reading the new number as the old one concludes they have
        // lost seats.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.Contains("\"Миний суудлууд: \"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void THELocalRecordOfAPendingReleaseIsSTILLReadable()
    {
        // ⚠️ THE MACHINE THIS SHIPS TO HAS A PENDING RELEASE ON DISK, written
        // under the old shape with an organisation in it. The call no longer
        // sends one, but the local record is matched on BOTH fields - so the
        // field has to stay, or an entry written yesterday can never be
        // forgotten and the queue never empties.
        string source = ReadAppSource("StudioPendingBotSeatReleases.cs");

        Assert.Contains("public required string OrganizationId { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("item.OrganizationId.Equals(organizationId", source, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
