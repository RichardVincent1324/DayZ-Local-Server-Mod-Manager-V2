using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class JunctionServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string WorkshopPath = @"D:\DayZ\!Workshop";
    private const string PresetKey = "1";

    private static string ModFolder => Path.Combine(ServerPath, ModListFolder.Name, PresetKey);

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

        service.Sync(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

        Assert.True(fs.DirectoryExists(ModFolder));
    }

    [Fact]
    public void Sync_CreatesMissingJunctionsForLoadedMods()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

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

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

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

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

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

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

        Assert.Equal(1, result.Removed);
        Assert.False(junctions.IsJunction(Path.Combine(ModFolder, "@OldMod")));
        Assert.True(junctions.IsJunction(Path.Combine(ModFolder, "@CF")));
    }

    [Fact]
    public void Sync_LeavesRootJunctionsOutsideModListUntouched()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        string rootJunction = Path.Combine(ServerPath, "@CF");
        junctions.Create(rootJunction, Path.Combine(WorkshopPath, "@CF"));

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

        Assert.True(junctions.IsJunction(rootJunction));
        Assert.Equal(1, result.Created);
    }

    [Fact]
    public void Sync_ReportsFailure_OnPhysicalFolderConflict()
    {
        (JunctionService service, FakeFileSystem fs, _) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(ModFolder, "@CF"); // physical folder, not a junction

        JunctionSyncResult result = service.Sync(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

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

        IReadOnlyList<string> missing = service.Verify(ServerPath, PresetKey, new[] { "@CF", "@Expansion" });

        Assert.Equal(new[] { "@Expansion" }, missing);
    }

    [Fact]
    public void Verify_ReturnsAllLoadedMods_WhenFolderMissing()
    {
        (JunctionService service, FakeFileSystem fs, _) = CreateService();
        fs.AddDirectory(ServerPath);

        IReadOnlyList<string> missing = service.Verify(ServerPath, PresetKey, new[] { "@CF", "@Expansion" });

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
        IReadOnlyList<string> orphans = service.FindOrphanedJunctions(ServerPath, PresetKey, valid);

        Assert.Equal(new[] { "@Ghost" }, orphans);
    }

    [Fact]
    public void PrepareLoaded_DoesNotRemoveOrphansOrRetargetStaleLinks()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(ModFolder, "@CF", "@Expansion", "@OldMod");
        junctions.Create(Path.Combine(ModFolder, "@CF"), Path.Combine(WorkshopPath, "@CF"));
        junctions.Create(Path.Combine(ModFolder, "@Expansion"), @"D:\OldWorkshop\@Expansion"); // stale target
        junctions.Create(Path.Combine(ModFolder, "@OldMod"), Path.Combine(WorkshopPath, "@OldMod")); // orphan

        JunctionSyncResult result = service.PrepareLoaded(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Removed);

        // Nothing destructive happened during preparation.
        Assert.True(junctions.IsJunction(Path.Combine(ModFolder, "@Expansion")));
        Assert.Equal(@"D:\OldWorkshop\@Expansion", junctions.Targets[Path.Combine(ModFolder, "@Expansion")]);
        Assert.True(junctions.IsJunction(Path.Combine(ModFolder, "@OldMod")));
    }

    [Fact]
    public void Finalize_RetargetsLoadedMods_AndRemovesOrphans()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF", "@Expansion");
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(ModFolder, "@CF", "@Expansion", "@OldMod");
        junctions.Create(Path.Combine(ModFolder, "@CF"), Path.Combine(WorkshopPath, "@CF"));
        junctions.Create(Path.Combine(ModFolder, "@Expansion"), @"D:\OldWorkshop\@Expansion"); // stale, still loaded
        junctions.Create(Path.Combine(ModFolder, "@OldMod"), Path.Combine(WorkshopPath, "@OldMod")); // orphan

        JunctionSyncResult result = service.Finalize(ServerPath, WorkshopPath, PresetKey, new[] { "@CF", "@Expansion" });

        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Removed);
        Assert.False(junctions.IsJunction(Path.Combine(ModFolder, "@OldMod")));
        Assert.Equal(Path.Combine(WorkshopPath, "@Expansion"), junctions.Targets[Path.Combine(ModFolder, "@Expansion")]);
    }

    [Fact]
    public void PrepareLoaded_KeepsWorkingJunction_WhenNewTargetMissing()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(ServerPath);
        string link = Path.Combine(ModFolder, "@CF");
        junctions.Create(link, @"D:\OldWorkshop\@CF");

        // Workshop path changed and @CF is not present in the new location yet.
        JunctionSyncResult result = service.PrepareLoaded(ServerPath, WorkshopPath, PresetKey, new[] { "@CF" });

        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Messages, m => m.Contains("Mod not found in workshop", StringComparison.OrdinalIgnoreCase));

        // The previously-working junction must be left intact for a failed/aborted Apply.
        Assert.True(junctions.IsJunction(link));
        Assert.Equal(@"D:\OldWorkshop\@CF", junctions.Targets[link]);
    }

    [Fact]
    public void Finalize_DoesNotTouchAnotherPresetsJunctions()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(WorkshopPath, "@CF");
        fs.AddDirectory(ServerPath);

        string otherFolder = Path.Combine(ServerPath, ModListFolder.Name, "2");
        fs.AddDirectory(otherFolder, "@CF", "@OtherOnly");
        junctions.Create(Path.Combine(otherFolder, "@CF"), Path.Combine(WorkshopPath, "@CF"));
        junctions.Create(Path.Combine(otherFolder, "@OtherOnly"), Path.Combine(WorkshopPath, "@OtherOnly"));

        // Finalizing preset "1" (which has no mods) must leave preset "2" untouched.
        JunctionSyncResult result = service.Finalize(ServerPath, WorkshopPath, PresetKey, Array.Empty<string>());

        Assert.Equal(0, result.Removed);
        Assert.True(junctions.IsJunction(Path.Combine(otherFolder, "@CF")));
        Assert.True(junctions.IsJunction(Path.Combine(otherFolder, "@OtherOnly")));
    }

    [Fact]
    public void DeleteJunctionFolder_RemovesPresetFolderAndLinks()
    {
        (JunctionService service, FakeFileSystem fs, FakeJunctionOperations junctions) = CreateService();
        fs.AddDirectory(ServerPath);
        fs.AddDirectory(ModFolder, "@CF", "@Expansion");
        junctions.Create(Path.Combine(ModFolder, "@CF"), Path.Combine(WorkshopPath, "@CF"));
        junctions.Create(Path.Combine(ModFolder, "@Expansion"), Path.Combine(WorkshopPath, "@Expansion"));

        service.DeleteJunctionFolder(ServerPath, PresetKey);

        Assert.False(junctions.IsJunction(Path.Combine(ModFolder, "@CF")));
        Assert.False(junctions.IsJunction(Path.Combine(ModFolder, "@Expansion")));
        Assert.False(fs.DirectoryExists(ModFolder));
    }

    [Fact]
    public void DeleteJunctionFolder_IsNoOp_WhenFolderMissing()
    {
        (JunctionService service, FakeFileSystem fs, _) = CreateService();
        fs.AddDirectory(ServerPath);

        service.DeleteJunctionFolder(ServerPath, PresetKey);

        Assert.False(fs.DirectoryExists(ModFolder));
    }
}
