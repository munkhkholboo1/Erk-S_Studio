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
    public void NEITHERSeatWindowASKSWhichCompany()
    {
        // The picker is REMOVED, not defaulted or hidden behind a count. A
        // window that still holds one has a rule about when to show it, and
        // that rule is the thing that put a company in front of a seat.
        //
        // 🔴 THIS TEST FIRST BANNED THE WORD «organization» ANYWHERE IN THE
        // FILE, and then went red the moment the company arrived back as
        // READ-ONLY HISTORY on the selected seat - the correct answer. A word
        // ban cannot tell asking from showing, so it fails in both directions:
        // it blocks the right change and would pass a wrong one spelled
        // differently. The rule is about ASKING, so that is what is asserted.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.DoesNotContain("organizationBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OrganizationLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Байгууллага\", ", source, StringComparison.Ordinal);

        // And nothing narrows the list by one: the company may be READ off a
        // seat, never SENT to choose which seats come back.
        Assert.DoesNotContain("ListBotSeatsAsync(organization", source, StringComparison.Ordinal);
        Assert.Contains("ListBotSeatsAsync();", source, StringComparison.Ordinal);
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
    public void THEListSaysWHOSESeatsItIsCountingAndSaysItONCE()
    {
        // The occupancy figure is the ACCOUNT's total now, across every company
        // - the same «7 / 10» that used to mean one company's. An owner reading
        // the new number as the old one concludes they have lost seats, so the
        // line names whose it is.
        //
        // 🔴 AND IT SAYS ONE NUMBER. It printed the row count AND the occupied
        // count, and on an unfiltered list those are the same query: not
        // deleted, owned by the caller. Two printings of one number make a
        // reader hunt for the difference - I invented one («seats with a
        // machine on them») and was wrong. Whether a machine is on a seat is
        // DeviceSeated, per row.
        string source = ReadAppSource("BotSeatDialogs.cs");
        string body = MethodBody(source, "    private async Task RefreshAsync()");

        Assert.Contains("\"Миний суудлууд: \"", body, StringComparison.Ordinal);
        Assert.Contains("response.OccupiedSeats", body, StringComparison.Ordinal);

        // The row count is NOT printed beside it. It is the same number, and
        // the one shown is the server's - the count the licence is enforced
        // against, which is what belongs next to the limit.
        Assert.DoesNotContain("response.Items.Count", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THESingleCountDEPENDSOnSendingNoFilter()
    {
        // The two totals are one query only while the list is unfiltered. This
        // ties the display decision to the call that justifies it, so a filter
        // added later cannot quietly make the line wrong: the test that pins
        // the unfiltered call is next door, and this names the dependency.
        string source = ReadAppSource("StudioAccountService.cs");
        string body = MethodBody(
            source,
            "public async Task<StudioCloudBotSeatListResponse> ListBotSeatsAsync(");

        Assert.DoesNotContain("organizationId", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ASeatWithNoCompanySaysNOTHINGRatherThanShowingAGap()
    {
        // 🔴 THE REVERSAL: this was going to be a COLUMN, and a column would
        // print an empty cell for every seat opened since the owner's decision.
        // An empty cell reads as «this is missing» when the truth is «there is
        // no such thing» - a seat belongs to the account, and the company lives
        // on the PROJECT the bot is assigned to. A gap where a link used to be
        // invites somebody to fill it.
        Assert.Equal("", StudioBotSeatOrigin.Describe("", ""));
        Assert.Equal("", StudioBotSeatOrigin.Describe("   ", "Алтанхөхийн Очир"));
    }

    [Fact]
    public void ASeatOpenedUnderACompanySaysWHICHOne()
    {
        string line = StudioBotSeatOrigin.Describe("org_1", "Алтанхөхийн Очир");

        Assert.Contains("Алтанхөхийн Очир", line, StringComparison.Ordinal);
        // Named as HISTORY. Without that word the line reads as a live link,
        // which is the very claim this change removed.
        Assert.Contains("Нээсэн үеийн", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ANAMEtheSERVERCouldNotResolveFallsBackToTheID()
    {
        // Two different facts, and the server draws the line: an empty NAME
        // means the server could not find the company - a seat opened under one
        // the owner has since left, say. An empty ID means there was never one.
        // Printing nothing for the first would hide a company that exists.
        string line = StudioBotSeatOrigin.Describe("org_1", "");

        Assert.Contains("org_1", line, StringComparison.Ordinal);
        Assert.NotEqual("", line);
    }

    [Fact]
    public void THEOriginIsShownOnTheSELECTEDSeatAndCollapsesWhenEmpty()
    {
        // Collapsed rather than blanked: a line that is always present and
        // usually empty is a gap on the screen, which is the thing this is
        // meant to avoid.
        string source = ReadAppSource("BotSeatDialogs.cs");

        Assert.Contains("StudioBotSeatOrigin.Describe(", source, StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed : Visibility.Visible", source, StringComparison.Ordinal);

        // Refreshed on selection AND after the list reloads - a stale sentence
        // under a seat that is gone is worse than none.
        // Defined once, called on selection and again after the list
        // reloads: three occurrences in the file.
        Assert.Equal(3, Occurrences(source, "RefreshSeatOrigin()"));

        // 🔴 A SABOTAGE SURVIVED HERE. Replacing the row's two company fields
        // with empty strings left every test green: the RULE was covered and
        // the WIRING to it was not, so the note would simply never appear and
        // nothing would say so. The row has to be built from the seat's own
        // fields, and that is what is asserted.
        Assert.Contains("seat.OrganizationId,", source, StringComparison.Ordinal);
        Assert.Contains("seat.OrganizationName))", source, StringComparison.Ordinal);
        Assert.Contains(
            "StudioBotSeatOrigin.Describe(Selected.OrganizationId, Selected.OrganizationName)",
            source,
            StringComparison.Ordinal);
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

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

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
