using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class SaveGameServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string MapName = "dayzOffline.chernarusplus";
    private const string MissionPath = @"D:\DayZServer\mpmissions\dayzOffline.chernarusplus";
    private const string DataDirectory = @"D:\app\data";
    private const string PresetFolder = DataDirectory + @"\Presets\dayzOffline.chernarusplus\__default_preset__";
    private const string SavesFolder = PresetFolder + @"\saves";
    private const int InstanceId = 1;

    private static string LiveStorage(int instanceId = InstanceId) => $@"{MissionPath}\storage_{instanceId}";

    private static string SavePath(string saveName) => $@"{SavesFolder}\{saveName}";

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

    private static void SeedLiveStorage(FakeFileSystem fs, int instanceId = InstanceId)
    {
        fs.AddDirectory(MissionPath, $"storage_{instanceId}");
        fs.AddFile($@"{LiveStorage(instanceId)}\players.db", "live-data");
    }

    private static string MetaJson(string storageFolder = "storage_1", string? savedAtUtc = null) =>
        $"{{\"saveName\":\"x\",\"storageFolder\":\"{storageFolder}\",\"savedAtUtc\":\"{savedAtUtc ?? "2026-01-01T00:00:00Z"}\",\"createdAtUtc\":\"2026-01-01T00:00:00Z\"}}";

    private static void SeedNestedStoredSave(FakeFileSystem fs, string saveName, string contents = "saved-data")
    {
        fs.AddDirectory(SavesFolder, saveName);
        fs.AddDirectory(SavePath(saveName), "storage_1");
        fs.AddFile($@"{SavePath(saveName)}\storage_1\players.db", contents);
        fs.AddFile($@"{SavePath(saveName)}\save-meta.json", MetaJson());
    }

    private static void SeedStoredSaveWithMeta(FakeFileSystem fs, string saveName, string metaJson, string contents = "saved-data")
    {
        fs.AddDirectory(SavesFolder, saveName);
        fs.AddDirectory(SavePath(saveName), "storage_1");
        fs.AddFile($@"{SavePath(saveName)}\storage_1\players.db", contents);
        fs.AddFile($@"{SavePath(saveName)}\save-meta.json", metaJson);
    }

    [Fact]
    public void GetStorageFolderPath_IncludesInstanceId()
    {
        string path = CreateService(new FakeFileSystem()).GetStorageFolderPath(ServerPath, MapName, 3);

        Assert.Equal($@"{MissionPath}\storage_3", path);
    }

    [Fact]
    public void ListStorageInstanceIds_ReturnsStorageFolders_ExcludingStaging()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(MissionPath, "storage_1", "storage_5", "storage_1.restore_aabbcc", "storage_1.old", "keep_me");
        fs.AddFile($@"{LiveStorage(5)}\players.db", "five");

        IReadOnlyList<int> ids = CreateService(fs).ListStorageInstanceIds(ServerPath, MapName);

        Assert.Equal(new[] { 1, 5 }, ids);
    }

    [Fact]
    public void ListStorageInstanceIds_ReturnsEmpty_WhenMissionMissing()
    {
        Assert.Empty(CreateService(new FakeFileSystem()).ListStorageInstanceIds(ServerPath, MapName));
    }

    [Fact]
    public void DeleteStorage_RemovesFolder()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs, instanceId: 5);

        SaveGameResult result = CreateService(fs).DeleteStorage(ServerPath, MapName, 5);

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(LiveStorage(5)));
    }

    [Fact]
    public void DeleteStorage_NoOp_WhenAbsent()
    {
        SaveGameResult result = CreateService(new FakeFileSystem()).DeleteStorage(ServerPath, MapName, 5);

        Assert.True(result.Success);
        Assert.True(result.Informational);
    }

    [Fact]
    public void DeleteStorage_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs, instanceId: 5);

        SaveGameResult result = CreateRunningService(fs).DeleteStorage(ServerPath, MapName, 5);

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.True(fs.DirectoryExists(LiveStorage(5)));
    }

    [Fact]
    public void AddSave_CopiesLiveStorageIntoLibrary_AndWritesSaveMeta()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: false);

        Assert.True(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{SavePath("Alpha")}\storage_1\players.db"));
        string? meta = fs.TryGetFileContents($@"{SavePath("Alpha")}\save-meta.json");
        Assert.NotNull(meta);
        Assert.Contains("\"storageFolder\": \"storage_1\"", meta);
        Assert.Contains("\"savedAtUtc\"", meta);
    }

    [Fact]
    public void AddSave_ReturnsFailure_WhenLiveStorageMissing()
    {
        var fs = new FakeFileSystem();

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: false);

        Assert.False(result.Success);
    }

    [Fact]
    public void AddSave_RejectsDuplicate_UnlessOverwrite()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedNestedStoredSave(fs, "Alpha");
        SaveGameService service = CreateService(fs);

        Assert.False(service.AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: false).Success);
        Assert.True(service.AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: true).Success);
    }

    [Fact]
    public void AddSave_RejectsInvalidName()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, SavesFolder, InstanceId, "bad\\name", overwrite: false);

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
        fs.AddDirectory(SavesFolder);

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, SavesFolder, InstanceId, saveName, overwrite: true);

        Assert.False(result.Success);
    }

    [Fact]
    public void AddSave_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateRunningService(fs).AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: false);

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.False(fs.DirectoryExists(SavePath("Alpha")));
    }

    [Fact]
    public void AddSave_Overwrite_ReplacesStoredSave()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SaveGameService service = CreateService(fs);

        Assert.True(service.AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: false).Success);

        fs.AddFile($@"{LiveStorage()}\players.db", "updated-data");
        Assert.True(service.AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: true).Success);

        Assert.Equal("updated-data", fs.TryGetFileContents($@"{SavePath("Alpha")}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{SavePath("Alpha")}.old"));
    }

    [Fact]
    public void LoadSave_ReplacesLiveStorage()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedNestedStoredSave(fs, "Alpha", "new-world");

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{LiveStorage()}\players.db"));
    }

    [Fact]
    public void LoadSave_CreatesLiveStorage_WhenAbsent()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(MissionPath);
        SeedNestedStoredSave(fs, "Alpha", "new-world");

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{LiveStorage()}\players.db"));
    }

    [Fact]
    public void LoadSave_ReturnsFailure_WhenSaveMissing()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, SavesFolder, InstanceId, "Ghost");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LiveStorage()}\players.db"));
    }

    [Fact]
    public void LoadSave_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedNestedStoredSave(fs, "Alpha", "new-world");

        SaveGameResult result = CreateRunningService(fs).LoadSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha");

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LiveStorage()}\players.db"));
    }

    [Fact]
    public void LoadSave_Fails_WhenMetaPresent_ButNoUsableStorage()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        fs.AddDirectory(SavesFolder, "Alpha");
        fs.AddFile($@"{SavePath("Alpha")}\save-meta.json", MetaJson());

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LiveStorage()}\players.db"));
    }

    [Fact]
    public void LoadSave_FallsBackToSingleChild_WhenMetaStorageFolderMissing()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        fs.AddDirectory(SavesFolder, "Alpha");
        fs.AddDirectory(SavePath("Alpha"), "actual");
        fs.AddFile($@"{SavePath("Alpha")}\actual\players.db", "new-world");
        fs.AddFile($@"{SavePath("Alpha")}\save-meta.json", MetaJson()); // references storage_1, which is absent

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{LiveStorage()}\players.db"));
    }

    [Fact]
    public void LoadSave_MetaPresent_WithStrayRootFiles_DoesNotCopyIntoLive()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        fs.AddDirectory(SavesFolder, "Alpha");
        fs.AddFile($@"{SavePath("Alpha")}\players.db", "stray-data");
        fs.AddFile($@"{SavePath("Alpha")}\save-meta.json", MetaJson());

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LiveStorage()}\players.db"));
    }

    [Fact]
    public void WipeWorld_DeletesLiveStorage()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).WipeWorld(ServerPath, MapName, InstanceId);

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(LiveStorage()));
    }

    [Fact]
    public void WipeWorld_NoOp_WhenNoLiveStorage()
    {
        var fs = new FakeFileSystem();

        SaveGameResult result = CreateService(fs).WipeWorld(ServerPath, MapName, InstanceId);

        Assert.True(result.Success);
        Assert.True(result.Informational);
    }

    [Fact]
    public void WipeWorld_ReturnsFailure_WhenServerRunning()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateRunningService(fs).WipeWorld(ServerPath, MapName, InstanceId);

        Assert.False(result.Success);
        Assert.Contains("server is running", result.Message);
        Assert.True(fs.DirectoryExists(LiveStorage()));
    }

    [Fact]
    public void ListSaves_ReturnsStoredFolders()
    {
        var fs = new FakeFileSystem();
        SeedNestedStoredSave(fs, "Alpha");
        SeedNestedStoredSave(fs, "Beta");

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(SavesFolder);

        Assert.Equal(new[] { "Alpha", "Beta" }, saves);
    }

    [Fact]
    public void ListSaves_OrdersBySavedAtUtc_OldestFirst()
    {
        var fs = new FakeFileSystem();
        SeedStoredSaveWithMeta(fs, "Alpha", MetaJson(savedAtUtc: "2026-01-01T00:00:00Z"));
        SeedStoredSaveWithMeta(fs, "Gamma", MetaJson(savedAtUtc: "2026-01-03T00:00:00Z"));
        SeedStoredSaveWithMeta(fs, "Beta", MetaJson(savedAtUtc: "2026-01-02T00:00:00Z"));

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(SavesFolder);

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, saves);
    }

    [Fact]
    public void ListSaves_TreatsCorruptMeta_AsSaveWithoutTimestamp()
    {
        var fs = new FakeFileSystem();
        SeedStoredSaveWithMeta(fs, "Zulu", MetaJson(savedAtUtc: "2026-01-02T00:00:00Z"));
        SeedStoredSaveWithMeta(fs, "Gamma", "{not-json");

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(SavesFolder);

        Assert.Equal(new[] { "Zulu", "Gamma" }, saves);
    }

    [Fact]
    public void GetMeta_ReturnsMissing_WhenSaveHasNoMeta()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(SavesFolder, "Alpha");

        ConfigLoadResult<SaveMetaData> result = CreateService(fs).GetMeta(SavesFolder, "Alpha");

        Assert.Equal(ConfigLoadStatus.Missing, result.Status);
    }

    [Fact]
    public void GetMeta_ReturnsMeta_WhenPresent()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SaveGameService service = CreateService(fs);
        service.AddSave(ServerPath, MapName, SavesFolder, InstanceId, "Alpha", overwrite: false);

        ConfigLoadResult<SaveMetaData> result = service.GetMeta(SavesFolder, "Alpha");

        Assert.Equal(ConfigLoadStatus.Success, result.Status);
        Assert.Equal("storage_1", result.Value!.StorageFolder);
        Assert.Equal("Alpha", result.Value.SaveName);
    }

    [Fact]
    public void GetSaveFolderPath_ReturnsLibraryPathForSave()
    {
        string path = CreateService(new FakeFileSystem()).GetSaveFolderPath(SavesFolder, "Alpha");

        Assert.Equal($@"{SavesFolder}\Alpha", path);
    }

    [Fact]
    public void DeleteSave_RemovesStoredSave()
    {
        var fs = new FakeFileSystem();
        SeedNestedStoredSave(fs, "Alpha");

        SaveGameResult result = CreateService(fs).DeleteSave(SavesFolder, "Alpha");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(SavePath("Alpha")));
    }

    [Fact]
    public void DeleteSave_ReturnsFailure_WhenSaveMissing()
    {
        SaveGameResult result = CreateService(new FakeFileSystem()).DeleteSave(SavesFolder, "Ghost");

        Assert.False(result.Success);
    }

    [Fact]
    public void DeleteSave_RejectsDotDot_AndDoesNotTouchLibrary()
    {
        var fs = new FakeFileSystem();
        SeedNestedStoredSave(fs, "Alpha");

        SaveGameResult result = CreateService(fs).DeleteSave(SavesFolder, "..");

        Assert.False(result.Success);
        Assert.True(fs.DirectoryExists(SavesFolder));
        Assert.True(fs.DirectoryExists(SavePath("Alpha")));
    }

    [Fact]
    public void RenameSave_MovesFolder_AndUpdatesSaveMeta()
    {
        var fs = new FakeFileSystem();
        SeedNestedStoredSave(fs, "Alpha", "world");

        SaveGameResult result = CreateService(fs).RenameSave(SavesFolder, "Alpha", "Beta");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(SavePath("Alpha")));
        Assert.Equal("world", fs.TryGetFileContents($@"{SavePath("Beta")}\storage_1\players.db"));
        ConfigLoadResult<SaveMetaData> meta = CreateService(fs).GetMeta(SavesFolder, "Beta");
        Assert.Equal(ConfigLoadStatus.Success, meta.Status);
        Assert.Equal("Beta", meta.Value!.SaveName);
    }

    [Fact]
    public void RenameSave_Fails_WhenTargetExists()
    {
        var fs = new FakeFileSystem();
        SeedNestedStoredSave(fs, "Alpha");
        SeedNestedStoredSave(fs, "Beta");

        Assert.False(CreateService(fs).RenameSave(SavesFolder, "Alpha", "Beta").Success);
        Assert.True(fs.DirectoryExists(SavePath("Alpha")));
    }

    [Fact]
    public void RenameSave_Fails_WhenSourceMissing()
    {
        var fs = new FakeFileSystem();

        Assert.False(CreateService(fs).RenameSave(SavesFolder, "Ghost", "Beta").Success);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("bad\\name")]
    [InlineData("trailing.")]
    public void RenameSave_RejectsUnsafeTargetName(string newName)
    {
        var fs = new FakeFileSystem();
        SeedNestedStoredSave(fs, "Alpha");

        Assert.False(CreateService(fs).RenameSave(SavesFolder, "Alpha", newName).Success);
        Assert.True(fs.DirectoryExists(SavePath("Alpha")));
    }

}
