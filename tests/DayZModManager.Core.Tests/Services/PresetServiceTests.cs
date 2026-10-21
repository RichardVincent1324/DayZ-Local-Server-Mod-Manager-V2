using DayZModManager.Core;
using DayZModManager.Core.Models;
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
    public void EnsureDefaultPreset_MissingServerConfig_ReportsFailureWithoutCopying()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map); // id 1

        fs.DeleteFile(ConfigPath(PresetPaths.DefaultPresetName));
        Assert.False(service.PresetExists(DataDirectory, Map, PresetPaths.DefaultPresetName));

        PresetResult result = service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.False(result.Success);
        Assert.Contains("serverDZ.cfg", result.Message);
        // The config is never re-seeded from the server root; the user restores it.
        Assert.False(fs.FileExists(ConfigPath(PresetPaths.DefaultPresetName)));
        Assert.False(service.PresetExists(DataDirectory, Map, PresetPaths.DefaultPresetName));
    }

    [Fact]
    public void EnsureDefaultPreset_RepairsMissingMeta_PreservingInstanceId()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        int instanceId = service.ReadInstanceId(DataDirectory, Map, PresetPaths.DefaultPresetName);

        fs.DeleteFile(MetaPath(PresetPaths.DefaultPresetName));
        Assert.False(service.PresetExists(DataDirectory, Map, PresetPaths.DefaultPresetName));

        PresetResult result = service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.True(result.Success);
        Assert.True(fs.FileExists(MetaPath(PresetPaths.DefaultPresetName)));
        Assert.Equal(instanceId, service.ReadInstanceId(DataDirectory, Map, PresetPaths.DefaultPresetName));
        Assert.True(service.PresetExists(DataDirectory, Map, PresetPaths.DefaultPresetName));
    }

    [Fact]
    public void DescribeIncompletePreset_NamesTheMissingPart()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.Null(service.DescribeIncompletePreset(DataDirectory, Map, PresetPaths.DefaultPresetName));

        fs.DeleteFile(ConfigPath(PresetPaths.DefaultPresetName));
        Assert.Contains("serverDZ.cfg", service.DescribeIncompletePreset(DataDirectory, Map, PresetPaths.DefaultPresetName));

        fs.DeleteFile(MetaPath(PresetPaths.DefaultPresetName));
        Assert.Contains("preset-meta.json", service.DescribeIncompletePreset(DataDirectory, Map, PresetPaths.DefaultPresetName));

        Assert.Contains("preset folder", service.DescribeIncompletePreset(DataDirectory, Map, "Missing"));
    }

    [Fact]
    public void EnsureDefaultPreset_FailsWithoutServerConfig()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();

        PresetResult result = service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.False(result.Success);
        Assert.Contains("serverDZ.cfg", result.Message);
        // Nothing is fabricated: no preset folder or stub config is left behind.
        Assert.False(fs.DirectoryExists(PresetFolder(PresetPaths.DefaultPresetName)));
        Assert.Null(fs.TryGetFileContents(ConfigPath(PresetPaths.DefaultPresetName)));
    }

    [Fact]
    public void CreatePreset_FailsWithoutAnyServerConfig()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();

        PresetResult result = service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore");

        Assert.False(result.Success);
        Assert.Contains("serverDZ.cfg", result.Message);
        Assert.False(fs.DirectoryExists(PresetFolder("Hardcore")));
    }

    [Fact]
    public void AllocateInstanceId_ReturnsMaxPlusOne_AcrossAllMaps()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map); // id 1
        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore"); // id 2
        service.DuplicatePreset(
            ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName,
            "dayzOffline.sakhal", "Other"); // id 3

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
    public void EnsureDefaultPreset_RootIdUsedByPresetOnSameMap_UsesFreshIdAndWarns()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs, instanceId: 7);
        fs.AddDirectory(PresetFolder("Survivor"));
        fs.AddFile(MetaPath("Survivor"), "{\"instanceId\":7}");

        PresetResult result = service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.True(result.Success);
        int id = service.ReadInstanceId(DataDirectory, Map, PresetPaths.DefaultPresetName);
        Assert.NotEqual(7, id);
        Assert.Contains(result.Warnings, w => w.Contains("7") && w.Contains("Survivor"));
    }

    [Fact]
    public void EnsureDefaultPreset_RootIdUsedByPresetOnAnotherMap_UsesFreshIdAndWarns()
    {
        const string otherMap = "dayzOffline.sakhal";
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs, instanceId: 7);
        fs.AddDirectory(PresetPaths.PresetFolder(DataDirectory, otherMap, "Other"));
        fs.AddFile(PresetPaths.PresetMetaPath(DataDirectory, otherMap, "Other"), "{\"instanceId\":7}");

        PresetResult result = service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        Assert.True(result.Success);
        int id = service.ReadInstanceId(DataDirectory, Map, PresetPaths.DefaultPresetName);
        Assert.NotEqual(7, id);
        Assert.Contains(result.Warnings, w => w.Contains("7") && w.Contains("Other"));
    }

    [Fact]
    public void CreatePreset_FailsWhenDefaultConfigMissing_EvenIfRootExists()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);

        // The map's default preset (and therefore its config) does not exist.
        PresetResult result = service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore");

        Assert.False(result.Success);
        Assert.Contains("default preset", result.Message);
        Assert.Contains("serverDZ.cfg", result.Message);
        Assert.False(fs.DirectoryExists(PresetFolder("Hardcore")));
    }

    [Fact]
    public void DuplicatePreset_FailsWhenSourceConfigMissing_EvenIfDefaultAndRootExist()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        fs.DeleteFile(ConfigPath(PresetPaths.DefaultPresetName));

        PresetResult result = service.DuplicatePreset(
            ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "Copy");

        Assert.False(result.Success);
        Assert.Contains("serverDZ.cfg", result.Message);
        Assert.False(fs.DirectoryExists(PresetFolder("Copy")));
    }

    [Fact]
    public void CreatePreset_NeverTakesOverPreExistingStorage()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs, instanceId: 1);
        // Two live worlds the server created before this tool was used.
        fs.AddDirectory(Path.Combine(ServerPath, "mpmissions", Map), "storage_1", "storage_2");

        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map); // adopts 1
        service.CreatePreset(ServerPath, DataDirectory, Map, "Named");

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

        PresetResult result = service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore");

        Assert.True(result.Success);
        Assert.Equal(6, service.ReadInstanceId(DataDirectory, Map, "Hardcore"));
        string cfg = fs.TryGetFileContents(ConfigPath("Hardcore"))!;
        Assert.Contains("instanceId=6;", cfg);
        Assert.Contains($"template=\"{Map}\"", cfg);
    }

    [Fact]
    public void CreatePreset_NeverCopiesDefaultProfiles()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        string defaultProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName);
        fs.AddFile(Path.Combine(defaultProfiles, "player.db"), "data");

        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore");

        string targetProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, "Hardcore");
        Assert.True(fs.DirectoryExists(targetProfiles));
        Assert.False(fs.FileExists(Path.Combine(targetProfiles, "player.db")));
    }

    [Fact]
    public void CreatePreset_RejectsReservedName()
    {
        (PresetService service, _) = CreateService();

        PresetResult result = service.CreatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad name")]
    [InlineData("bad(name)")]
    [InlineData("bad&name")]
    [InlineData("name!")]
    [InlineData("@name")]
    public void CreatePreset_RejectsInvalidName(string name)
    {
        (PresetService service, _) = CreateService();

        Assert.False(service.CreatePreset(ServerPath, DataDirectory, Map, name).Success);
    }

    [Theory]
    [InlineData("Hardcore", true)]
    [InlineData("PvP-1", true)]
    [InlineData("a_b+c.d", true)]
    [InlineData("a b", false)]
    [InlineData("a(b)", false)]
    [InlineData("a&b", false)]
    [InlineData("", false)]
    public void IsValidPresetName_EnforcesWhitelist(string name, bool expected)
    {
        Assert.Equal(expected, PresetService.IsValidPresetName(name));
    }

    [Fact]
    public void CreatePreset_RejectsDuplicate()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        Assert.True(service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore").Success);

        Assert.False(service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore").Success);
    }

    [Fact]
    public void RenamePreset_MovesFolder()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore");

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
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Hardcore");

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
    public void ReadInstanceId_PrefersServerConfigOverMetadata()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        fs.AddFile(MetaPath("Drift"), "{\"instanceId\":1}");
        fs.AddFile(ConfigPath("Drift"), $"template=\"{Map}\";\ninstanceId=9;\n");

        // DayZ uses the config value, so the app must too.
        Assert.Equal(9, service.ReadInstanceId(DataDirectory, Map, "Drift"));
    }

    [Fact]
    public void ReadInstanceId_TreatsEmptyMetadataAsUnknown()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        fs.AddFile(MetaPath("EmptyMeta"), "{}");
        fs.AddFile(ConfigPath("EmptyMeta"), $"template=\"{Map}\";\ninstanceId=9;\n");

        Assert.Equal(9, service.ReadInstanceId(DataDirectory, Map, "EmptyMeta"));
    }

    [Fact]
    public void FindInstanceIdIssues_ReportsDuplicateIds()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        fs.AddDirectory(PresetFolder("A"));
        fs.AddFile(MetaPath("A"), "{\"instanceId\":7}");
        fs.AddFile(ConfigPath("A"), $"template=\"{Map}\";\ninstanceId=7;\n");
        fs.AddDirectory(PresetFolder("B"));
        fs.AddFile(MetaPath("B"), "{\"instanceId\":7}");
        fs.AddFile(ConfigPath("B"), $"template=\"{Map}\";\ninstanceId=7;\n");

        IReadOnlyList<string> issues = service.FindInstanceIdIssues(DataDirectory);

        Assert.Contains(issues, i => i.Contains("Instance ID 7") && i.Contains("\"A\"") && i.Contains("\"B\""));
    }

    [Fact]
    public void FindInstanceIdIssues_ReportsMetaConfigMismatch()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        fs.AddDirectory(PresetFolder("A"));
        fs.AddFile(MetaPath("A"), "{\"instanceId\":1}");
        fs.AddFile(ConfigPath("A"), $"template=\"{Map}\";\ninstanceId=9;\n");

        IReadOnlyList<string> issues = service.FindInstanceIdIssues(DataDirectory);

        Assert.Contains(issues, i => i.Contains("preset-meta.json") && i.Contains("9"));
    }

    [Fact]
    public void FindInstanceIdConflicts_ExcludesMetadataMismatch()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        fs.AddDirectory(PresetFolder("A"));
        fs.AddFile(MetaPath("A"), "{\"instanceId\":1}");
        fs.AddFile(ConfigPath("A"), $"template=\"{Map}\";\ninstanceId=9;\n");

        // The config is authoritative and the app will use ID 9, so a stale
        // metadata value is a warning, not a launch blocker.
        Assert.Empty(service.FindInstanceIdConflicts(DataDirectory));
        Assert.NotEmpty(service.FindInstanceIdIssues(DataDirectory));
    }

    [Fact]
    public void FindInstanceIdIssues_ReturnsEmpty_WhenIdsAreConsistentAndUnique()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        fs.AddDirectory(PresetFolder("A"));
        fs.AddFile(MetaPath("A"), "{\"instanceId\":1}");
        fs.AddFile(ConfigPath("A"), $"template=\"{Map}\";\ninstanceId=1;\n");
        fs.AddDirectory(PresetFolder("B"));
        fs.AddFile(MetaPath("B"), "{\"instanceId\":2}");
        fs.AddFile(ConfigPath("B"), $"template=\"{Map}\";\ninstanceId=2;\n");

        Assert.Empty(service.FindInstanceIdIssues(DataDirectory));
    }

    [Fact]
    public void ListPresetNames_ReturnsDefaultFirst_ThenSorted()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        service.CreatePreset(ServerPath, DataDirectory, Map, "Zulu");
        service.CreatePreset(ServerPath, DataDirectory, Map, "Alpha");

        IReadOnlyList<string> names = service.ListPresetNames(DataDirectory, Map);

        Assert.Equal(new[] { PresetPaths.DefaultPresetName, "Alpha", "Zulu" }, names);
    }

    [Fact]
    public void PresetExists_RequiresFolderMetadataAndServerConfig()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);

        // A tool-created preset is usable.
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        Assert.True(service.PresetExists(DataDirectory, Map, PresetPaths.DefaultPresetName));

        // A bare folder is not a preset.
        fs.AddDirectory(PresetFolder("Bare"));
        Assert.False(service.PresetExists(DataDirectory, Map, "Bare"));

        // Metadata alone is not enough.
        fs.AddDirectory(PresetFolder("MetaOnly"));
        fs.AddFile(MetaPath("MetaOnly"), "{}");
        Assert.False(service.PresetExists(DataDirectory, Map, "MetaOnly"));

        // Server config alone is not enough.
        fs.AddDirectory(PresetFolder("ConfigOnly"));
        fs.AddFile(ConfigPath("ConfigOnly"), $"template=\"{Map}\";\ninstanceId=9;\n");
        Assert.False(service.PresetExists(DataDirectory, Map, "ConfigOnly"));

        // Folder + metadata + config is usable.
        fs.AddFile(MetaPath("ConfigOnly"), "{}");
        Assert.True(service.PresetExists(DataDirectory, Map, "ConfigOnly"));
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
        fs.AddFile(PresetPaths.TypesConfigPath(DataDirectory, Map, PresetPaths.DefaultPresetName), "{\"mods\":[]}");

        PresetResult result = service.DuplicatePreset(
            ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "Copy");

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
    public void DuplicatePreset_NeverCopiesProfiles()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        string sourceProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName);
        fs.AddFile(Path.Combine(sourceProfiles, "player.db"), "data");

        service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "Copy");

        string targetProfiles = PresetPaths.ProfilesFolder(DataDirectory, Map, "Copy");
        Assert.True(fs.DirectoryExists(targetProfiles));
        Assert.False(fs.FileExists(Path.Combine(targetProfiles, "player.db")));
    }

    [Fact]
    public void DuplicatePreset_DoesNotCopySaves()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);
        string sourceSaves = PresetPaths.SavesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName);
        fs.AddFile(Path.Combine(sourceSaves, "Alpha", "save-meta.json"), "{}");

        service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "Copy");

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
        service.CreatePreset(ServerPath, DataDirectory, Map, "Existing");

        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "").Success);
        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, PresetPaths.DefaultPresetName).Success);
        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, "Missing", Map, "Copy").Success);
        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "Existing").Success);
        Assert.False(service.DuplicatePreset(ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "Copy Name").Success);
    }

    [Fact]
    public void DuplicatePreset_FailsWithoutAnyServerConfig()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map);

        // Remove both the default preset's config and the server-root config.
        fs.DeleteFile(ConfigPath(PresetPaths.DefaultPresetName));
        fs.DeleteFile(RootConfig);

        PresetResult result = service.DuplicatePreset(
            ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, Map, "Copy");

        Assert.False(result.Success);
        Assert.Contains("serverDZ.cfg", result.Message);
        Assert.False(fs.DirectoryExists(PresetFolder("Copy")));
    }

    [Fact]
    public void DuplicatePreset_ToAnotherMap_CopiesMapAgnosticTypesConfig()
    {
        const string otherMap = "dayzOffline.sakhal";
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);
        service.EnsureDefaultPreset(ServerPath, DataDirectory, Map); // map A default, id 1

        // Source preset content: the types file is map-agnostic.
        fs.AddFile(ConfigPath(PresetPaths.DefaultPresetName), $"template=\"{Map}\";\ninstanceId=1;\n");
        fs.AddFile(
            PresetPaths.TypesConfigPath(DataDirectory, Map, PresetPaths.DefaultPresetName),
            "{\"mods\":[{\"modName\":\"@CF\"}]}");
        fs.AddFile(
            Path.Combine(PresetPaths.TypeFilesFolder(DataDirectory, Map, PresetPaths.DefaultPresetName), "CF_types.xml"),
            "<types/>");
        fs.AddFile(PresetPaths.ModOrderPath(DataDirectory, Map, PresetPaths.DefaultPresetName), "[\"@CF\"]");

        PresetResult result = service.DuplicatePreset(
            ServerPath, DataDirectory, Map, PresetPaths.DefaultPresetName, otherMap, "Copy");

        Assert.True(result.Success);

        // Created under the destination map with a fresh instance ID.
        string targetFolder = PresetPaths.PresetFolder(DataDirectory, otherMap, "Copy");
        Assert.True(fs.DirectoryExists(targetFolder));
        int instanceId = service.ReadInstanceId(DataDirectory, otherMap, "Copy");
        Assert.True(instanceId > 0);
        Assert.NotEqual(1, instanceId);

        // serverDZ.cfg template points at the destination map and uses the new ID.
        string cfg = fs.TryGetFileContents(PresetPaths.ServerConfigPath(DataDirectory, otherMap, "Copy"))!;
        Assert.Contains($"template=\"{otherMap}\"", cfg);
        Assert.Contains($"instanceId={instanceId};", cfg);

        // The map-agnostic types config is copied as-is.
        string types = fs.TryGetFileContents(PresetPaths.TypesConfigPath(DataDirectory, otherMap, "Copy"))!;
        Assert.Contains("\"mods\"", types);
        Assert.Contains("\"@CF\"", types);
        Assert.DoesNotContain("\"currentMap\"", types);
        Assert.DoesNotContain("\"maps\"", types);

        // Map-agnostic content is copied; saves never are.
        Assert.True(fs.FileExists(
            Path.Combine(PresetPaths.TypeFilesFolder(DataDirectory, otherMap, "Copy"), "CF_types.xml")));
        Assert.True(fs.FileExists(PresetPaths.ModOrderPath(DataDirectory, otherMap, "Copy")));
        Assert.Empty(fs.GetDirectories(PresetPaths.SavesFolder(DataDirectory, otherMap, "Copy")));
    }

    [Fact]
    public void ResolveEnvironment_HealsDefaultPreset_AndReturnsEnvironment()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);

        EnvironmentResolution result = service.ResolveEnvironment(
            new Settings { ServerPath = ServerPath }, DataDirectory, Map, PresetPaths.DefaultPresetName);

        Assert.Null(result.Error);
        Assert.NotNull(result.Environment);
        Assert.Equal(PresetPaths.DefaultPresetName, result.Environment!.PresetName);
        Assert.Equal(PresetFolder(PresetPaths.DefaultPresetName), result.Environment.PresetFolder);
        Assert.Equal("1", result.Environment.ModListKey);
        Assert.True(fs.FileExists(ConfigPath(PresetPaths.DefaultPresetName)));
    }

    [Fact]
    public void ResolveEnvironment_Fails_ForIncompleteNamedPreset()
    {
        (PresetService service, FakeFileSystem fs) = CreateService();
        SeedRootConfig(fs);

        EnvironmentResolution result = service.ResolveEnvironment(
            new Settings { ServerPath = ServerPath }, DataDirectory, Map, "Missing");

        Assert.Null(result.Environment);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ResolveEnvironment_WithNoMap_ReturnsNullEnvironmentWithoutError()
    {
        (PresetService service, _) = CreateService();

        EnvironmentResolution result = service.ResolveEnvironment(
            new Settings(), DataDirectory, string.Empty, PresetPaths.DefaultPresetName);

        Assert.Null(result.Environment);
        Assert.Null(result.Error);
    }
}
