using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class TypesBackupServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string MapName = "dayzOffline.chernarusplus";
    private const string DataDirectory = @"D:\data";

    private static string MissionPath => $@"{ServerPath}\mpmissions\{MapName}";

    private static string LiveTypes => $@"{MissionPath}\db\ModTypes";

    private static void SeedLiveTypes(FakeFileSystem fs, string fileName, string contents = "types-data")
    {
        fs.AddDirectory($@"{MissionPath}\db", "ModTypes");
        fs.AddFile($@"{LiveTypes}\{fileName}", contents);
    }

    private static TypesBackupService Create(FakeFileSystem fs) => new(fs);

    [Fact]
    public void Capture_CopiesLiveFilesAndMapping()
    {
        var fs = new FakeFileSystem();
        SeedLiveTypes(fs, "CF_types.xml");
        var mapping = new MapTypesConfig
        {
            Mods = { new ModTypesEntry { ModName = "@CF", GeneratedFiles = { @"db\ModTypes\CF_types.xml" } } },
        };

        TypesBackupResult result = Create(fs).Capture(ServerPath, MapName, DataDirectory, mapping);

        Assert.True(result.Success);
        string backup = Path.Combine(DataDirectory, "ModTypes_Backup", MapName);
        Assert.Equal("types-data", fs.TryGetFileContents($@"{backup}\ModTypes\CF_types.xml"));
        Assert.NotNull(fs.TryGetFileContents($@"{backup}\config.json"));
    }

    [Fact]
    public void EnsureCaptured_DoesNotOverwriteExistingBackup()
    {
        var fs = new FakeFileSystem();
        SeedLiveTypes(fs, "CF_types.xml", "original");
        TypesBackupService service = Create(fs);
        service.Capture(ServerPath, MapName, DataDirectory, new MapTypesConfig());

        // The live folder changes (as if a world was loaded), then Ensure runs.
        fs.DeleteFile($@"{LiveTypes}\CF_types.xml");
        fs.AddFile($@"{LiveTypes}\New_types.xml", "new");
        TypesBackupResult result = service.EnsureCaptured(ServerPath, MapName, DataDirectory, new MapTypesConfig());

        Assert.True(result.Success);
        string backup = Path.Combine(DataDirectory, "ModTypes_Backup", MapName);
        Assert.Equal("original", fs.TryGetFileContents($@"{backup}\ModTypes\CF_types.xml"));
        Assert.False(fs.FileExists($@"{backup}\ModTypes\New_types.xml"));
    }

    [Fact]
    public void Restore_ReplacesLiveTypesAndReturnsMapping()
    {
        var fs = new FakeFileSystem();
        SeedLiveTypes(fs, "Configured_types.xml", "configured");
        TypesBackupService service = Create(fs);
        var mapping = new MapTypesConfig
        {
            Mods = { new ModTypesEntry { ModName = "@CF", GeneratedFiles = { @"db\ModTypes\Configured_types.xml" } } },
        };
        service.Capture(ServerPath, MapName, DataDirectory, mapping);

        // Simulate a loaded world replacing the live folder.
        fs.DeleteFile($@"{LiveTypes}\Configured_types.xml");
        fs.AddFile($@"{LiveTypes}\World_types.xml", "world");

        TypesBackupResult result = service.Restore(ServerPath, MapName, DataDirectory);

        Assert.True(result.Success);
        Assert.NotNull(result.Config);
        Assert.Equal("configured", fs.TryGetFileContents($@"{LiveTypes}\Configured_types.xml"));
        Assert.False(fs.FileExists($@"{LiveTypes}\World_types.xml"));
    }

    [Fact]
    public void Restore_WithoutBackup_FailsAndLeavesLiveTypesUntouched()
    {
        var fs = new FakeFileSystem();
        SeedLiveTypes(fs, "live_types.xml", "live-types");

        TypesBackupResult result = Create(fs).Restore(ServerPath, MapName, DataDirectory);

        Assert.False(result.Success);
        Assert.Equal("live-types", fs.TryGetFileContents($@"{LiveTypes}\live_types.xml"));
    }

    [Fact]
    public void EnsureCaptured_Recaptures_WhenBackupIsIncomplete()
    {
        var fs = new FakeFileSystem();
        SeedLiveTypes(fs, "CF_types.xml", "configured");
        // Simulate an interrupted Capture: the folder exists but has no files/mapping.
        fs.AddDirectory(Path.Combine(DataDirectory, "ModTypes_Backup", MapName));

        TypesBackupResult result = Create(fs).EnsureCaptured(ServerPath, MapName, DataDirectory, new MapTypesConfig());

        Assert.True(result.Success);
        string backup = Path.Combine(DataDirectory, "ModTypes_Backup", MapName);
        Assert.Equal("configured", fs.TryGetFileContents($@"{backup}\ModTypes\CF_types.xml"));
        Assert.NotNull(fs.TryGetFileContents($@"{backup}\config.json"));
    }

    [Fact]
    public void Restore_WithIncompleteBackup_FailsAndLeavesLiveTypesUntouched()
    {
        var fs = new FakeFileSystem();
        SeedLiveTypes(fs, "Configured_types.xml", "configured");
        TypesBackupService service = Create(fs);
        service.Capture(ServerPath, MapName, DataDirectory, new MapTypesConfig());

        // Remove the backed-up files folder, leaving only config.json.
        string backup = Path.Combine(DataDirectory, "ModTypes_Backup", MapName);
        fs.DeleteDirectory(Path.Combine(backup, "ModTypes"), recursive: true);

        // The live folder now holds a loaded world's files.
        fs.DeleteFile($@"{LiveTypes}\Configured_types.xml");
        fs.AddFile($@"{LiveTypes}\World_types.xml", "world");

        TypesBackupResult result = service.Restore(ServerPath, MapName, DataDirectory);

        Assert.False(result.Success);
        Assert.Equal("world", fs.TryGetFileContents($@"{LiveTypes}\World_types.xml"));
    }
}
