using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ServerLogCleanupServiceTests
{
    private const string Folder = @"D:\server\map_profiles\dayzOffline.chernarusplus";

    [Fact]
    public void Cleanup_DoesNothing_WhenAtThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        // Exactly the threshold: no cleanup happens at or below it.
        for (int i = 0; i < ServerLogCleanupService.CleanupThreshold; i++)
        {
            fs.AddFile($@"{Folder}\DayZServer_x64_2026-09-06_00-00-{i:00}.RPT", "rpt");
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.True(result.FolderExists);
        Assert.Equal(0, result.Removed);
        Assert.True(fs.FileExists($@"{Folder}\DayZServer_x64_2026-09-06_00-00-00.RPT"));
    }

    [Fact]
    public void Cleanup_DeletesAll_WhenOverThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        // 31 total: 16 rpt + 15 log.
        for (int i = 0; i < 16; i++)
        {
            fs.AddFile($@"{Folder}\DayZServer_x64_2026-09-06_00-00-{i:00}.RPT", "rpt");
        }

        for (int i = 0; i < 15; i++)
        {
            fs.AddFile($@"{Folder}\script_2026-09-06_00-00-{i:00}.log", "log");
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(31, result.Removed);
        Assert.False(fs.FileExists($@"{Folder}\DayZServer_x64_2026-09-06_00-00-00.RPT"));
        Assert.False(fs.FileExists($@"{Folder}\DayZServer_x64_2026-09-06_00-00-15.RPT"));
        Assert.False(fs.FileExists($@"{Folder}\script_2026-09-06_00-00-00.log"));
        Assert.False(fs.FileExists($@"{Folder}\script_2026-09-06_00-00-14.log"));
    }

    [Fact]
    public void Cleanup_CountsBothExtensionsTogether()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        // 20 rpt + 11 log = 31, even though neither type alone is over the limit.
        for (int i = 0; i < 20; i++)
        {
            fs.AddFile($@"{Folder}\crash_{i:00}.rpt", "rpt");
        }

        for (int i = 0; i < 11; i++)
        {
            fs.AddFile($@"{Folder}\server_{i:00}.log", "log");
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(31, result.Removed);
    }

    [Fact]
    public void Cleanup_LeavesUnrelatedFilesUntouched()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        for (int i = 0; i < 31; i++)
        {
            fs.AddFile($@"{Folder}\log_{i:00}.rpt", "rpt");
        }

        fs.AddFile($@"{Folder}\settings.cfg", "unrelated");
        fs.AddFile($@"{Folder}\notes.txt", "unrelated");

        service.Cleanup(Folder);

        Assert.True(fs.FileExists($@"{Folder}\settings.cfg"));
        Assert.True(fs.FileExists($@"{Folder}\notes.txt"));
    }

    [Fact]
    public void Cleanup_MatchesExtensionCaseInsensitively()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        for (int i = 0; i < 31; i++)
        {
            fs.AddFile($@"{Folder}\DayZServer_x64_2026-09-06_00-00-{i:00}.rpt", "x");
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(31, result.Removed);
    }

    [Fact]
    public void Cleanup_ReturnsNotExists_WhenFolderMissing()
    {
        var service = new ServerLogCleanupService(new FakeFileSystem());

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.False(result.FolderExists);
        Assert.Equal(0, result.Removed);
    }
}
