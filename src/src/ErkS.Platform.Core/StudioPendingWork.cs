namespace ErkS.Platform.Core;

/// <summary>
/// Everything this device has made and not yet delivered, counted by KIND.
///
/// 🔴 WRITTEN BECAUSE READERS KEPT LOOKING IN ONE BUCKET. Unsent work lives in
/// six separate places on the project, and the two things that TELL a person
/// whether they are up to date each read a different subset of them:
///
///   the cloud indicator   read album components only → painted GREEN while two
///                         changed sources sat unsent
///   the refresh report    read album components only → «Таны оруулга: илгээх
///                         зүйл байсангүй» while the same two sat unsent
///
/// Both were honest about what they read. Neither read enough. The owner spent a
/// morning believing their work had gone up, and pressed the button again.
///
/// 🔴 THE POINT OF THIS TYPE IS THAT A SEVENTH BUCKET CANNOT BE FORGOTTEN. It is
/// positional, so adding a kind breaks every construction site at compile time
/// rather than quietly reading zero. A rule that depends on somebody remembering
/// is the rule that produced this.
/// </summary>
/// <param name="SourcePackages">Sheet packages recorded and not yet synced.</param>
/// <param name="AlbumComponents">Album components waiting to be merged.</param>
/// <param name="ProjectInformation">A queued canonical project-information update.</param>
/// <param name="CompanyAssignment">A design-company assignment made locally.</param>
/// <param name="BuildingComposition">Building composition edited locally.</param>
/// <param name="CanonicalTitleBlock">A canonical title block waiting to be published.</param>
public readonly record struct StudioPendingWork(
    int SourcePackages,
    int AlbumComponents,
    int ProjectInformation,
    int CompanyAssignment,
    int BuildingComposition,
    int CanonicalTitleBlock)
{
    /// <summary>
    /// How many pieces of work are waiting, across every kind.
    ///
    /// Derived here rather than at each reader: «is anything waiting» is the
    /// question both the indicator and the report are really asking, and each
    /// answering it for itself is how they came to disagree.
    /// </summary>
    public int Total =>
        SourcePackages +
        AlbumComponents +
        ProjectInformation +
        CompanyAssignment +
        BuildingComposition +
        CanonicalTitleBlock;

    public bool Any => Total > 0;

    /// <summary>
    /// Reads every bucket off the project.
    ///
    /// 🔴 STRUCTURAL AND CHEAP ON PURPOSE. This answers WHICH BUCKETS hold
    /// something - counts of records and flags, no file read and no hashing.
    /// Whether a given piece can actually be SENT from this device is a
    /// different and far more expensive question, and the indicator must never
    /// be the thing that asks it: that cost is what froze the window for
    /// minutes at a time.
    /// </summary>
    public static StudioPendingWork Of(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectCloudLink cloud = project.Cloud;
        return new StudioPendingWork(
            ProjectCloudSyncMetadata.PendingSourcePackages(project).Count,
            (cloud.PendingAlbumComponentCodes ?? []).Count,
            cloud.PendingProjectInformation is null ? 0 : 1,
            project.Foundation.DesignCompany.AssignmentSource.Equals(
                "StudioCloudPending",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0,
            cloud.BuildingCompositionPending ? 1 : 0,
            cloud.CanonicalTitleBlockPending ? 1 : 0);
    }
}
