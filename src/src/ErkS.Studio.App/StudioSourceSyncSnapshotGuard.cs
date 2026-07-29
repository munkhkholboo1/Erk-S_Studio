using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Proves that source metadata and the in-memory sheet library still describe
/// the same immutable package set throughout one cloud sync.
/// </summary>
internal static class StudioSourceSyncSnapshotGuard
{
    public static long Capture(SheetLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return library.Version;
    }

    public static bool IsCurrent(SheetLibrary library, long expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(library);
        return library.Version == expectedVersion;
    }

    public static void Require(
        SheetLibrary library,
        long expectedVersion,
        string operation)
    {
        if (!IsCurrent(library, expectedVersion))
            throw new StudioSourceSyncSnapshotChangedException(operation);
    }
}

internal sealed class StudioSourceSyncSnapshotChangedException
    : OperationCanceledException
{
    public StudioSourceSyncSnapshotChangedException(string operation)
        : base(
            $"{operation} cancelled because a local source package changed " +
            "after the sync preview was created.")
    {
    }
}
