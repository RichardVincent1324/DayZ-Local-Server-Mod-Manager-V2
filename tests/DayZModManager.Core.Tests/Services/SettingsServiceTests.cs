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
        };

        service.Save(@"C:\data", settings);
        ConfigLoadResult<Settings> result = service.Load(@"C:\data");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal(settings.WorkshopPath, result.Value!.WorkshopPath);
        Assert.Equal(settings.ServerPath, result.Value!.ServerPath);
        Assert.Equal(settings.BatFileName, result.Value!.BatFileName);
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
}
