namespace ErkS.Studio;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ErkS.Platform.Core;

/// <summary>
/// One field, seen from the three places that disagree about it.
/// </summary>
/// <param name="Label">What the field is called on the information page.</param>
/// <param name="Typed">What the user entered, still queued for the server.</param>
/// <param name="OnScreen">What the page shows now, after the refresh.</param>
/// <param name="Server">What the server holds.</param>
internal sealed record ProjectInformationConflictField(
    string Label,
    string Typed,
    string OnScreen,
    string Server)
{
    /// <summary>The user typed something and it is still what they see.</summary>
    public bool Kept => Typed.Length > 0 && string.Equals(Typed, OnScreen, StringComparison.Ordinal);

    /// <summary>
    /// The user typed something and the page now shows something else, because
    /// a colleague's accepted edit arrived for this field. The typed value is
    /// not gone - it is queued - but it is no longer on screen, and saying so
    /// is the whole point of this dialog.
    /// </summary>
    public bool Replaced => Typed.Length > 0 && !string.Equals(Typed, OnScreen, StringComparison.Ordinal);
}

/// <summary>
/// What the conflict dialog says.
///
/// It used to show three fields - name, location, purpose - out of the twenty
/// the user can fill in, and assert that the local edit had been preserved.
/// For this project all three happened to be empty, so the dialog claimed a
/// rescue and then displayed nothing at all; the only reasonable reading was
/// that the work was gone. Worse, before the sync fix the claim was false: the
/// server snapshot really did overwrite the page.
///
/// So the report reads every field the pending update carries, and separates
/// what is still on screen from what a colleague's edit replaced there. A
/// warning nobody can act on is a silent failure wearing a warning's clothes.
/// </summary>
internal static class ProjectInformationConflictReport
{
    public static IReadOnlyList<ProjectInformationConflictField> Compare(
        PendingProjectInformationUpdate pending,
        ProjectWorkspace project,
        ProjectServerSnapshot server)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(server);

        ProjectServerFoundationUpdate queued = pending.Foundation ?? new();
        ProjectInitiationBasis basis = project.Foundation.InitiationBasis;
        PlanningTaskInformation task = project.Foundation.PlanningTask;
        ProjectServerInformation information = server.Information ?? new();
        ProjectServerFoundation serverFoundation = server.Foundation ?? new();
        ProjectServerInitiationBasis serverBasis = serverFoundation.InitiationBasis ?? new();
        ProjectServerPlanningTask serverTask = serverFoundation.PlanningTask ?? new();

        // Three local fields are each fed by two names on the wire. They are
        // reported once, from whichever name carries something, rather than as
        // pairs of rows that point at the same box on the page.
        string queuedAuthority = Prefer(queued.AtdAuthorityName, pending.PlanningAuthorityName);
        string queuedSiteAddress = Prefer(queued.SiteAddress, pending.Location);
        string queuedBasisSummary = Prefer(queued.BasisSummary, pending.BuildingPurpose);

        return
        [
            Field("Төслийн код", pending.ProjectCode, project.Identity.Code, server.ProjectCode),
            Field("Төслийн нэр", pending.Name, project.Identity.Name, server.Name),
            Field("Захиалагч", pending.ClientName, basis.ClientName, server.ClientName),
            Field("Захиалагчийн төрөл", queued.ClientType, basis.ClientType, serverBasis.ClientType),
            Field("Захиалагчийн и-мэйл", queued.ClientEmail, basis.ClientEmail, serverBasis.ClientEmail),
            Field(
                "Төлөөлөгчийн нэр",
                queued.ClientRepresentativeName,
                basis.ClientRepresentativeName,
                serverBasis.ClientRepresentativeName),
            Field(
                "Төлөөлөгчийн албан тушаал",
                queued.ClientRepresentativePosition,
                basis.ClientRepresentativePosition,
                serverBasis.ClientRepresentativePosition),
            Field("Талбайн хаяг", queuedSiteAddress, basis.SiteAddress, information.Location),
            Field("Газрын лавлагаа", queued.LandReference, basis.LandReference, serverBasis.LandReference),
            Field("Үндэслэлийн төрөл", queued.SourceType, basis.SourceType, serverBasis.SourceType),
            Field("Хүсэлтийн дугаар", queued.RequestNumber, basis.RequestNumber, serverBasis.RequestNumber),
            Field(
                "Үндэслэл гаргасан байгууллага",
                queued.SourceOrganizationName,
                basis.SourceOrganizationName,
                serverBasis.SourceOrganizationName),
            Field("Үндэслэлийн хураангуй", queuedBasisSummary, basis.Summary, information.BuildingPurpose),
            Field("АТД-ийн дугаар", queued.AtdNumber, task.AtdNumber, serverTask.AtdNumber),
            Field(
                "АТД олгосон байгууллага",
                queuedAuthority,
                task.IssuingAuthorityName,
                server.PlanningAuthorityName),
            Field("АТД-ийн төлөв", queued.AtdStatus, task.Status, serverTask.Status),
            Field("АТД-ийн хураангуй", queued.AtdSummary, task.Summary, serverTask.Summary),
            Field(
                "Зураг төслийн байгууллага",
                pending.DesignOrganizationName,
                project.DesignOrganizationName,
                server.DesignOrganizationName),
        ];
    }

    public static string Describe(
        PendingProjectInformationUpdate pending,
        ProjectWorkspace project,
        ProjectServerSnapshot server)
    {
        IReadOnlyList<ProjectInformationConflictField> fields = Compare(pending, project, server);
        List<ProjectInformationConflictField> kept = fields.Where(field => field.Kept).ToList();
        List<ProjectInformationConflictField> replaced = fields.Where(field => field.Replaced).ToList();

        var text = new StringBuilder();
        text.Append("Энэ төслийг өөр хэрэглэгч эсвэл өөр төхөөрөмж дээр шинэчилсэн байна. ");
        text.AppendLine("Таны бичсэн зүйл устаагүй — доор яг юу болсныг харуулав.");

        if (kept.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"ТАНЫ БИЧСЭН, ХЭВЭЭР БАЙГАА ({kept.Count})");
            foreach (ProjectInformationConflictField field in kept)
                text.AppendLine($"  • {field.Label}: {field.Typed}");
        }

        if (replaced.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"ДЭЛГЭЦЭД СЕРВЕРИЙНХЭЭР СОЛИГДСОН ({replaced.Count})");
            foreach (ProjectInformationConflictField field in replaced)
                text.AppendLine($"  • {field.Label}: таных «{field.Typed}» → одоо «{Show(field.OnScreen)}»");
            text.AppendLine("  Таны утга илгээгдэхээр хүлээгдэж байгаа, устаагүй.");
        }

        if (kept.Count == 0 && replaced.Count == 0)
        {
            // Nothing was typed into any of these fields, so there is nothing
            // to reassure anyone about. Saying that plainly beats an empty
            // list under a heading that promises content.
            text.AppendLine();
            text.AppendLine("Хүлээгдэж буй шинэчлэлтэд бөглөсөн талбар алга — зөвхөн хувилбар зөрсөн байна.");
        }

        text.AppendLine();
        text.AppendLine("ДАРААХ АЛХАМ");
        if (kept.Count > 0 || replaced.Count > 0)
            text.AppendLine("  • Дахин бичих шаардлагагүй. Таны утгууд хүлээгдэж буй жагсаалтад бүрэн байна.");
        text.AppendLine("  • Хуучин суурь хувилбараас автоматаар дахин илгээхгүй.");
        text.Append("  • Болих дараад серверийн мэдээллийг харьцуулаад Засварлахыг дахин нээнэ үү.");
        return text.ToString();
    }

    /// <summary>
    /// The one line that fits in the status bar. It counts rather than
    /// reassures, because a count can be checked against the page and a
    /// reassurance cannot.
    /// </summary>
    public static string Summarize(
        PendingProjectInformationUpdate pending,
        ProjectWorkspace project,
        ProjectServerSnapshot server)
    {
        IReadOnlyList<ProjectInformationConflictField> fields = Compare(pending, project, server);
        int kept = fields.Count(field => field.Kept);
        int replaced = fields.Count(field => field.Replaced);

        if (kept == 0 && replaced == 0)
        {
            return "Хувилбарын зөрчил: хүлээгдэж буй шинэчлэлтэд бөглөсөн талбар алга. " +
                "Серверийн мэдээллийг харьцуулж дахин засна уу.";
        }

        string keptPart = kept > 0
            ? $"таны {kept} талбар дэлгэц дээр хэвээр"
            : "таны талбарууд хүлээгдэж буй жагсаалтад бүрэн";
        string replacedPart = replaced > 0
            ? $", {replaced} талбар серверийнхээр солигдсон (утга чинь илгээгдэхээр хүлээж байна)"
            : "";
        return $"Хувилбарын зөрчил: {keptPart}{replacedPart}. Дахин бичих шаардлагагүй.";
    }

    private static ProjectInformationConflictField Field(
        string label,
        string? typed,
        string? onScreen,
        string? server) => new(label, Clean(typed), Clean(onScreen), Clean(server));

    private static string Prefer(string? first, string? second) =>
        Clean(first).Length > 0 ? Clean(first) : Clean(second);

    private static string Show(string value) => value.Length > 0 ? value : "хоосон";

    private static string Clean(string? value) => (value ?? "").Trim();
}
