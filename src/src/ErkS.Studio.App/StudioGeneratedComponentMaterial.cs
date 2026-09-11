using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// The local material a generated album page is drawn from, once the project has
/// confirmed that THIS account holds it.
/// </summary>
/// <param name="OwnerEmail">Who the material belongs to.</param>
/// <param name="SourceKey">
/// The stable identity of the material. Empty means nothing local feeds this
/// page - it is canonical, and only an account that may rewrite canonical
/// metadata can redraw it.
/// </param>
/// <param name="ComponentKind">
/// How the cloud files the component once it is redrawn from that material.
/// </param>
internal sealed record StudioGeneratedComponentMaterial(
    string OwnerEmail,
    string SourceKey,
    string ComponentKind)
{
    /// <summary>Nothing this account holds draws this page.</summary>
    public static readonly StudioGeneratedComponentMaterial None = new("", "", "");

    public bool IsHeldLocally => !string.IsNullOrWhiteSpace(SourceKey);
}

/// <summary>
/// Answers one question about a generated album page: WHICH LOCAL MATERIAL IS IT
/// DRAWN FROM, and does this account hold that material.
///
/// 🔴 THE OWNER COULD NOT SEND THEIR OWN RENDERS. Every generated page was gated
/// on <c>canManageCanonicalMetadata</c> - an administrator's right - so pages
/// Studio draws from a person's OWN files were treated exactly like the cover.
/// The owner of the licence is an architect, not an administrator, and said what
/// the line should be: «Би төслийн мэдээлэл болон байгууллагын мэдээлэлд
/// өөрчлөлт оруулах эрхгүй байгаа. Тэр нь ч зөв. Гагцхүү би өөрийн эх үүсвэрээ
/// бүрэн удирдах эрхтэй байх ёстой.»
///
/// So authority here is never granted to a CODE. Each arm says only which
/// material feeds that page; the PROJECT then answers whether this account holds
/// it, through the same predicates the rest of Studio uses. A page whose material
/// cannot be named falls through to <see cref="StudioGeneratedComponentMaterial.None"/>
/// and stays canonical - which is why a new generated page is administrator-only
/// until somebody deliberately says what draws it.
///
/// This is also the correspondence the upload path needs, and it used to be
/// written twice. Two copies of a rule about who may overwrite whose pages is a
/// way for one of them to drift.
/// </summary>
internal static class StudioGeneratedComponentAuthority
{
    public static StudioGeneratedComponentMaterial Resolve(
        ProjectWorkspace project,
        string? componentCode,
        string? ownerEmail,
        bool hasOwnedAtd,
        bool hasVisualizations)
    {
        ArgumentNullException.ThrowIfNull(project);
        string code = (componentCode ?? "").Trim();
        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();

        // The approved planning task is a document the person uploaded. Studio
        // draws the page; the file underneath it is theirs.
        if (code.Equals(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return hasOwnedAtd && !string.IsNullOrWhiteSpace(owner)
                ? new StudioGeneratedComponentMaterial(
                    owner,
                    StudioAlbumComponentIdentity.AtdSourceKey,
                    StudioAlbumComponentIdentity.SourceComponentKind)
                : StudioGeneratedComponentMaterial.None;
        }

        // The «Харагдах байдал» pages are a layout over images the person added.
        if (code.Equals(
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return hasVisualizations && !string.IsNullOrWhiteSpace(owner)
                ? new StudioGeneratedComponentMaterial(
                    owner,
                    StudioAlbumComponentIdentity.VisualizationSourceKey,
                    StudioAlbumComponentIdentity.SourceComponentKind)
                : StudioGeneratedComponentMaterial.None;
        }

        // The location scheme is drawn from a general-plan source, and exactly
        // one contributor controls it. That policy already decides who; asking
        // it again here rather than re-deciding is the point.
        if (code.Equals(
                ProjectCloudSyncMetadata.SiteContextComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            ProjectSiteContextEditAuthority authority =
                ProjectSiteContextEditingPolicy.Resolve(project, owner);
            return authority.CanEdit && !string.IsNullOrWhiteSpace(authority.SourceKey)
                ? new StudioGeneratedComponentMaterial(
                    string.IsNullOrWhiteSpace(authority.SourceOwnerEmail)
                        ? owner
                        : authority.SourceOwnerEmail,
                    authority.SourceKey,
                    StudioAlbumComponentIdentity.SiteContextComponentKind)
                : StudioGeneratedComponentMaterial.None;
        }

        // The cover, the drawing list, a building's sub-cover, the organisation's
        // certificate and licence: every one of them is drawn from project or
        // company information, which this account is not allowed to change - and
        // the owner agreed that it should not be. Anything unrecognised lands
        // here too, on purpose.
        return StudioGeneratedComponentMaterial.None;
    }
}
