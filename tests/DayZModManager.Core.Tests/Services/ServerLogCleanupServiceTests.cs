using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ServerLogCleanupServiceTests
{
    private const string Folder = @"D:\server\map_profiles\dayzOffline.chernarusplus";

    [Fact]
    public void Cleanup_DoesNothing_WhenAtOrBelowThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        for (int i = 0; i < ServerLogCleanupService.PruneThreshold; i++)
        {
            AddRpt(fs, i);
            AddScript(fs, i);
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.True(result.FolderExists);
        Assert.Equal(0, result.RptRemoved);
        Assert.Equal(0, result.ScriptRemoved);
        Assert.True(fs.FileExists(RptPath(0)));
        Assert.True(fs.FileExists(RptPath(ServerLogCleanupService.PruneThreshold - 1)));
    }

    [Fact]
    public void Cleanup_RemovesOldest_KeepingNewestThree_WhenMoreThanThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        int count = ServerLogCleanupService.PruneThreshold + 1;
        for (int i = 0; i < count; i++)
        {
            AddRpt(fs, i);
            AddScript(fs, i);
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(ServerLogCleanupService.PruneThreshold + 1 - ServerLogCleanupService.RetainedPerGroup, result.RptRemoved);
        Assert.Equal(ServerLogCleanupService.PruneThreshold + 1 - ServerLogCleanupService.RetainedPerGroup, result.ScriptRemoved);

        // The oldest files were removed; the newest three remain.
        for (int i = 0; i < count - ServerLogCleanupService.RetainedPerGroup; i++)
        {
            Assert.False(fs.FileExists(RptPath(i)), $"oldest .RPT {i} should be deleted");
            Assert.False(fs.FileExists(ScriptPath(i)), $"oldest script log {i} should be deleted");
        }

        for (int i = count - ServerLogCleanupService.RetainedPerGroup; i < count; i++)
        {
            Assert.True(fs.FileExists(RptPath(i)), $"newest .RPT {i} should be kept");
            Assert.True(fs.FileExists(ScriptPath(i)), $"newest script log {i} should be kept");
        }
    }

    [Fact]
    public void Cleanup_TrimsOnlyTheOverLimitType()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        // 12 .RPT files (over the threshold) but only 3 script logs.
        for (int i = 0; i < 12; i++)
        {
            AddRpt(fs, i);
        }

        for (int i = 0; i < 3; i++)
        {
            AddScript(fs, i);
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(12 - ServerLogCleanupService.RetainedPerGroup, result.RptRemoved);
        Assert.Equal(0, result.ScriptRemoved);
        Assert.True(fs.FileExists(ScriptPath(0)));
        Assert.True(fs.FileExists(ScriptPath(2)));
    }

    [Fact]
    public void Cleanup_LeavesUnrelatedFilesUntouched()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        int count = ServerLogCleanupService.PruneThreshold + 1;
        for (int i = 0; i < count; i++)
        {
            AddRpt(fs, i);
            AddScript(fs, i);
        }

        fs.AddFile($@"{Folder}\settings.cfg", "unrelated");
        fs.AddFile($@"{Folder}\DayZServer_x64_config.txt", "unrelated");

        service.Cleanup(Folder);

        Assert.True(fs.FileExists($@"{Folder}\settings.cfg"));
        Assert.True(fs.FileExists($@"{Folder}\DayZServer_x64_config.txt"));
    }

    [Fact]
    public void Cleanup_MatchesExtensionCaseInsensitively()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        int count = ServerLogCleanupService.PruneThreshold + 1;
        for (int i = 0; i < count; i++)
        {
            fs.AddFile($@"{Folder}\DayZServer_x64_2026-09-06_00-00-{i:00}.rpt", "x");
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(count - ServerLogCleanupService.RetainedPerGroup, result.RptRemoved);
    }

    [Fact]
    public void Cleanup_ReturnsNotExists_WhenFolderMissing()
    {
        var service = new ServerLogCleanupService(new FakeFileSystem());

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.False(result.FolderExists);
        Assert.Equal(0, result.RptRemoved);
        Assert.Equal(0, result.ScriptRemoved);
    }

    private static void AddRpt(FakeFileSystem fs, int index) =>
        fs.AddFile(RptPath(index), "rpt");

    private static void AddScript(FakeFileSystem fs, int index) =>
        fs.AddFile(ScriptPath(index), "script");

    private static string RptPath(int index) =>
        $@"{Folder}\DayZServer_x64_2026-09-06_00-00-{index:00}.RPT";

    private static string ScriptPath(int index) =>
        $@"{Folder}\script_2026-09-06_00-00-{index:00}.log";
}
