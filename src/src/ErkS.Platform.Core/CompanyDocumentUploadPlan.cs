namespace ErkS.Platform.Core;

/// <summary>
/// One of an organisation's scans that this device holds and the server does
/// not.
/// </summary>
/// <param name="Category">
/// <see cref="ProjectDocumentCategories.CompanyRegistrationCertificate"/> or
/// <see cref="ProjectDocumentCategories.CompanyDesignLicense"/>.
/// </param>
public sealed record CompanyDocumentUpload(
    ProjectFileReference Document,
    string Category);

/// <summary>
/// Which of a company's documents still need to reach the server.
///
/// People uploaded these into their own Studio before organisations could
/// carry them, so the scans exist - on one machine, in one company library,
/// invisible to every colleague on the same project. Sending them up is what
/// spares whoever did that work from doing it again.
///
/// The whole difficulty is not sending the same scan twice. The server now
/// takes uploads from the website as well, it caps each category at five, and
/// a person who uploads by hand and then syncs would otherwise watch their own
/// certificate arrive a second time.
/// </summary>
public static class CompanyDocumentUploadPlan
{
    /// <summary>
    /// What this device should send for one company.
    /// </summary>
    /// <param name="localCertificates">Documents in this device's company library.</param>
    /// <param name="localLicences">Ditto.</param>
    /// <param name="serverHashes">
    /// The <c>sha256</c> of every document the server already holds for this
    /// company, whatever category it is filed under. Category is ignored on
    /// purpose: the same scan filed differently is still the same scan, and
    /// sending it again would spend one of the five slots to no purpose.
    /// </param>
    /// <param name="canManage">
    /// Whether this user may write to the organisation. Somebody who merely
    /// works on a project that uses the company has no business changing the
    /// company's papers.
    /// </param>
    public static IReadOnlyList<CompanyDocumentUpload> Decide(
        IEnumerable<ProjectFileReference>? localCertificates,
        IEnumerable<ProjectFileReference>? localLicences,
        IEnumerable<string>? serverHashes,
        bool canManage)
    {
        if (!canManage)
            return [];

        var known = new HashSet<string>(
            (serverHashes ?? [])
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .Select(hash => hash.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var uploads = new List<CompanyDocumentUpload>();
        Collect(localCertificates, ProjectDocumentCategories.CompanyRegistrationCertificate);
        Collect(localLicences, ProjectDocumentCategories.CompanyDesignLicense);
        return uploads;

        void Collect(IEnumerable<ProjectFileReference>? documents, string category)
        {
            foreach (ProjectFileReference document in documents ?? [])
            {
                if (document is null)
                    continue;

                // A scan with no fingerprint cannot be compared against what
                // the server holds, so sending it risks a duplicate nobody can
                // detect afterwards. It stays where it is.
                string hash = (document.Sha256 ?? "").Trim();
                if (hash.Length == 0)
                    continue;

                // Not on this device: there is nothing to send.
                if (!document.IsAvailable || document.IsCloudPlaceholder)
                    continue;

                // Already up there - possibly put there by hand from the
                // website, possibly by this same routine yesterday.
                if (!known.Add(hash))
                    continue;

                uploads.Add(new CompanyDocumentUpload(document, category));
            }
        }
    }
}
