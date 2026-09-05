using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class JunctionServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string WorkshopPath = @"D:\DayZ\!Workshop";

    private static string ModFolder => Path.Combine(ServerPath, ModListFolder.Name);

    private static (JunctionService Service, FakeFileSystem Fs, FakeJunctionOperations Junctions) CreateService()
    {
        var fs = new FakeFileSystem();
        var junctions = new FakeJunctionOperations();
        return (new JunctionService(fs, junctions), fs, junctions);
    }

    [Fact]
    public void Sync_CreatesFolder_WhenMissing()
    {
        (JunctionService service, FakeFileSystem fs, _) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);

        service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.True(fs.DirectoryExists(ModFolder));
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
        string link = Path.Combine(ModFolder, "@CF");
        Assert.True(junctions.IsJunction(link));
        Assert.Equal(Path.Combine(WorkshopPath, "@CF"), junctions.Targets[link]);
    }

    [Fact]
    public void Sync_SkipsExistingJunctions()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        string link = Path.Combine(ModFolder, "@CF");
        junctions.Create(link, Path.Combine(WorkshopPath, "@CF"));

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Sync_RepointsJunction_WhenTargetDiffers()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        string link = Path.Combine(ModFolder, "@CF");
        junctions.Create(link, @"D:\OldWorkshop\@CF");

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Created);
        Assert.Equal(Path.Combine(WorkshopPath, "@CF"), junctions.Targets[link]);
    }

    [Fact]
    public void Sync_RemovesJunctionsNotInLoadedSet()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(ModFolder, "@CF", "@OldMod");
        junctions.Create(Path.Combine(ModFolder, "@CF"), Path.Combine(WorkshopPath, "@CF"));
        junctions.Create(Path.Combine(ModFolder, "@OldMod"), Path.Combine(WorkshopPath, "@OldMod"));

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.Equal(1, result.Removed);
        Assert.False(junctions.IsJunction(Path.Combine(ModFolder, "@OldMod")));
        Assert.True(junctions.IsJunction(Path.Combine(ModFolder, "@CF")));
    }

    [Fact]
    public void Sync_LeavesLegacyRootJunctionsUntouched()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        string legacy = Path.Combine(ServerPath, "@CF");
        junctions.Create(legacy, Path.Combine(WorkshopPath, "@CF"));

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, new[] { "@CF" });

        Assert.True(junctions.IsJunction(legacy));
        Assert.Equal(1, result.Created);
    }

    [Fact]
    public void Sync_ReportsFailure_OnPhysicalFolderConflict()
    {
        (JunctionService service, FakeFileSystem fs, _) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(ModFolder, "@CF"); // physical folder, not a junction

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
        junctions.Create(Path.Combine(ModFolder, "@CF"), Path.Combine(WorkshopPath, "@CF"));

        IReadOnlyList<string> missing = service.Verify(ServerPath, new[] { "@CF", "@Expansion" });

        Assert.Equal(new[] { "@Expansion" }, missing);
    }

    [Fact]
    public void Verify_ReturnsAllLoadedMods_WhenFolderMissing()
    {
        (JunctionService service, FakeFileSystem fs, _) = CreateService();
        fs.AddDirectory(ServerPath);

        IReadOnlyList<string> missing = service.Verify(ServerPath, new[] { "@CF", "@Expansion" });

        Assert.Equal(new[] { "@CF", "@Expansion" }, missing);
    }

    [Fact]
    public void FindOrphanedJunctions_ReturnsJunctionsOutsideValidSet()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(ModFolder, "@CF", "@Ghost", "@Physical");
        junctions.Create(Path.Combine(ModFolder, "@CF"), "x");
        junctions.Create(Path.Combine(ModFolder, "@Ghost"), "x");

        var valid = new HashSet<string>(new[] { "@CF" }, StringComparer.Ordinal);
        IReadOnlyList<string> orphans = service.FindOrphanedJunctions(ServerPath, valid);

        Assert.Equal(new[] { "@Ghost" }, orphans);
    }
}
