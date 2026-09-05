namespace ErkS.Studio.App.Tests;

/// <summary>
/// Test classes that redirect ERKS_STUDIO_DATA_ROOT.
///
/// The variable is process-wide, so two such classes running at once take turns
/// pointing the whole app's data root at each other's temp folder - and the
/// failure lands on whichever test happened to read a file in between. That
/// looks exactly like a real bug in the store under test, which is how it cost
/// an afternoon. xUnit runs one collection at a time, so naming them all here
/// is the fix.
/// </summary>
[CollectionDefinition(Name)]
public sealed class StudioDataRootCollection
{
    public const string Name = "studio-data-root";
}
