using System.IO;
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
        ITypesConfigStore? typesConfigStore = null,
        FakeTypesService? typesService = null,
        FakeDialogs? dialogs = null,
        FakeSaveGameService? saveGameService = null)
    {
        mapService ??= new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"));
        typesConfigStore ??= new FakeTypesConfigStore();
        typesService ??= new FakeTypesService();
        dialogs ??= new FakeDialogs();
        saveGameService ??= new FakeSaveGameService();

        return new MapTypesViewModel(
            mapService,
            typesService,
            saveGameService,
            typesConfigStore,
            new FakeServerConfigService(),
            new FakeBatchFileService(),
            new FakeFileSystem(),
            dialogs,
            new LogViewModel(),
            config,
            new FakeDataDirectoryProvider());
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
        vm.ReconcileAppliedMap();

        Assert.Equal(MapName, config.CurrentMap);
        Assert.True(store.SaveCalled);
    }

    [Fact]
    public void Refresh_WithCurrentMap_DoesNotOverride()
    {
        var config = new TypesConfig { CurrentMap = MapName };
        var store = new FakeTypesConfigStore();

        MapTypesViewModel vm = Create(config, typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(MapName, config.CurrentMap);
        Assert.Equal(MapName, vm.SelectedMap);
        Assert.False(store.SaveCalled, "an already-applied valid map should not be re-applied");
    }

    [Fact]
    public void SelectingAMap_AppliesItImmediately()
    {
        const string secondMap = "dayzOffline.deerisle";
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();
        var mapService = new FakeMapService(
            new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"),
            new MapInfo(secondMap, $@"{ServerPath}\mpmissions\{secondMap}"));

        MapTypesViewModel vm = Create(config, mapService: mapService, typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());

        vm.SelectedMap = secondMap;

        Assert.Equal(secondMap, config.CurrentMap);
        Assert.Equal(secondMap, vm.SelectedMap);
        Assert.True(store.SaveCalled);
    }

    [Fact]
    public void Refresh_WithNoMaps_LeavesCurrentMapEmpty()
    {
        var config = new TypesConfig();

        MapTypesViewModel vm = Create(config, new FakeMapService());
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(string.Empty, config.CurrentMap);
    }

    [Fact]
    public void ReconcileAppliedMap_FirstRun_AppliesFirstDiscoveredMap()
    {
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();

        MapTypesViewModel vm = Create(config, typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(MapName, config.CurrentMap);
        Assert.True(store.SaveCalled);
    }

    [Fact]
    public void ReconcileAppliedMap_MissingRetainedMap_WarnsAndKeepsSelection()
    {
        const string missingMap = "dayzOffline.deerisle";
        var config = new TypesConfig
        {
            CurrentMap = missingMap,
            Maps = { [missingMap] = new MapTypesConfig() },
        };
        var store = new FakeTypesConfigStore();

        MapTypesViewModel vm = Create(config, typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(missingMap, config.CurrentMap);
        Assert.Equal(missingMap, vm.SelectedMap);
        Assert.False(store.SaveCalled, "a missing retained map must not trigger an auto-apply");
    }

    [Fact]
    public void SelectingAMap_NotDiscoverable_RevertsToAppliedMap()
    {
        const string deadMap = "dayzOffline.deerisle";
        var config = new TypesConfig
        {
            CurrentMap = MapName,
            Maps =
            {
                [MapName] = new MapTypesConfig(),
                [deadMap] = new MapTypesConfig(), // persisted but no longer on disk
            },
        };
        var store = new FakeTypesConfigStore();

        MapTypesViewModel vm = Create(config, typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());

        vm.SelectedMap = deadMap;

        Assert.Equal(MapName, config.CurrentMap);
        Assert.Equal(MapName, vm.SelectedMap);
        Assert.False(store.SaveCalled, "an unresolvable map must not be applied");
    }

    private static (MapTypesViewModel Vm, FakeTypesService Types, FakeDialogs Dialogs) CreateTypesVm(
        TypesConfig config,
        FakeTypesService types,
        FakeDialogs dialogs,
        IReadOnlyList<string>? workshopMods = null,
        IReadOnlyList<string>? loadedMods = null,
        FakeSaveGameService? saveGameService = null)
    {
        workshopMods ??= new[] { "@CF" };
        loadedMods ??= new[] { "@CF" };

        MapTypesViewModel vm = Create(
            config,
            mapService: new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}")),
            typesConfigStore: new FakeTypesConfigStore(),
            typesService: types,
            dialogs: dialogs,
            saveGameService: saveGameService);
        vm.Refresh(Settings(), workshopMods, loadedMods);
        return (vm, types, dialogs);
    }

    private static ModTypesEntry Entry(string modName, params string[] generatedFiles) =>
        new() { ModName = modName, GeneratedFiles = generatedFiles.ToList() };

    private static TypesConfig ConfigWithEntry(ModTypesEntry entry) =>
        new()
        {
            CurrentMap = MapName,
            Maps = { [MapName] = new MapTypesConfig { Mods = { entry } } },
        };

    [Fact]
    public void ConfigureMod_AsksConfirmation_WhenSelectionDeletesActiveFiles()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        const string hardcore = @"D:\workshop\@CF\hardcore_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual, hardcore } };
        var dialogs = new FakeDialogs { ConfirmResult = false, SelectedFiles = new[] { hardcore } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigXmlCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Contains("CF_casual_types.xml", fakeDialogs.LastConfirmMessage);
        Assert.Equal(0, fakeTypes.ConfigureModCalls);
    }

    [Fact]
    public void ConfigureMod_Applies_WhenReplacementConfirmed()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        const string hardcore = @"D:\workshop\@CF\hardcore_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual, hardcore } };
        var dialogs = new FakeDialogs { ConfirmResult = true, SelectedFiles = new[] { hardcore } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigXmlCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(1, fakeTypes.ConfigureModCalls);
        Assert.Equal(new[] { hardcore }, fakeTypes.ConfigureModSourceFiles);
    }

    [Fact]
    public void ConfigureMod_Prompts_EvenWhenSameFileReSelected()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs { ConfirmResult = true, SelectedFiles = new[] { casual } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigXmlCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(1, fakeTypes.ConfigureModCalls);
    }

    [Fact]
    public void ConfigureMod_Cancels_WhenSameFileReSelected()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs { ConfirmResult = false, SelectedFiles = new[] { casual } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigXmlCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(0, fakeTypes.ConfigureModCalls);
    }

    [Fact]
    public void ConfigureMod_PreSelectsOnlyActiveFilesInPicker()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        const string hardcore = @"D:\workshop\@CF\hardcore_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual, hardcore } };
        var dialogs = new FakeDialogs { SelectedFiles = new[] { casual } };

        (MapTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigXmlCommand.Execute(null);

        Assert.NotNull(fakeDialogs.LastActiveFiles);
        Assert.Contains(casual, fakeDialogs.LastActiveFiles);
        Assert.DoesNotContain(hardcore, fakeDialogs.LastActiveFiles);
    }

    [Fact]
    public void RemoveSelected_AsksConfirmation_BeforeDeleting()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = false };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);
        vm.SelectedTypesRows.Add(new TypesRowViewModel("@CF", "CF_types.xml", isInactive: false));

        vm.RemoveSelectedCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(0, fakeTypes.RemoveFilesCalls);
    }

    [Fact]
    public void RemoveSelected_Deletes_WhenConfirmed()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = true };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);
        vm.SelectedTypesRows.Add(new TypesRowViewModel("@CF", "CF_types.xml", isInactive: false));

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Equal(1, fakeTypes.RemoveFilesCalls);
        Assert.NotNull(fakeTypes.LastRemoveLeaves);
        Assert.Contains("CF_types.xml", fakeTypes.LastRemoveLeaves);
    }

    [Fact]
    public void CleanInvalid_AsksConfirmation_WhenInvalidModsExist()
    {
        var config = ConfigWithEntry(Entry("@Ghost", @"db\ModTypes\Ghost_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = false };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, workshopMods: new[] { "@CF" }, loadedMods: new[] { "@CF" });

        vm.CleanInvalidCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Contains("@Ghost", fakeDialogs.LastConfirmMessage);
        Assert.Equal(0, fakeTypes.CleanInvalidCalls);
    }

    [Fact]
    public void CleanInvalid_DoesNotPrompt_WhenNothingInvalid()
    {
        var config = new TypesConfig { CurrentMap = MapName, Maps = { [MapName] = new MapTypesConfig() } };
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs();

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, workshopMods: new[] { "@CF" }, loadedMods: new[] { "@CF" });

        vm.CleanInvalidCommand.Execute(null);

        Assert.Equal(0, fakeDialogs.ConfirmCalls);
        Assert.Equal(1, fakeTypes.CleanInvalidCalls);
    }

    private static TypesConfig AppliedConfig() =>
        new() { CurrentMap = MapName, Maps = { [MapName] = new MapTypesConfig() } };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task AddSave_UsesEnteredName_ForNewSave()
    {
        var dialogs = new FakeDialogs { AskTextResult = "First" };
        var saves = new FakeSaveGameService();

        (MapTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

        vm.AddSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.AddSaveCalls == 1);

        Assert.Equal(1, saves.AddSaveCalls);
        Assert.Equal("First", saves.LastAddName);
        Assert.False(saves.LastAddOverwrite);
        Assert.Equal(0, fakeDialogs.ConfirmCalls);
    }

    [Fact]
    public async Task AddSave_AsksOverwrite_WhenNameExists()
    {
        var dialogs = new FakeDialogs { AskTextResult = "First", ConfirmResult = false };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "First" } };

        (MapTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

        vm.AddSaveCommand.Execute(null);
        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(0, saves.AddSaveCalls);

        // Confirming the overwrite proceeds with overwrite: true.
        fakeDialogs.ConfirmResult = true;
        vm.AddSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.AddSaveCalls == 1);
        Assert.Equal(1, saves.AddSaveCalls);
        Assert.True(saves.LastAddOverwrite);
    }

    [Fact]
    public void LoadSave_AsksConfirmation_AndAbortsOnNo()
    {
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (MapTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(0, saves.LoadSaveCalls);
    }

    [Fact]
    public async Task LoadSave_LoadsSelected_WhenConfirmed()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.Equal(1, saves.LoadSaveCalls);
        Assert.Equal("Alpha", saves.LastLoadName);
    }

    [Fact]
    public async Task NewGame_AsksConfirmation_AndCallsService()
    {
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var saves = new FakeSaveGameService();

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

        vm.NewGameCommand.Execute(null);
        Assert.Equal(0, saves.NewGameCalls);

        dialogs.ConfirmResult = true;
        vm.NewGameCommand.Execute(null);
        await WaitUntilAsync(() => saves.NewGameCalls == 1);
        Assert.Equal(1, saves.NewGameCalls);
    }

    [Fact]
    public async Task DeleteSave_AsksConfirmation_AndDeletesSelected()
    {
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.DeleteSaveCommand.Execute(null);
        Assert.Equal(0, saves.DeleteSaveCalls);

        dialogs.ConfirmResult = true;
        vm.DeleteSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.DeleteSaveCalls == 1);
        Assert.Equal(1, saves.DeleteSaveCalls);
        Assert.Equal("Alpha", saves.LastDeleteName);
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
        public IReadOnlyList<string> DiscoveryFiles { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string>? ConfigureModSourceFiles { get; private set; }

        public int ConfigureModCalls { get; private set; }

        public IReadOnlySet<string>? LastRemoveLeaves { get; private set; }

        public int RemoveFilesCalls { get; private set; }

        public IReadOnlySet<string>? LastCleanValidMods { get; private set; }

        public int CleanInvalidCalls { get; private set; }

        public IReadOnlyList<string> DiscoverTypeFiles(string workshopPath, string modName) => DiscoveryFiles;

        public string? GetGeneratedFileName(string workshopPath, string modName, string sourceFile)
        {
            string modPath = Path.Combine(workshopPath, modName);
            string? relative = RelativeWithin(modPath, sourceFile);
            if (relative is null)
            {
                return null;
            }

            string clean = modName.StartsWith('@') ? modName[1..] : modName;
            return clean + "_" + relative.Replace('\\', '_').Replace('/', '_');
        }

        public TypesOperationResult ConfigureMod(
            TypesConfig config, string mapName, string missionPath, string workshopPath, string modName,
            IReadOnlyList<string> sourceFiles, IReadOnlySet<string> loadedModNames)
        {
            ConfigureModCalls++;
            ConfigureModSourceFiles = sourceFiles;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult RemoveFiles(
            TypesConfig config, string mapName, string missionPath, string modName,
            IReadOnlySet<string> fileLeaves, IReadOnlySet<string> loadedModNames)
        {
            RemoveFilesCalls++;
            LastRemoveLeaves = fileLeaves;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult CleanInvalid(
            TypesConfig config, string mapName, string missionPath,
            IReadOnlySet<string> validModNames, IReadOnlySet<string> loadedModNames)
        {
            CleanInvalidCalls++;
            LastCleanValidMods = validModNames;
            return new TypesOperationResult { Success = true };
        }

        public bool SyncEconomyCore(TypesConfig config, string mapName, string missionPath, IReadOnlySet<string> loadedModNames) =>
            true;

        private static string? RelativeWithin(string basePath, string fullPath)
        {
            string relative = Path.GetRelativePath(basePath, fullPath);
            return relative.Equals(".", StringComparison.Ordinal) || relative.StartsWith("..", StringComparison.Ordinal)
                ? null
                : relative;
        }
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

    private sealed class FakeSaveGameService : ISaveGameService
    {
        public IReadOnlyList<string> StoredSaves { get; set; } = Array.Empty<string>();

        public int AddSaveCalls { get; private set; }

        public string? LastAddName { get; private set; }

        public bool LastAddOverwrite { get; private set; }

        public int LoadSaveCalls { get; private set; }

        public string? LastLoadName { get; private set; }

        public int NewGameCalls { get; private set; }

        public int DeleteSaveCalls { get; private set; }

        public string? LastDeleteName { get; private set; }

        public int ReadInstanceId(string serverPath) => 1;

        public string GetStorageFolderPath(string serverPath, string mapName) =>
            Path.Combine(serverPath, "mpmissions", mapName, "storage_1");

        public IReadOnlyList<string> ListSaves(string dataDirectory, string mapName) => StoredSaves;

        public SaveGameResult AddSave(string serverPath, string mapName, string dataDirectory, string saveName, bool overwrite)
        {
            AddSaveCalls++;
            LastAddName = saveName;
            LastAddOverwrite = overwrite;
            return new SaveGameResult { Success = true, Message = $"Saved \"{saveName}\"." };
        }

        public SaveGameResult LoadSave(string serverPath, string mapName, string dataDirectory, string saveName)
        {
            LoadSaveCalls++;
            LastLoadName = saveName;
            return new SaveGameResult { Success = true, Message = $"Loaded \"{saveName}\"." };
        }

        public SaveGameResult NewGame(string serverPath, string mapName)
        {
            NewGameCalls++;
            return new SaveGameResult { Success = true, Message = "New game started." };
        }

        public SaveGameResult DeleteSave(string dataDirectory, string mapName, string saveName)
        {
            DeleteSaveCalls++;
            LastDeleteName = saveName;
            return new SaveGameResult { Success = true, Message = $"Deleted \"{saveName}\"." };
        }
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

        public void CopyDirectory(string sourcePath, string destinationPath) { }

        public void DeleteDirectory(string path, bool recursive) { }

        public void MoveDirectory(string sourcePath, string destinationPath) { }
    }

    private sealed class FakeDataDirectoryProvider : IDataDirectoryProvider
    {
        public string Current => @"D:\data";

        public void Initialize() { }

        public string Resolve(Settings settings) => Current;

        public void MoveTo(string directory, Settings settings) { }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public int ConfirmCalls { get; private set; }

        public string? LastConfirmMessage { get; private set; }

        public IReadOnlyList<string>? SelectedFiles { get; set; }

        public IReadOnlySet<string>? LastActiveFiles { get; private set; }

        public string? AskTextResult { get; set; } = null;

        public void ShowMessage(string message, string title, bool isError = false) { }

        public bool Confirm(string message, string title)
        {
            ConfirmCalls++;
            LastConfirmMessage = message;
            return ConfirmResult;
        }

        public string? AskText(string title, string prompt, string defaultValue = "") => AskTextResult;

        public string? PickFolder(string title = "Select a folder") => null;

        public string? PickFile(string title, string filter, string initialDirectory) => null;

        public IReadOnlyList<string>? PickTypeFiles(string modName, IReadOnlyList<string> files, IReadOnlySet<string>? activeFiles = null)
        {
            LastActiveFiles = activeFiles;
            return SelectedFiles;
        }
    }
}
