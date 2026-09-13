using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class ServerLogCleanupServiceTests
{
    private const string Folder = @"D:\server\map_profiles\dayzOffline.chernarusplus";

    [Fact]
    public void Cleanup_DoesNothing_WhenOnlyOneTypeAtThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        // .RPT reaches the threshold but script logs stay below it.
        for (int i = 0; i < ServerLogCleanupService.WipeThresholdPerType; i++)
        {
            AddRpt(fs, i);
            if (i < ServerLogCleanupService.WipeThresholdPerType - 1)
            {
                AddScript(fs, i);
            }
        }

        AddCrash(fs, 0);
        AddWarning(fs, 0);

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.True(result.FolderExists);
        Assert.Equal(0, result.FilesRemoved);
        Assert.True(fs.FileExists(RptPath(0)));
        Assert.True(fs.FileExists(RptPath(ServerLogCleanupService.WipeThresholdPerType - 1)));
        Assert.True(fs.FileExists(CrashPath(0)));
        Assert.True(fs.FileExists(WarningPath(0)));
    }

    [Fact]
    public void Cleanup_DoesNothing_WhenBothTypesBelowThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        for (int i = 0; i < ServerLogCleanupService.WipeThresholdPerType - 1; i++)
        {
            AddRpt(fs, i);
            AddScript(fs, i);
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.True(result.FolderExists);
        Assert.Equal(0, result.FilesRemoved);
        Assert.True(fs.FileExists(RptPath(ServerLogCleanupService.WipeThresholdPerType - 2)));
        Assert.True(fs.FileExists(ScriptPath(ServerLogCleanupService.WipeThresholdPerType - 2)));
    }

    [Fact]
    public void Cleanup_WipesAllFourGroups_WhenBothTypesReachThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        for (int i = 0; i < ServerLogCleanupService.WipeThresholdPerType; i++)
        {
            AddRpt(fs, i);
            AddScript(fs, i);
        }

        for (int i = 0; i < 4; i++)
        {
            AddCrash(fs, i);
        }

        for (int i = 0; i < 3; i++)
        {
            AddWarning(fs, i);
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        int expectedRemoved = ServerLogCleanupService.WipeThresholdPerType * 2 + 4 + 3;
        Assert.Equal(expectedRemoved, result.FilesRemoved);

        // Every log of all four groups is gone.
        Assert.Empty(fs.GetFiles(Folder, "*", recursive: false));
    }

    [Fact]
    public void Cleanup_WipesAllFourGroups_WhenBothTypesExceedThreshold()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        int count = ServerLogCleanupService.WipeThresholdPerType + 2;
        for (int i = 0; i < count; i++)
        {
            AddRpt(fs, i);
            AddScript(fs, i);
        }

        AddCrash(fs, 0);
        AddWarning(fs, 0);

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(count * 2 + 2, result.FilesRemoved);
        Assert.Empty(fs.GetFiles(Folder, "*", recursive: false));
    }

    [Fact]
    public void Cleanup_LeavesUnrelatedFilesUntouched()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        for (int i = 0; i < ServerLogCleanupService.WipeThresholdPerType; i++)
        {
            AddRpt(fs, i);
            AddScript(fs, i);
        }

        fs.AddFile($@"{Folder}\settings.cfg", "unrelated");
        fs.AddFile($@"{Folder}\DayZServer_x64_config.txt", "unrelated");

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(ServerLogCleanupService.WipeThresholdPerType * 2, result.FilesRemoved);
        Assert.True(fs.FileExists($@"{Folder}\settings.cfg"));
        Assert.True(fs.FileExists($@"{Folder}\DayZServer_x64_config.txt"));
    }

    [Fact]
    public void Cleanup_MatchesExtensionsCaseInsensitively()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Folder);
        var service = new ServerLogCleanupService(fs);

        int count = ServerLogCleanupService.WipeThresholdPerType;
        for (int i = 0; i < count; i++)
        {
            fs.AddFile($@"{Folder}\DayZServer_x64_2026-09-06_00-00-{i:00}.rpt", "x");
            fs.AddFile($@"{Folder}\script_2026-09-06_00-00-{i:00}.LOG", "x");
        }

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.Equal(count * 2, result.FilesRemoved);
        Assert.Empty(fs.GetFiles(Folder, "*", recursive: false));
    }

    [Fact]
    public void Cleanup_ReturnsNotExists_WhenFolderMissing()
    {
        var service = new ServerLogCleanupService(new FakeFileSystem());

        ServerLogCleanupResult result = service.Cleanup(Folder);

        Assert.False(result.FolderExists);
        Assert.Equal(0, result.FilesRemoved);
    }

    private static void AddRpt(FakeFileSystem fs, int index) =>
        fs.AddFile(RptPath(index), "rpt");

    private static void AddScript(FakeFileSystem fs, int index) =>
        fs.AddFile(ScriptPath(index), "script");

    private static void AddCrash(FakeFileSystem fs, int index) =>
        fs.AddFile(CrashPath(index), "crash");

    private static void AddWarning(FakeFileSystem fs, int index) =>
        fs.AddFile(WarningPath(index), "warning");

    private static string RptPath(int index) =>
        $@"{Folder}\DayZServer_x64_2026-09-06_00-00-{index:00}.RPT";

    private static string ScriptPath(int index) =>
        $@"{Folder}\script_2026-09-06_00-00-{index:00}.log";

    private static string CrashPath(int index) =>
        $@"{Folder}\crash_2026-09-06_00-00-{index:00}.log";

    private static string WarningPath(int index) =>
        $@"{Folder}\warning_2026-09-06_00-00-{index:00}.log";
}
