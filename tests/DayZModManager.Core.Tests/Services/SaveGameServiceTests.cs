using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class SaveGameServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string MissionPath = @"D:\DayZServer\mpmissions\dayzOffline.chernarusplus";
    private const string MapName = "dayzOffline.chernarusplus";
    private const string DataDirectory = @"D:\app\data";

    private static string ConfigWithInstance(int id) =>
        $"template=\"{MapName}\"\ninstanceId = {id};\n";

    private static SaveGameService CreateService(FakeFileSystem fs, IDayZServerProcessState processState) =>
        new(fs, processState);

    private static SaveGameService CreateService(FakeFileSystem fs) =>
        CreateService(fs, new FakeServerProcessState());

    private static SaveGameService CreateRunningService(FakeFileSystem fs) =>
        CreateService(fs, new FakeServerProcessState { Running = true });

    private sealed class FakeServerProcessState : IDayZServerProcessState
    {
        public bool Running { get; set; }

        public bool IsDayZServerRunning() => Running;
    }

    private static void SeedLiveStorage(FakeFileSystem fs, string folderName = "storage_1")
    {
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));
        fs.AddDirectory($@"{MissionPath}", folderName);
        fs.AddFile($@"{MissionPath}\{folderName}\players.db", "live-data");
    }

    private static void SeedLiveModTypes(FakeFileSystem fs, string fileName = "CF_types.xml", string contents = "types-data")
    {
        fs.AddDirectory($@"{MissionPath}\db", "ModTypes");
        fs.AddFile($@"{MissionPath}\db\ModTypes\{fileName}", contents);
    }

    private static void SeedStoredSave(FakeFileSystem fs, string saveName, string contents = "saved-data")
    {
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, saveName);
        fs.AddDirectory($@"{library}\{saveName}", "storage_1");
        fs.AddFile($@"{library}\{saveName}\storage_1\players.db", contents);
        fs.AddFile($@"{library}\{saveName}\meta.json", MetaJsonStorage1);
    }

    /// <summary>Minimal meta.json for a nested-format save of storage_1.</summary>
    private const string MetaJsonStorage1 =
        "{\"map\":\"dayzOffline.chernarusplus\",\"storageFolder\":\"storage_1\",\"savedAtUtc\":\"2026-01-01T00:00:00Z\",\"modList\":[\"@CF\"],\"typesFiles\":[]}";

    private static void SeedNestedStoredSave(FakeFileSystem fs, string saveName, string contents = "saved-data", bool withMeta = true)
    {
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, saveName);
        fs.AddDirectory($@"{library}\{saveName}", "storage_1");
        fs.AddFile($@"{library}\{saveName}\storage_1\players.db", contents);
        if (withMeta)
        {
            fs.AddFile($@"{library}\{saveName}\meta.json", MetaJsonStorage1);
        }
    }

    private static string MetaJsonSavedAt(string savedAtUtc) =>
        $"{{\"map\":\"dayzOffline.chernarusplus\",\"storageFolder\":\"storage_1\",\"savedAtUtc\":\"{savedAtUtc}\",\"modList\":[\"@CF\"],\"typesFiles\":[]}}";

    private static void SeedStoredSaveWithMeta(FakeFileSystem fs, string saveName, string metaJson, string contents = "saved-data")
    {
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, saveName);
        fs.AddDirectory($@"{library}\{saveName}", "storage_1");
        fs.AddFile($@"{library}\{saveName}\storage_1\players.db", contents);
        fs.AddFile($@"{library}\{saveName}\meta.json", metaJson);
    }

    private static string LibraryPath(string saveName) =>
        $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}\{saveName}";

    [Fact]
    public void ReadInstanceId_ParsesValueFromServerDz()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(4));

        int id = CreateService(fs).ReadInstanceId(ServerPath);

        Assert.Equal(4, id);
    }

    [Fact]
    public void ReadInstanceId_DefaultsToOne_WhenAbsent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", "template=\"x\"\n");

        Assert.Equal(1, CreateService(fs).ReadInstanceId(ServerPath));
    }

    [Fact]
    public void ReadInstanceId_DefaultsToOne_WhenFileMissing()
    {
        Assert.Equal(1, CreateService(new FakeFileSystem()).ReadInstanceId(ServerPath));
    }

    [Fact]
    public void GetStorageFolderPath_IncludesInstanceId()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(3));

        string path = CreateService(fs).GetStorageFolderPath(ServerPath, MapName);

        Assert.Equal($@"{MissionPath}\storage_3", path);
    }

    [Fact]
    public void AddSave_WritesNestedStorage_AndMetaJson()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        var meta = new SaveMetaData
        {
            Map = MapName,
            SavedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModList = new List<string> { "@CF" },
            TypesFiles = new List<string> { "CF_types.xml" },
        };

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: meta);

        Assert.True(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
        Assert.Null(fs.TryGetFileContents($@"{LibraryPath("Alpha")}\players.db"));
        string? json = fs.TryGetFileContents($@"{LibraryPath("Alpha")}\meta.json");
        Assert.NotNull(json);
        Assert.Contains("\"modList\"", json);
        Assert.Contains("@CF", json);
        Assert.Contains("storage_1", json);
    }

    [Fact]
    public void AddSave_Message_ReportsSnapshotCounts()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        var meta = new SaveMetaData
        {
            ModList = new List<string> { "@CF", "@Extra" },
            TypesFiles = new List<string> { "CF_types.xml" },
        };

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: meta);

        Assert.True(result.Success);
        Assert.Contains("2 mod(s), 1 type file(s)", result.Message);
    }

    [Fact]
    public void AddSave_CopiesLiveModTypesIntoLibrary_WhenPresent()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedLiveModTypes(fs);
        var meta = new SaveMetaData
        {
            Map = MapName,
            StorageFolder = "storage_1",
            ModList = new List<string> { "@CF" },
            TypesFiles = new List<string> { "CF_types.xml" },
        };

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: meta);

        Assert.True(result.Success);
        Assert.Equal("types-data", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\ModTypes\CF_types.xml"));
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
        Assert.NotNull(fs.TryGetFileContents($@"{LibraryPath("Alpha")}\meta.json"));
    }

    [Fact]
    public void AddSave_SkipsTypesSnapshot_WhenLiveModTypesAbsent()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: new SaveMetaData());

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists($@"{LibraryPath("Alpha")}\ModTypes"));
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
    }

    [Fact]
    public void AddSave_ModTypesCopyFailure_FailsAndPreservesExistingSave()
    {
        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedLiveModTypes(inner);
        SeedStoredSave(inner, "Alpha", "old-world");
        var fs = new FaultyFileSystem(inner)
        {
            CopyDirectoryThrowsWhen = path => path.Equals($@"{MissionPath}\db\ModTypes", StringComparison.OrdinalIgnoreCase),
        };

        SaveGameResult result = new SaveGameService(fs, new FakeServerProcessState())
            .AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: true, meta: new SaveMetaData());

        Assert.False(result.Success);
        Assert.Equal("old-world", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
        Assert.Empty(fs.GetDirectories($@"{DataDirectory}\{SaveGameService.SavesRootName}").Where(n => n.StartsWith(".save_", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LoadSave_DoesNotRestoreModTypesSnapshot_IntoLiveMission()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedLiveModTypes(fs, fileName: "live_types.xml", contents: "live-types");

        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, "Alpha");
        fs.AddDirectory($@"{library}\Alpha", "storage_1", "ModTypes");
        fs.AddFile($@"{library}\Alpha\storage_1\players.db", "new-world");
        fs.AddFile($@"{library}\Alpha\ModTypes\stored_types.xml", "stored-types");
        fs.AddFile($@"{library}\Alpha\meta.json", MetaJsonStorage1);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.Equal("live-types", fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\live_types.xml"));
        Assert.False(fs.FileExists($@"{MissionPath}\db\ModTypes\stored_types.xml"));
        Assert.Equal("stored-types", fs.TryGetFileContents($@"{library}\Alpha\ModTypes\stored_types.xml"));
    }

    [Fact]
    public void LoadSave_Fails_WhenStorageMissing_ButModTypesSnapshotPresent()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedLiveModTypes(fs, fileName: "live_types.xml", contents: "live-types");

        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, "Alpha");
        fs.AddDirectory($@"{library}\Alpha", "ModTypes"); // storage_1 intentionally absent
        fs.AddFile($@"{library}\Alpha\ModTypes\stored_types.xml", "<types/>");
        fs.AddFile($@"{library}\Alpha\meta.json", MetaJsonStorage1); // references storage_1

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        // The ModTypes snapshot must never be mistaken for the world data: the
        // load fails cleanly and the live world stays untouched.
        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.Equal("live-types", fs.TryGetFileContents($@"{MissionPath}\db\ModTypes\live_types.xml"));
        Assert.False(fs.FileExists($@"{MissionPath}\storage_1\stored_types.xml"));
    }

    [Fact]
    public void GetModTypesSnapshotPath_ReturnsSnapshotFolderInsideSave()
    {
        string path = CreateService(new FakeFileSystem()).GetModTypesSnapshotPath(DataDirectory, MapName, "Alpha");

        Assert.Equal($@"{LibraryPath("Alpha")}\ModTypes", path);
    }

    private static void SeedStoredModTypes(
        FakeFileSystem fs, string saveName, string fileName = "CF_types.xml", string contents = "snapshot-types")
    {
        fs.AddDirectory(LibraryPath(saveName), "ModTypes");
        fs.AddFile($@"{LibraryPath(saveName)}\ModTypes\{fileName}", contents);
    }

    private static string LiveModTypesPath() => $@"{MissionPath}\db\ModTypes";

    [Fact]
    public void RestoreModTypes_MirrorsSnapshot_IntoLiveMission()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedLiveModTypes(fs, fileName: "live_types.xml", contents: "live-types");
        fs.AddFile($@"{LiveModTypesPath()}\stale.xml", "<types/>");
        SeedStoredSave(fs, "Alpha");
        SeedStoredModTypes(fs, "Alpha");

        SaveGameResult result = CreateService(fs).RestoreModTypes(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("snapshot-types", fs.TryGetFileContents($@"{LiveModTypesPath()}\CF_types.xml"));
        Assert.Null(fs.TryGetFileContents($@"{LiveModTypesPath()}\live_types.xml"));
        Assert.Null(fs.TryGetFileContents($@"{LiveModTypesPath()}\stale.xml"));
        // The stored snapshot must be left untouched.
        Assert.Equal("snapshot-types", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\ModTypes\CF_types.xml"));
        Assert.Contains("1 type file(s)", result.Message);
    }

    [Fact]
    public void RestoreModTypes_CreatesModTypesFolder_WhenLiveAbsent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));
        fs.AddDirectory(MissionPath);
        SeedStoredSave(fs, "Alpha");
        SeedStoredModTypes(fs, "Alpha");

        SaveGameResult result = CreateService(fs).RestoreModTypes(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("snapshot-types", fs.TryGetFileContents($@"{LiveModTypesPath()}\CF_types.xml"));
    }

    [Fact]
    public void RestoreModTypes_ReturnsFailure_WhenSnapshotMissing()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedLiveModTypes(fs, fileName: "live_types.xml", contents: "live-types");
        SeedStoredSave(fs, "Alpha"); // no ModTypes snapshot

        SaveGameResult result = CreateService(fs).RestoreModTypes(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Contains("no snapshot", result.Message);
        Assert.Equal("live-types", fs.TryGetFileContents($@"{LiveModTypesPath()}\live_types.xml"));
    }

    [Fact]
    public void RestoreModTypes_ReturnsFailure_WhenSaveMissing()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).RestoreModTypes(ServerPath, MapName, DataDirectory, "Ghost");

        Assert.False(result.Success);
    }

    [Fact]
    public void RestoreModTypes_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedLiveModTypes(fs, fileName: "live_types.xml", contents: "live-types");
        SeedStoredSave(fs, "Alpha");
        SeedStoredModTypes(fs, "Alpha");

        SaveGameResult result = CreateRunningService(fs).RestoreModTypes(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.Equal("live-types", fs.TryGetFileContents($@"{LiveModTypesPath()}\live_types.xml"));
    }

    [Fact]
    public void RestoreModTypes_PromotionFailure_RollsBack_AndCleansStaging()
    {
        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedLiveModTypes(inner, fileName: "live_types.xml", contents: "live-types");
        SeedStoredSave(inner, "Alpha");
        SeedStoredModTypes(inner, "Alpha");
        var fs = new FaultyFileSystem(inner)
        {
            MoveThrowsWhen = path => path.Contains(".restore", StringComparison.OrdinalIgnoreCase),
        };

        SaveGameResult result = new SaveGameService(fs, new FakeServerProcessState())
            .RestoreModTypes(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Equal("live-types", fs.TryGetFileContents($@"{LiveModTypesPath()}\live_types.xml"));
        Assert.False(fs.FileExists($@"{LiveModTypesPath()}\CF_types.xml"));
        Assert.Empty(fs.GetDirectories($@"{MissionPath}\db").Where(n => n.StartsWith("ModTypes.restore", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void RestoreModTypes_Success_LeavesNoBackupOrStaging()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedLiveModTypes(fs, fileName: "live_types.xml", contents: "live-types");
        SeedStoredSave(fs, "Alpha");
        SeedStoredModTypes(fs, "Alpha");

        SaveGameResult result = CreateService(fs).RestoreModTypes(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists($@"{LiveModTypesPath()}.old"));
        Assert.Empty(fs.GetDirectories($@"{MissionPath}\db").Where(n => n.StartsWith("ModTypes.restore", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void AddSave_GetMeta_RoundTripsTypesConfig()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        var meta = new SaveMetaData
        {
            Map = MapName,
            ModList = new List<string> { "@CF" },
            Types = new MapTypesConfig
            {
                Mods =
                {
                    new ModTypesEntry
                    {
                        ModName = "@CF",
                        GeneratedFiles = new List<string> { @"db\ModTypes\CF_types.xml" },
                    },
                },
            },
        };
        SaveGameService service = CreateService(fs);

        SaveGameResult saved = service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: meta);
        ConfigLoadResult<SaveMetaData> loaded = service.GetMeta(DataDirectory, MapName, "Alpha");

        Assert.True(saved.Success);
        Assert.Equal(ConfigLoadStatus.Success, loaded.Status);
        Assert.NotNull(loaded.Value!.Types);
        Assert.Equal("@CF", loaded.Value.Types!.Mods.Single().ModName);
    }

    [Fact]
    public void GetMeta_ReturnsMissing_WhenSaveHasNoMeta()
    {
        var fs = new FakeFileSystem();
        SeedNestedStoredSave(fs, "Alpha", withMeta: false);

        ConfigLoadResult<SaveMetaData> result = CreateService(fs).GetMeta(DataDirectory, MapName, "Alpha");

        Assert.Equal(ConfigLoadStatus.Missing, result.Status);
    }

    [Fact]
    public void GetMeta_ReturnsSnapshot_WhenMetaPresent()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        var meta = new SaveMetaData
        {
            Map = MapName,
            StorageFolder = "storage_1",
            ModList = new List<string> { "@CF" },
            TypesFiles = new List<string> { "CF_types.xml" },
        };
        SaveGameService service = CreateService(fs);
        service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: meta);

        ConfigLoadResult<SaveMetaData> result = service.GetMeta(DataDirectory, MapName, "Alpha");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal(MapName, result.Value!.Map);
        Assert.Equal("storage_1", result.Value.StorageFolder);
        Assert.Equal(new[] { "@CF" }, result.Value.ModList);
        Assert.Equal(new[] { "CF_types.xml" }, result.Value.TypesFiles);
    }

    [Fact]
    public void UpdateMeta_RewritesMetaJson()
    {
        var fs = new FakeFileSystem();
        SeedStoredSave(fs, "Alpha");
        SaveGameService service = CreateService(fs);

        ConfigLoadResult<SaveMetaData> loaded = service.GetMeta(DataDirectory, MapName, "Alpha");
        SaveMetaData meta = loaded.Value!;
        meta.ModList = new List<string> { "@CF", "@Extra" };

        SaveGameResult result = service.UpdateMeta(DataDirectory, MapName, "Alpha", meta);

        Assert.True(result.Success);
        ConfigLoadResult<SaveMetaData> reread = service.GetMeta(DataDirectory, MapName, "Alpha");
        Assert.Equal(new[] { "@CF", "@Extra" }, reread.Value!.ModList);
    }

    [Fact]
    public void UpdateMeta_ReturnsFailure_WhenSaveMissing()
    {
        var service = CreateService(new FakeFileSystem());

        SaveGameResult result = service.UpdateMeta(DataDirectory, MapName, "Ghost", new SaveMetaData());

        Assert.False(result.Success);
    }

    [Fact]
    public void AddSave_ReturnsFailure_WhenLiveStorageMissing()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: new SaveMetaData());

        Assert.False(result.Success);
    }

    [Fact]
    public void AddSave_RejectsDuplicate_UnlessOverwrite()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedStoredSave(fs, "Alpha");
        SaveGameService service = CreateService(fs);

        Assert.False(service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: new SaveMetaData()).Success);
        Assert.True(service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: true, meta: new SaveMetaData()).Success);
    }

    [Fact]
    public void AddSave_RejectsInvalidName()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "bad\\name", overwrite: false, meta: new SaveMetaData());

        Assert.False(result.Success);
    }

    [Fact]
    public void ListSaves_ReturnsStoredFolders()
    {
        var fs = new FakeFileSystem();
        SeedStoredSave(fs, "Alpha");
        SeedStoredSave(fs, "Beta");

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(DataDirectory, MapName);

        Assert.Equal(new[] { "Alpha", "Beta" }, saves);
    }

    [Fact]
    public void ListSaves_IgnoresPromotionBackupFolders()
    {
        var fs = new FakeFileSystem();
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, "Alpha", "Alpha.old", "Beta");
        fs.AddFile($@"{library}\Alpha\players.db", "a");
        fs.AddFile($@"{library}\Alpha.old\players.db", "old");
        fs.AddFile($@"{library}\Beta\players.db", "b");

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(DataDirectory, MapName);

        Assert.Equal(new[] { "Alpha", "Beta" }, saves);
    }

    [Fact]
    public void ListSaves_OrdersBySavedAtUtc_OldestFirst()
    {
        var fs = new FakeFileSystem();
        SeedStoredSaveWithMeta(fs, "Alpha", MetaJsonSavedAt("2026-01-01T00:00:00Z"));
        SeedStoredSaveWithMeta(fs, "Gamma", MetaJsonSavedAt("2026-01-03T00:00:00Z"));
        SeedStoredSaveWithMeta(fs, "Beta", MetaJsonSavedAt("2026-01-02T00:00:00Z"));

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(DataDirectory, MapName);

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, saves);
    }

    [Fact]
    public void ListSaves_PlacesSavesWithoutMeta_AfterTimestampedOnes()
    {
        var fs = new FakeFileSystem();
        SeedStoredSaveWithMeta(fs, "Zulu", MetaJsonSavedAt("2026-01-02T00:00:00Z"));
        SeedNestedStoredSave(fs, "Alpha", withMeta: false);
        SeedNestedStoredSave(fs, "Beta", withMeta: false);

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(DataDirectory, MapName);

        Assert.Equal(new[] { "Zulu", "Alpha", "Beta" }, saves);
    }

    [Fact]
    public void ListSaves_TreatsCorruptMeta_AsSaveWithoutTimestamp()
    {
        var fs = new FakeFileSystem();
        SeedStoredSaveWithMeta(fs, "Zulu", MetaJsonSavedAt("2026-01-02T00:00:00Z"));
        SeedStoredSaveWithMeta(fs, "Gamma", "{not-json");

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(DataDirectory, MapName);

        Assert.Equal(new[] { "Zulu", "Gamma" }, saves);
    }

    [Fact]
    public void LoadSave_RecoversInterruptedPromotion_FromDotOldBackup()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        // Interrupted AddSave overwrite: only the previous copy remains as .old.
        fs.AddDirectory(library, "Alpha.old");
        fs.AddDirectory($@"{library}\Alpha.old", "storage_1");
        fs.AddFile($@"{library}\Alpha.old\storage_1\players.db", "old-world");
        fs.AddFile($@"{library}\Alpha.old\meta.json", MetaJsonStorage1);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("old-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.True(fs.DirectoryExists($@"{library}\Alpha"));
        Assert.False(fs.DirectoryExists($@"{library}\Alpha.old"));
    }

    [Fact]
    public void GetSaveFolderPath_ReturnsLibraryPathForSave()
    {
        string path = CreateService(new FakeFileSystem()).GetSaveFolderPath(DataDirectory, MapName, "Alpha");

        Assert.Equal($@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}\Alpha", path);
    }

    [Fact]
    public void LoadSave_ReplacesLiveStorage_AndRemovesStaging()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedStoredSave(fs, "Alpha", "new-world");
        SaveGameService service = CreateService(fs);

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.old"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.restore"));
    }

    [Fact]
    public void LoadSave_CreatesLiveStorage_WhenAbsent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));
        fs.AddDirectory(MissionPath);
        SeedStoredSave(fs, "Alpha", "new-world");

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_ReturnsFailure_WhenSaveMissing()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Ghost");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_NestedFormat_RestoresNestedStorageFolder()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedNestedStoredSave(fs, "Alpha", "new-world");

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_Fails_WhenMetaMissing_EvenWithStorageFolder()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedNestedStoredSave(fs, "Alpha", "new-world", withMeta: false);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        // A stored save must carry meta.json to be loadable; without it the world
        // data cannot be located and the live progress is left untouched.
        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void NewGame_DeletesLiveStorage()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).NewGame(ServerPath, MapName);

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1"));
    }

    [Fact]
    public void NewGame_NoOp_WhenNoLiveStorage()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));

        SaveGameResult result = CreateService(fs).NewGame(ServerPath, MapName);

        Assert.True(result.Success);
    }

    [Fact]
    public void AddSave_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateRunningService(fs).AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: new SaveMetaData());

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.False(fs.DirectoryExists($@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}\Alpha"));
    }

    [Fact]
    public void LoadSave_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedStoredSave(fs, "Alpha", "new-world");

        SaveGameResult result = CreateRunningService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void NewGame_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateRunningService(fs).NewGame(ServerPath, MapName);

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.True(fs.DirectoryExists($@"{MissionPath}\storage_1"));
    }

    [Fact]
    public void DeleteSave_RemovesStoredSave()
    {
        var fs = new FakeFileSystem();
        SeedStoredSave(fs, "Alpha");
        SaveGameService service = CreateService(fs);

        SaveGameResult result = service.DeleteSave(DataDirectory, MapName, "Alpha");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(LibraryPath("Alpha")));
    }

    [Fact]
    public void DeleteSave_ReturnsFailure_WhenSaveMissing()
    {
        var fs = new FakeFileSystem();

        SaveGameResult result = CreateService(fs).DeleteSave(DataDirectory, MapName, "Ghost");

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("trailing.")]
    [InlineData("bad\\name")]
    public void AddSave_RejectsUnsafeNames(string saveName)
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        fs.AddDirectory($@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}");

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, saveName, overwrite: true, meta: new SaveMetaData());

        Assert.False(result.Success);
    }

    [Fact]
    public void DeleteSave_RejectsDotDot_AndDoesNotTouchLibrary()
    {
        var fs = new FakeFileSystem();
        SeedStoredSave(fs, "Alpha");

        SaveGameResult result = CreateService(fs).DeleteSave(DataDirectory, MapName, "..");

        Assert.False(result.Success);
        Assert.True(fs.DirectoryExists($@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}"));
        Assert.True(fs.DirectoryExists(LibraryPath("Alpha")));
    }

    [Fact]
    public void LoadSave_ReportsSuccess_WhenBackupCleanupFails()
    {
        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedStoredSave(inner, "Alpha", "new-world");
        var fs = new FaultyFileSystem(inner) { DeleteThrowsWhen = path => path.EndsWith(".old", StringComparison.OrdinalIgnoreCase) };
        SaveGameService service = new(fs, new FakeServerProcessState());

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_CleansStagingFolder_AndPreservesLive_WhenPromotionFails()
    {        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedStoredSave(inner, "Alpha", "new-world");
        var fs = new FaultyFileSystem(inner) { MoveThrowsWhen = path => path.Contains(".restore", StringComparison.OrdinalIgnoreCase) };
        SaveGameService service = new(fs, new FakeServerProcessState());

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.restore"));
    }

    [Fact]
    public void LoadSave_Recovers_WhenInterruptedRunLeftOnlyBackup()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));
        fs.AddDirectory($@"{MissionPath}", "storage_1.old"); // interrupted run: live missing, .old holds the world
        fs.AddFile($@"{MissionPath}\storage_1.old\players.db", "old-world");
        SeedStoredSave(fs, "Alpha", "new-world");
        SaveGameService service = new(fs, new FakeServerProcessState());

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.old"));
    }

    [Fact]
    public void AddSave_Overwrite_CopyFailure_PreservesExistingSave()
    {
        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedStoredSave(inner, "Alpha", "old-world");
        var fs = new FaultyFileSystem(inner)
        {
            CopyDirectoryThrowsWhen = path => path.Equals($@"{MissionPath}\storage_1", StringComparison.OrdinalIgnoreCase),
        };

        SaveGameResult result = new SaveGameService(fs, new FakeServerProcessState())
            .AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: true, meta: new SaveMetaData());

        Assert.False(result.Success);
        Assert.Equal("old-world", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
        Assert.Empty(fs.GetDirectories($@"{DataDirectory}\{SaveGameService.SavesRootName}").Where(n => n.StartsWith(".save_", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void AddSave_MetaWriteFailure_PreservesExistingSave()
    {
        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedStoredSave(inner, "Alpha", "old-world");
        var fs = new FaultyFileSystem(inner)
        {
            WriteAllTextThrowsWhen = path => path.EndsWith("meta.json", StringComparison.OrdinalIgnoreCase),
        };

        SaveGameResult result = new SaveGameService(fs, new FakeServerProcessState())
            .AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: true, meta: new SaveMetaData());

        Assert.False(result.Success);
        Assert.Equal("old-world", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
        Assert.Empty(fs.GetDirectories($@"{DataDirectory}\{SaveGameService.SavesRootName}").Where(n => n.StartsWith(".save_", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void AddSave_Success_LeavesNoStagingOrBackupBehind()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SaveGameService service = CreateService(fs);

        SaveGameResult first = service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false, meta: new SaveMetaData());
        SaveGameResult second = service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: true, meta: new SaveMetaData());

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{LibraryPath("Alpha")}.old"));
        Assert.Empty(fs.GetDirectories($@"{DataDirectory}\{SaveGameService.SavesRootName}").Where(n => n.StartsWith(".save_", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void AddSave_PromotionFailure_RollsBackPreviousSave()
    {
        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedStoredSave(inner, "Alpha", "old-world");
        var fs = new FaultyFileSystem(inner)
        {
            MoveThrowsWhen = path => path.Contains(".save_", StringComparison.OrdinalIgnoreCase),
        };

        SaveGameResult result = new SaveGameService(fs, new FakeServerProcessState())
            .AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: true, meta: new SaveMetaData());

        Assert.False(result.Success);
        Assert.Equal("old-world", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\storage_1\players.db"));
        Assert.Empty(fs.GetDirectories($@"{DataDirectory}\{SaveGameService.SavesRootName}").Where(n => n.StartsWith(".save_", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LoadSave_Fails_WhenMetaPresent_ButNoUsableStorage()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, "Alpha");
        fs.AddFile($@"{library}\Alpha\meta.json", MetaJsonStorage1);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_Fails_WhenMetaStorageFolderMissing_EvenWithOtherChild()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, "Alpha");
        fs.AddDirectory($@"{library}\Alpha", "actual");
        fs.AddFile($@"{library}\Alpha\actual\players.db", "new-world");
        fs.AddFile($@"{library}\Alpha\meta.json", MetaJsonStorage1); // references storage_1, which is absent

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        // The world data is only ever the folder meta.json names; no heuristic may
        // substitute a different child folder.
        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_MetaPresent_WithStrayRootFiles_DoesNotCopyIntoLive()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        string library = $@"{DataDirectory}\{SaveGameService.SavesRootName}\{MapName}";
        fs.AddDirectory(library, "Alpha");
        fs.AddFile($@"{library}\Alpha\players.db", "stray-data"); // flat-looking, but a meta.json exists
        fs.AddFile($@"{library}\Alpha\meta.json", MetaJsonStorage1);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_CleansStaleRestoreFolders()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedStoredSave(fs, "Alpha", "new-world");
        fs.AddDirectory($@"{MissionPath}", "storage_1.restore_aabbcc", "storage_1.restore", "keep_me");
        fs.AddFile($@"{MissionPath}\storage_1.restore_aabbcc\players.db", "stale");
        fs.AddFile($@"{MissionPath}\storage_1.restore\players.db", "stale");
        fs.AddFile($@"{MissionPath}\keep_me\players.db", "keep");

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.restore_aabbcc"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.restore"));
        Assert.True(fs.DirectoryExists($@"{MissionPath}\keep_me"));
    }

    /// <summary>Wraps <see cref="FakeFileSystem"/> and can fail folder operations on demand.</summary>
    private sealed class FaultyFileSystem : IFileSystem
    {
        private readonly FakeFileSystem _inner;

        public FaultyFileSystem(FakeFileSystem inner) => _inner = inner;

        public Func<string, bool>? MoveThrowsWhen { get; set; }

        public Func<string, bool>? DeleteThrowsWhen { get; set; }

        public Func<string, bool>? CopyDirectoryThrowsWhen { get; set; }

        public Func<string, bool>? WriteAllTextThrowsWhen { get; set; }

        public void AddFile(string path, string contents) => _inner.AddFile(path, contents);

        public void AddDirectory(string path, params string[] childDirectoryNames) => _inner.AddDirectory(path, childDirectoryNames);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetDirectories(string path) => _inner.GetDirectories(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) =>
            _inner.GetFiles(path, searchPattern, recursive);

        public string? TryGetFileContents(string path) => _inner.TryGetFileContents(path);

        public void CopyFile(string sourcePath, string destinationPath) => _inner.CopyFile(sourcePath, destinationPath);

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public void WriteAllText(string path, string contents)
        {
            if (WriteAllTextThrowsWhen?.Invoke(path) == true)
            {
                throw new IOException("write failed");
            }

            _inner.WriteAllText(path, contents);
        }

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void CopyDirectory(string sourcePath, string destinationPath)
        {
            if (CopyDirectoryThrowsWhen?.Invoke(sourcePath) == true)
            {
                throw new IOException("copy failed");
            }

            _inner.CopyDirectory(sourcePath, destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            if (DeleteThrowsWhen?.Invoke(path) == true)
            {
                throw new IOException("delete failed");
            }

            _inner.DeleteDirectory(path, recursive);
        }

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (MoveThrowsWhen?.Invoke(sourcePath) == true)
            {
                throw new IOException("move failed");
            }

            _inner.MoveDirectory(sourcePath, destinationPath);
        }
    }
}
