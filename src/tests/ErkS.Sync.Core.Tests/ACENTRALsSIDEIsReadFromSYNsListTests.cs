namespace ErkS.Sync.Core.Tests;

/// <summary>PFR, 2026-10-27: «central-info» - host, member or none, from SYN's own list, not from the path's name.</summary>
public sealed class ACENTRALsSIDEIsReadFromSYNsListTests
{
    [Fact]
    public void TheSIDEComesFromTheListedCentral()
    {
        using var root = new TemporaryFolder();
        var paths = new SyncPathMap(@"D:\Cloud Work=>" + root.Path);
        var state = new SyncState
        {
            Folders =
            [
                new SyncFolderState
                {
                    FolderId = "f-1",
                    CanonicalPath = @"D:\Cloud Work\Erin",
                    PresentHere = true,
                    Centrals =
                    [
                        new SyncCentralState { CentralId = "c-1", RelativePath = @"RVT\V1.rvt", Role = "member" },
                        new SyncCentralState { CentralId = "c-2", RelativePath = @"RVT\V2.rvt", Role = "host" },
                    ],
                },
            ],
        };
        string folder = Path.Combine(root.Path, "Erin", "RVT");

        Assert.Equal("member", SyncCentralRoles.Of(state, Path.Combine(folder, "v1.RVT"), paths));
        Assert.Equal("host", SyncCentralRoles.Of(state, Path.Combine(folder, "V2.rvt"), paths));
        Assert.Equal("none", SyncCentralRoles.Of(state, Path.Combine(folder, "V3.rvt"), paths));
        Assert.Equal("none", SyncCentralRoles.Of(null, Path.Combine(folder, "V1.rvt"), paths));
    }
}
