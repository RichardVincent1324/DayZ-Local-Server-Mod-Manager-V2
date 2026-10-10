using DayZModManager.Core;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class PresetServiceTests
{
    private const string ServerPath = @"D:\server";
    private const string DataDirectory = @"D:\data";
    private const string Map = "dayzOffline.chernarusplus";

    private static string RootConfig => Path.Combine(ServerPath, ConfigFileNames.ServerConfig);

    private static string PresetFolder(string preset) =>
        PresetPaths.PresetFolder(DataDirectory, Map, preset);

    private static string MetaPath(string preset) =>
        PresetPaths.PresetMetaPath(DataDirectory, Map, preset);

    private static string ConfigPath(string preset) =>
        PresetPaths.ServerConfigPath(DataDirectory, Map, preset);

    private static (PresetService Service, FakeFileSystem Fs) CreateService()
    {
        var fs = new FakeFileSystem();
        var service = new PresetService(fs, new ServerConfigService(fs));
        return (service, fs);
    }

    private static void SeedRootConfig(FakeFileSystem fs, string map = Map, int instanceId = 1)
    {
        fs.AddFile(RootConfig, $"template=\"{map}\";\ninstanceId={instanceId};\n");
    }

    [Fact]
    public void EnsureDefaultPreset_CreatesStructure_MetaAndConfig()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);

        PresetResult result = service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.True(result.Success);
        Assert.True(fs.DirectoryExists(PresetFolder(PresetPaths.DefaultPresetName)));
        Assert.True(fs.DirectoryExists(PresetPaths.TypeFilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName)));
        Assert.True(fs.DirectoryExists(PresetPaths.ProfilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName)));
        Assert.True(fs.DirectoryExists(PresetPaths.SavesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName)));

        Assert.Equal(1, service.ReadInstanceId(DataDirectory, Map, PresetPaths.DefaultPresetName));
        string cfg = fs.TryGetFileContents(ConfigPath(PresetPaths.DefaultPresetName))!;
        Assert.Contains($"template=\"{Map}\"", cfg);
        Assert.Contains("instanceId=1;", cfg);
    }

    [Fact]
    public void EnsureDefaultPreset_IsIdempotent()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);

        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        string? firstMeta = fs.TryGetFileContents(MetaPath(PresetPaths.DefaultPresetName));

        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.Equal(firstMeta, fs.TryGetFileContents(MetaPath(PresetPaths.DefaultPresetName)));
    }

    [Fact]
    public void EnsureDefaultPreset_WorksWithoutServerConfig()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();

        PresetResult result = service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.True(result.Success);
        Assert.Contains($"template=\"{Map}\"", fs.TryGetFileContents(ConfigPath(PresetPaths.DefaultPresetName))!);
        Assert.Contains("instanceId=1;", fs.TryGetFileContents(ConfigPath(PresetPaths.DefaultPresetName))!);
    }

    [Fact]
    public void AllocateInstanceId_ReturnsMaxPlusOne_AcrossAllMaps()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map); // id 1
        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", copyProfilesFromDefault: false); // id 2
        service.CreatePreset(ServerPath, DataDirectory, "dayzOffline.sakhal", "Other", copyProfilesFromDefault: false); // id 3

        Assert.Equal(4, service.AllocateInstanceId(DataDirectory, ServerPath, Map));
    }

    [Fact]
    public void EnsureDefaultPreset_AdoptsRootInstanceId_SoExistingWorldStaysAttached()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs, instanceId: 7);
        // The server already generated storage_7 before this tool was used.
        fs.AddDirectory(Path.Combine(ServerPath, "mpmissions", Map), "storage_7", "storage_9");

        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        // The default preset deliberately adopts 7 (the value in serverDZ.cfg)
        // even though storage_7 exists, so the user's existing world is used.
        Assert.Equal(7, service.ReadInstanceId(DataDirectory, Map, PresetPaths.DefaultPresetName));
        Assert.Contains("instanceId=7;", fs.TryGetFileContents(ConfigPath(PresetPaths.DefaultPresetName))!);
    }

    [Fact]
    public void CreatePreset_NeverTakesOverPreExistingStorage()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs, instanceId: 1);
        // Two live worlds the server created before this tool was used.
        fs.AddDirectory(Path.Combine(ServerPath, "mpmissions", Map), "storage_1", "storage_2");

        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map); // adopts 1
        service.CreatePreset(ServerPath, DataDirectory, Map, "Named", false);

        // The named preset must skip the live storage_1/storage_2 slots.
        Assert.Equal(3, service.ReadInstanceId(DataDirectory, Map, "Named"));
        Assert.Contains("instanceId=3;", fs.TryGetFileContents(ConfigPath("Named"))!);
    }

    [Fact]
    public void CreatePreset_AllocatesDedicatedInstanceId_AndSeedsFromDefault()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs, instanceId: 5);
        // The default preset adopts the server root's existing instance ID so it
        // stays attached to the world the server already created.
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        Assert.Equal(5, service.ReadInstanceId(DataDirectory, Map, PresetPaths.DefaultPresetName));

        PresetResult result = service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", false);

        Assert.True(result.Success);
        Assert.Equal(6, service.ReadInstanceId(DataDirectory, Map, "Hardcore"));
        string cfg = fs.TryGetFileContents(ConfigPath("Hardcore"))!;
        Assert.Contains("instanceId=6;", cfg);
        Assert.Contains($"template=\"{Map}\"", cfg);
    }

    [Fact]
    public void CreatePreset_CopyProfiles_CopiesDefaultProfiles()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        string defaultProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName);
        fs.AddDirectory(defaultProfiles);
        fs.AddFile(Path.Combine(defaultProfiles, "player.db"), "data");

        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", copyProfilesFromDefault: true);

        string targetProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, "Hardcore");
        Assert.True(fs.FileExists(Path.Combine(targetProfiles, "player.db")));
    }

    [Fact]
    public void CreatePreset_WithoutCopy_LeavesEmptyProfiles()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        fs.AddFile(
            Path.Combine(PresetPaths.ProfilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName), "player.db"),
            "data");

        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", copyProfilesFromDefault: false);

        string targetProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, "Hardcore");
        Assert.True(fs.DirectoryExists(targetProfiles));
        Assert.False(fs.FileExists(Path.Combine(targetProfiles, "player.db")));
    }

    [Fact]
    public void CreatePreset_RejectsReservedName()
    {
        (PresetService service, _) = CreateService();

        PresetResult result = service.CreatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, false);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    public void CreatePreset_RejectsInvalidName(string name)
    {
        (PresetService service, _) = CreateService();

        Assert.False(service.CreatePreset(ServerPath, DataDirectory, Map, name, false).Success);
    }

    [Fact]
    public void CreatePreset_RejectsDuplicate()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", false);

        Assert.False(service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", false).Success);
    }

    [Fact]
    public void RenamePreset_MovesFolder()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", false);

        PresetResult result = service.RenamePreset(DataDirectory, Map, "Hardcore", "Apocalypse");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(PresetFolder("Hardcore")));
        Assert.True(fs.DirectoryExists(PresetFolder("Apocalypse")));
    }

    [Fact]
    public void RenamePreset_RefusesDefault()
    {
        (PresetService service, _) = CreateService();

        Assert.False(service.RenamePreset(DataDirectory, Map, PresetPaths.DefaultPresetName, "New").Success);
    }

    [Fact]
    public void DeletePreset_RefusesDefault()
    {
        (PresetService service, _) = CreateService();

        Assert.False(service.DeletePreset(DataDirectory, Map, PresetPaths.DefaultPresetName).Success);
    }

    [Fact]
    public void DeletePreset_RemovesFolder()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore", false);

        PresetResult result = service.DeletePreset(DataDirectory, Map, "Hardcore");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(PresetFolder("Hardcore")));
    }

    [Fact]
    public void ReadInstanceId_ReturnsZero_WhenMetaAndConfigMissing()
    {
        (PresetService service, _) = CreateService();

        // Never invent an ID: an unknown preset must not claim storage_1.
        Assert.Equal(0, service.ReadInstanceId(DataDirectory, Map, "Nonexistent"));
    }

    [Fact]
    public void ReadInstanceId_FallsBackToPresetServerConfig_WhenMetaMissing()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        fs.AddFile(ConfigPath("Broken"), $"template=\"{Map}\";\ninstanceId=9;\n");

        Assert.Equal(9, service.ReadInstanceId(DataDirectory, Map, "Broken"));
    }

    [Fact]
    public void ListPresetNames_ReturnsDefaultFirst_ThenSorted()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Zulu", false);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Alpha", false);

        IReadOnlyList<string> names = service.ListPresetNames(DataDirectory, Map);

        Assert.Equal(new[] { PresetPaths.DefaultPresetName, "Alpha", "Zulu" }, names);
    }

    [Fact]
    public void DuplicatePreset_CopiesConfigAndTypeFiles_WithNewInstanceId()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map); // id 1

        string sourceTypeFiles = PresetPaths.TypeFilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName);
        fs.AddFile(Path.Combine(sourceTypeFiles, "CF_types.xml"), "<types/>");
        fs.AddFile(PresetPaths.ModOrderPath(DataDirectory, Map, PresetPaths.DefaultPresetName), "[\"@CF\"]");
        fs.AddFile(PresetPaths.TypesConfigPath(DataDirectory, Map, PresetPaths.DefaultPresetName), "{\"currentMap\":\"x\"}");

        PresetResult result = service.DuplicatePreset(
            ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, "Copy", copyProfiles: false);

        Assert.True(result.Success);
        Assert.Equal(2, service.ReadInstanceId(DataDirectory, Map, "Copy"));
        string cfg = fs.TryGetFileContents(ConfigPath("Copy"))!;
        Assert.Contains("instanceId=2;", cfg);
        Assert.Contains($"template=\"{Map}\"", cfg);
        Assert.True(fs.FileExists(Path.Combine(PresetPaths.TypeFilesFolder(DataDirectory, Map, "Copy"), "CF_types.xml")));
        Assert.True(fs.FileExists(PresetPaths.ModOrderPath(DataDirectory, Map, "Copy")));
        Assert.True(fs.FileExists(PresetPaths.TypesConfigPath(DataDirectory, Map, "Copy")));
    }

    [Fact]
    public void DuplicatePreset_CopiesProfiles_OnlyWhenRequested()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        string sourceProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName);
        fs.AddFile(Path.Combine(sourceProfiles, "player.db"), "data");

        service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, "WithProfiles", copyProfiles: true);
        service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, "WithoutProfiles", copyProfiles: false);

        Assert.True(fs.FileExists(Path.Combine(PresetPaths.ProfilesFolder(DataDirectory, Map, "WithProfiles"), "player.db")));
        Assert.False(fs.FileExists(Path.Combine(PresetPaths.ProfilesFolder(DataDirectory, Map, "WithoutProfiles"), "player.db")));
    }

    [Fact]
    public void DuplicatePreset_DoesNotCopySaves()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        string sourceSaves = PresetPaths.SavesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName);
        fs.AddFile(Path.Combine(sourceSaves, "Alpha", "save-meta.json"), "{}");

        service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, "Copy", copyProfiles: false);

        string targetSaves = PresetPaths.SavesFolder(DataDirectory, Map, "Copy");
        Assert.True(fs.DirectoryExists(targetSaves));
        Assert.False(fs.FileExists(Path.Combine(targetSaves, "Alpha", "save-meta.json")));
    }

    [Fact]
    public void DuplicatePreset_RejectsBadRequests()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Existing", false);

        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, "", false).Success);
        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, PresetPaths.DefaultPresetName, false).Success);
        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, "Missing", "Copy", false).Success);
        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, "Existing", false).Success);
    }
}
