using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public void Load_ReturnsMissing_WhenFileAbsent()
    {
        var service = new SettingsService(new FakeFileSystem());

        ConfigLoadResult<Settings> result = service.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Missing, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Load_ReturnsCorrupt_WhenJsonInvalid()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\data\settings.json", "{ not valid json");
        var service = new SettingsService(fs);

        ConfigLoadResult<Settings> result = service.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Corrupt, result.Status);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var fs = new FakeFileSystem();
        var service = new SettingsService(fs);
        var settings = new Settings
        {
            WorkshopPath = @"D:\DayZ\!Workshop",
            ServerPath = @"D:\DayZServer",
            BatchFile = "LocalServer.example.bat",
            AutoCleanServerLogs = true,
        };

        service.Save(@"C:\data", settings);
        ConfigLoadResult<Settings> result = service.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal(settings.WorkshopPath, result.Value!.WorkshopPath);
        Assert.Equal(settings.ServerPath, result.Value!.ServerPath);
        Assert.Equal(settings.BatchFile, result.Value!.BatchFile);
        Assert.True(result.Value.AutoCleanServerLogs);
    }

    [Fact]
    public void Save_WritesCamelCaseWithoutBom()
    {
        var fs = new FakeFileSystem();
        var service = new SettingsService(fs);
        service.Save(@"C:\data", new Settings { WorkshopPath = @"D:\x" });

        string? json = fs.TryGetFileContents(@"C:\data\settings.json");

        Assert.NotNull(json);
        Assert.Contains("\"workshopPath\"", json);
        Assert.DoesNotContain("\"WorkshopPath\"", json);
        Assert.False(json.Contains('\uFEFF'), "settings.json should not contain a UTF-8 BOM");
    }

    [Fact]
    public void Save_DoesNotWriteDerivedBatFilePath()
    {
        var fs = new FakeFileSystem();
        var service = new SettingsService(fs);
        service.Save(@"C:\data", new Settings { ServerPath = @"D:\DayZServer", BatchFile = "run.bat" });

        string? json = fs.TryGetFileContents(@"C:\data\settings.json");

        Assert.NotNull(json);
        Assert.DoesNotContain("\"batFilePath\"", json);
        Assert.Contains("\"batchFile\": \"run.bat\"", json);
        Assert.DoesNotContain("batFileName", json);
    }

    [Fact]
    public void Save_ThenLoad_DefaultBatchFileIsEmpty()
    {
        var fs = new FakeFileSystem();
        var service = new SettingsService(fs);
        service.Save(@"C:\data", new Settings());

        ConfigLoadResult<Settings> result = service.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal(string.Empty, result.Value!.BatchFile);
        Assert.Equal(string.Empty, result.Value!.BatFilePath);
    }

    [Fact]
    public void BackupCorrupt_PreservesExistingFile()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\data\settings.json", "not json");
        var service = new SettingsService(fs);

        Assert.True(service.BackupCorrupt(@"C:\data"));
        Assert.Equal("not json", fs.TryGetFileContents(@"C:\data\settings.json.corrupt"));
        Assert.Equal("not json", fs.TryGetFileContents(@"C:\data\settings.json"));
    }

    [Fact]
    public void BackupCorrupt_DoesNotOverwriteExistingBackup()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\data\settings.json", "not json");
        fs.AddFile(@"C:\data\settings.json.corrupt", "older backup");
        var service = new SettingsService(fs);

        Assert.True(service.BackupCorrupt(@"C:\data"));
        Assert.Equal("older backup", fs.TryGetFileContents(@"C:\data\settings.json.corrupt"));
    }

    [Fact]
    public void BackupCorrupt_ReturnsFalse_WhenFileMissing()
    {
        var service = new SettingsService(new FakeFileSystem());

        Assert.False(service.BackupCorrupt(@"C:\data"));
    }
}
