using DayZModManager.App.Services;
using DayZModManager.App.ViewModels;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Tests.ViewModels;

public class MapTypesViewModelTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string MapName = "dayzOffline.chernarusplus";

    private static MapTypesViewModel Create(
        TypesConfig config,
        IMapService? mapService = null,
        ITypesConfigStore? typesConfigStore = null)
    {
        mapService ??= new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"));
        typesConfigStore ??= new FakeTypesConfigStore();

        return new MapTypesViewModel(
            mapService,
            new FakeTypesService(),
            typesConfigStore,
            new FakeServerConfigService(),
            new FakeBatchFileService(),
            new FakeFileSystem(),
            new FakeDialogs(),
            new LogViewModel(),
            config,
            @"D:\data");
    }

    private static Settings Settings() => new()
    {
        ServerPath = ServerPath,
        WorkshopPath = @"D:\workshop",
        BatFileName = "run.bat",
    };

    [Fact]
    public void Refresh_WithNoCurrentMap_AppliesFirstDiscoveredMap()
    {
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();

        MapTypesViewModel vm = Create(config, typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(MapName, config.CurrentMap);
        Assert.False(vm.CanApplyMap);
        Assert.True(store.SaveCalled);
    }

    [Fact]
    public void Refresh_WithCurrentMap_DoesNotOverride()
    {
        var config = new TypesConfig { CurrentMap = MapName };

        MapTypesViewModel vm = Create(config);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(MapName, config.CurrentMap);
        Assert.False(vm.CanApplyMap);
    }

    [Fact]
    public void Refresh_WithNoMaps_LeavesCurrentMapEmpty()
    {
        var config = new TypesConfig();

        MapTypesViewModel vm = Create(config, new FakeMapService());
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(string.Empty, config.CurrentMap);
    }

    private sealed class FakeMapService : IMapService
    {
        private readonly IReadOnlyList<MapInfo> _maps;

        public FakeMapService(params MapInfo[] maps) => _maps = maps;

        public IReadOnlyList<MapInfo> DiscoverMaps(string serverPath) => _maps;

        public string? ResolveMapPath(string serverPath, string mapName) =>
            _maps.FirstOrDefault(m => m.Name == mapName)?.Path;
    }

    private sealed class FakeTypesService : ITypesService
    {
        public IReadOnlyList<string> DiscoverTypeFiles(string workshopPath, string modName) => Array.Empty<string>();

        public TypesOperationResult ConfigureMod(
            TypesConfig config, string mapName, string missionPath, string workshopPath, string modName,
            IReadOnlyList<string> sourceFiles, IReadOnlySet<string> loadedModNames) =>
            new() { Success = true };

        public TypesOperationResult RemoveFiles(
            TypesConfig config, string mapName, string missionPath, string modName,
            IReadOnlySet<string> fileLeaves, IReadOnlySet<string> loadedModNames) =>
            new() { Success = true };

        public TypesOperationResult CleanInvalid(
            TypesConfig config, string mapName, string missionPath,
            IReadOnlySet<string> validModNames, IReadOnlySet<string> loadedModNames) =>
            new() { Success = true };

        public bool SyncEconomyCore(TypesConfig config, string mapName, string missionPath, IReadOnlySet<string> loadedModNames) =>
            true;
    }

    private sealed class FakeTypesConfigStore : ITypesConfigStore
    {
        public bool SaveCalled { get; private set; }

        public ConfigLoadResult<TypesConfig> Load(string dataDirectory) => ConfigLoadResult<TypesConfig>.Missing();

        public void Save(string dataDirectory, TypesConfig config) => SaveCalled = true;
    }

    private sealed class FakeServerConfigService : IServerConfigService
    {
        public bool UpdateTemplate(string serverPath, string missionFolderName) => true;
    }

    private sealed class FakeBatchFileService : IBatchFileService
    {
        public IReadOnlyList<string> ReadModList(string batFilePath) => Array.Empty<string>();

        public bool HasModListLine(string batFilePath) => true;

        public bool WriteModList(string batFilePath, IReadOnlyList<string> modNames) => true;

        public bool WriteServerProfile(string batFilePath, string relativeProfile) => true;
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        public bool DirectoryExists(string path) => false;

        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();

        public bool FileExists(string path) => false;

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) => Array.Empty<string>();

        public void CopyFile(string sourcePath, string destinationPath) { }

        public void DeleteFile(string path) { }

        public string ReadAllText(string path) => string.Empty;

        public void WriteAllText(string path, string contents) { }

        public void CreateDirectory(string path) { }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public void ShowMessage(string message, string title, bool isError = false) { }

        public bool Confirm(string message, string title) => true;

        public string? PickFolder(string title = "Select a folder") => null;

        public string? PickFile(string title, string filter, string initialDirectory) => null;

        public IReadOnlyList<string>? PickTypeFiles(string modName, IReadOnlyList<string> files) => null;
    }
}
