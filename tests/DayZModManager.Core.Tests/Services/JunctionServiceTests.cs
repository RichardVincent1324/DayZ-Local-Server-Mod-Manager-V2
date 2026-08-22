using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class JunctionServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string WorkshopPath = @"D:\DayZ\!Workshop";

    private static (JunctionService Service, FakeFileSystem Fs, FakeJunctionOperations Junctions) CreateService()
    {
        var fs = new FakeFileSystem();
        var junctions = new FakeJunctionOperations();
        return (new JunctionService(fs, junctions), fs, junctions);
    }

    [Fact]
    public void Sync_CreatesMissingJunctionsForLoadedMods()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.True(junctions.IsJunction($@"{ServerPath}\@CF"));
        Assert.Equal($@"{WorkshopPath}\@CF", junctions.Targets[$@"{ServerPath}\@CF"]);
    }

    [Fact]
    public void Sync_SkipsExistingJunctions()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath, "@CF");
        junctions.Create($@"{ServerPath}\@CF", $@"{WorkshopPath}\@CF");

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Sync_RepointsJunction_WhenTargetDiffers()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        junctions.Create($@"{ServerPath}\@CF", $@"D:\OldWorkshop\@CF");

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Created);
        Assert.Equal($@"{WorkshopPath}\@CF", junctions.Targets[$@"{ServerPath}\@CF"]);
    }

    [Fact]
    public void Sync_RemovesJunctionsNotInLoadedSet()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath, "@CF", "@OldMod");
        junctions.Create($@"{ServerPath}\@CF", $@"{WorkshopPath}\@CF");
        junctions.Create($@"{ServerPath}\@OldMod", $@"{WorkshopPath}\@OldMod");

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(1, result.Removed);
        Assert.False(junctions.IsJunction($@"{ServerPath}\@OldMod"));
    }

    [Fact]
    public void Sync_ReportsFailure_OnPhysicalFolderConflict()
    {
        (JunctionService service, FakeFileSystem fs, _) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath, "@CF"); // physical folder, not a junction

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Messages, m => m.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Verify_ReturnsLoadedModsMissingJunction()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);
        junctions.Create($@"{ServerPath}\@CF", $@"{WorkshopPath}\@CF");

        IReadOnlyList<string> missing = service.Verify(ServerPath, new[] { "@CF", "@Expansion" });

        Assert.Equal(new[] { "@Expansion" }, missing);
    }

    [Fact]
    public void FindOrphanedJunctions_ReturnsJunctionsOutsideValidSet()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(ServerPath, "@CF", "@Ghost", "@Physical");
        junctions.Create($@"{ServerPath}\@CF", "x");
        junctions.Create($@"{ServerPath}\@Ghost", "x");

        var valid = new HashSet<string>(new[] { "@CF" }, StringComparer.Ordinal);
        IReadOnlyList<string> orphans = service.FindOrphanedJunctions(ServerPath, valid);

        Assert.Equal(new[] { "@Ghost" }, orphans);
    }
}
