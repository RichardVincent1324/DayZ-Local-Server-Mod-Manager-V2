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
            BatFileName = "LocalServer.example.bat",
            DataDirectory = @"D:\DayZServer\DayZ-Local-Server-Mod-Manager-Data",
        };

        service.Save(@"C:\data", settings);
        ConfigLoadResult<Settings> result = service.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal(settings.WorkshopPath, result.Value!.WorkshopPath);
        Assert.Equal(settings.ServerPath, result.Value!.ServerPath);
        Assert.Equal(settings.BatFileName, result.Value!.BatFileName);
        Assert.Equal(settings.DataDirectory, result.Value!.DataDirectory);
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
    public void Load_MigratesSchemaV1ToCurrentVersion()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\data\settings.json", "{\"workshopPath\":\"D:\\\\ws\",\"schemaVersion\":1}");
        var service = new SettingsService(fs);

        ConfigLoadResult<Settings> result = service.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal(Settings.CurrentSchemaVersion, result.Value!.SchemaVersion);
        Assert.Equal(@"D:\ws", result.Value.WorkshopPath);
        Assert.Equal(string.Empty, result.Value.DataDirectory);
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
