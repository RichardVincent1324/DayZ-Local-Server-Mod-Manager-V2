using System.IO;
using DayZModManager.Core;
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
        FakeSaveGameService? saveGameService = null,
        FakeServerProcessState? serverProcess = null,
        FakeFileSystem? fileSystem = null,
        FakeBatchFileService? batchFileService = null,
        FakeProcessLauncher? processLauncher = null,
        FakePresetService? presetService = null,
        IJunctionService? junctionService = null)
    {
        mapService ??= new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"));
        typesConfigStore ??= new FakeTypesConfigStore();
        typesService ??= new FakeTypesService();
        dialogs ??= new FakeDialogs();
        saveGameService ??= new FakeSaveGameService();
        serverProcess ??= new FakeServerProcessState();
        fileSystem ??= new FakeFileSystem();
        batchFileService ??= new FakeBatchFileService();
        processLauncher ??= new FakeProcessLauncher();
        presetService ??= new FakePresetService();
        junctionService ??= new FakeJunctionService();

        return new MapTypesViewModel(
            mapService,
            typesService,
            saveGameService,
            typesConfigStore,
            new FakeServerConfigService(),
            batchFileService,
            fileSystem,
            dialogs,
            new LogViewModel(),
            config,
            new FakeDataDirectoryProvider(),
            serverProcess,
            processLauncher,
            presetService,
            junctionService);
    }

    private static string PresetFolder(string mapName) =>
        Path.Combine(@"D:\data", "Presets", mapName, "__default_preset__");

    private static string PresetModTypesFolder(string mapName) =>
        Path.Combine(PresetFolder(mapName), "ModTypes");

    private static string PresetProfilesFolder(string mapName) =>
        Path.Combine(PresetFolder(mapName), "profiles");

    private static string PresetSavesFolder(string mapName) =>
        Path.Combine(PresetFolder(mapName), "saves");

    private static string PresetServerConfig(string mapName) =>
        Path.Combine(PresetFolder(mapName), "serverDZ.cfg");

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
    public void ApplyingAMap_PointsBatchAtThePresetProfilesAndConfig()
    {
        var config = new TypesConfig();
        var batch = new FakeBatchFileService();
        var fs = new FakeFileSystem();

        MapTypesViewModel vm = Create(config, batchFileService: batch, fileSystem: fs);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(MapName, config.CurrentMap);

        // The preset lives outside the server root, so both launch-batch values are
        // expressed as absolute paths.
        Assert.Equal(PresetProfilesFolder(MapName), batch.LastServerProfile);
        Assert.Equal(PresetServerConfig(MapName), batch.LastServerConfig);
        Assert.True(fs.DirectoryExists(PresetProfilesFolder(MapName)));
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
    public void ReconcileAppliedMap_NoServerPath_DoesNotAutoApply()
    {
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();

        MapTypesViewModel vm = Create(config, typesConfigStore: store);
        vm.Refresh(new Settings { ServerPath = string.Empty }, Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(string.Empty, config.CurrentMap);
        Assert.False(store.SaveCalled, "no auto-apply should happen without a configured server path");
    }

    [Fact]
    public void ReconcileAppliedMap_NoBatchFile_DoesNotAutoApply()
    {
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();
        var batch = new FakeBatchFileService();

        MapTypesViewModel vm = Create(config, typesConfigStore: store, batchFileService: batch);
        vm.Refresh(Settings() with { BatFileName = string.Empty }, Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(string.Empty, config.CurrentMap);
        Assert.False(store.SaveCalled, "no auto-apply should happen without a launch batch file");
        Assert.Null(batch.LastServerProfile);
    }

    [Fact]
    public void SelectingAMap_NoBatchFile_DoesNotSwitch()
    {
        const string secondMap = "dayzOffline.deerisle";
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();
        var batch = new FakeBatchFileService();
        var mapService = new FakeMapService(
            new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"),
            new MapInfo(secondMap, $@"{ServerPath}\mpmissions\{secondMap}"));

        MapTypesViewModel vm = Create(
            config, mapService: mapService, typesConfigStore: store, batchFileService: batch);
        vm.Refresh(Settings() with { BatFileName = string.Empty }, Array.Empty<string>(), Array.Empty<string>());

        vm.SelectedMap = secondMap;

        Assert.NotEqual(secondMap, config.CurrentMap);
        Assert.False(store.SaveCalled, "a map switch must be refused without a launch batch file");
        Assert.Null(batch.LastServerProfile);
    }

    [Fact]
    public void ReconcileAppliedMap_ConfigOnlyMap_NoServerMap_DoesNotAutoApply()
    {
        var config = new TypesConfig { Maps = { [MapName] = new MapTypesConfig() } };
        var store = new FakeTypesConfigStore();

        MapTypesViewModel vm = Create(config, mapService: new FakeMapService(), typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(string.Empty, config.CurrentMap);
        Assert.False(store.SaveCalled, "a config-only map with no folder on the server must not be auto-applied");
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
        FakeSaveGameService? saveGameService = null,
        FakeFileSystem? fileSystem = null,
        FakeTypesConfigStore? typesConfigStore = null,
        FakeProcessLauncher? processLauncher = null)
    {
        workshopMods ??= new[] { "@CF" };
        loadedMods ??= new[] { "@CF" };

        MapTypesViewModel vm = Create(
            config,
            mapService: new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}")),
            typesConfigStore: typesConfigStore ?? new FakeTypesConfigStore(),
            typesService: types,
            dialogs: dialogs,
            saveGameService: saveGameService,
            fileSystem: fileSystem,
            processLauncher: processLauncher);
        vm.Refresh(Settings(), workshopMods, loadedMods);
        return (vm, types, dialogs);
    }

    private static void SelectSave(MapTypesViewModel vm, string name) =>
        vm.SelectedSave = vm.SaveNames.First(entry => entry.Name == name && !entry.IsOrphaned);

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
        var dialogs = new FakeDialogs { ConfirmResult = false, SelectedFiles = new[] { new TypeFileSelection(hardcore, TypesFileRole.Types) } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

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
        var dialogs = new FakeDialogs { ConfirmResult = true, SelectedFiles = new[] { new TypeFileSelection(hardcore, TypesFileRole.Types) } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

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
        var dialogs = new FakeDialogs { ConfirmResult = true, SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(1, fakeTypes.ConfigureModCalls);
    }

    [Fact]
    public void ConfigureMod_Cancels_WhenSameFileReSelected()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs { ConfirmResult = false, SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) } };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(0, fakeTypes.ConfigureModCalls);
    }

    [Fact]
    public void ConfigureMod_PreSelectsOnlyActiveFilesInPicker()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        const string hardcore = @"D:\workshop\@CF\hardcore_types.xml";
        var config = ConfigWithEntry(new ModTypesEntry
        {
            ModName = "@CF",
            SourceFiles = { "casual_types.xml" },
            GeneratedFiles = { @"db\ModTypes\CF_casual_types.xml" },
        });
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual, hardcore } };
        var dialogs = new FakeDialogs { SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) } };

        (MapTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.NotNull(fakeDialogs.LastActiveFiles);
        Assert.Contains(casual, fakeDialogs.LastActiveFiles);
        Assert.DoesNotContain(hardcore, fakeDialogs.LastActiveFiles);
    }

    [Fact]
    public void ConfigureMod_PassesModFolderPathToPicker()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs { SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) } };

        (MapTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.Equal(Path.Combine(@"D:\workshop", "@CF"), fakeDialogs.LastModFolderPath);
    }

    [Fact]
    public void RefreshModNames_LeavesSelectionEmpty()
    {
        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), new FakeDialogs());

        Assert.Null(vm.SelectedMod);
        Assert.True(vm.IsModSelectionEmpty);
    }

    [Fact]
    public async Task SelectingMod_TriggersConfigure_AndResetsToNone()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs
        {
            ConfirmResult = true,
            SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) },
        };

        (MapTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(config, types, dialogs);
        Assert.Null(vm.SelectedMod);

        vm.SelectedMod = "@CF";
        await WaitUntilAsync(() => fakeTypes.ConfigureModCalls == 1);

        Assert.Equal(1, fakeTypes.ConfigureModCalls);
        Assert.Null(vm.SelectedMod);
        Assert.True(vm.IsModSelectionEmpty);
    }

    [Fact]
    public async Task SelectingMod_WhenTypesLocked_DoesNotConfigure_AndResetsToNone()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs
        {
            ConfirmResult = true,
            SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) },
        };
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Path.Combine(ServerPath, "mpmissions", MapName, "storage_1"));

        (MapTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(config, types, dialogs, fileSystem: fs);
        Assert.False(vm.TypesEditingAllowed);

        vm.SelectedMod = "@CF";
        await WaitUntilAsync(() => vm.SelectedMod is null);

        Assert.Equal(0, fakeTypes.ConfigureModCalls);
        Assert.Null(vm.SelectedMod);
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

    private static string ModTypesFolderPath() =>
        PresetModTypesFolder(MapName);

    [Fact]
    public void Sync_AddsUntrackedRows_ForFilesInModTypesNotInConfig()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ModTypesFolderPath()}\Orphan_types.xml");

        (MapTypesViewModel vm, _, _) = CreateTypesVm(config, new FakeTypesService(), new FakeDialogs(), fileSystem: fs);

        Assert.Contains(vm.TypesRows, r => !r.IsUntracked && r.ModName == "@CF" && r.FileName == "CF_types.xml");
        TypesRowViewModel orphan = Assert.Single(vm.TypesRows, r => r.IsUntracked);
        Assert.Equal("(untracked)", orphan.ModName);
        Assert.Equal("Orphan_types.xml", orphan.FileName);
    }

    [Fact]
    public void Sync_PopulatesFileTypeColumn_FromConfiguredRole()
    {
        var config = ConfigWithEntry(new ModTypesEntry
        {
            ModName = "@CF",
            GeneratedFiles = { @"db\ModTypes\CF_types.xml", @"db\ModTypes\CF_spawn.xml" },
            FileRoles = { { "CF_types.xml", "types" }, { "CF_spawn.xml", "spawnabletypes" } },
        });
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ModTypesFolderPath()}\Orphan_types.xml");

        (MapTypesViewModel vm, _, _) = CreateTypesVm(config, new FakeTypesService(), new FakeDialogs(), fileSystem: fs);

        Assert.Equal("type", vm.TypesRows.Single(r => r.FileName == "CF_types.xml").FileType);
        Assert.Equal("spawnable", vm.TypesRows.Single(r => r.FileName == "CF_spawn.xml").FileType);
        Assert.Equal(string.Empty, vm.TypesRows.Single(r => r.IsUntracked).FileType);
    }

    [Fact]
    public async Task RemoveSelected_DeletesUntrackedRows_WithoutTouchingTrackedFiles()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ModTypesFolderPath()}\Orphan_types.xml");

        (MapTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(config, types, dialogs, fileSystem: fs);

        TypesRowViewModel orphan = vm.TypesRows.Single(r => r.IsUntracked);
        vm.SelectedTypesRows.Add(orphan);

        vm.RemoveSelectedCommand.Execute(null);
        await WaitUntilAsync(() => fakeTypes.RemoveUntrackedCalls == 1);

        Assert.Equal(1, fakeTypes.RemoveUntrackedCalls);
        Assert.Equal(0, fakeTypes.RemoveFilesCalls);
        Assert.Contains("Orphan_types.xml", fakeTypes.LastUntrackedLeaves!);
    }

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
        SelectSave(vm, "Alpha");

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
        SelectSave(vm, "Alpha");

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.Equal(1, saves.LoadSaveCalls);
        Assert.Equal("Alpha", saves.LastLoadName);
    }

    [Fact]
    public async Task WipeWorld_AsksConfirmation_AndCallsService()
    {
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var saves = new FakeSaveGameService();

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

        vm.WipeWorldCommand.Execute(null);
        Assert.Equal(0, saves.WipeWorldCalls);

        dialogs.ConfirmResult = true;
        vm.WipeWorldCommand.Execute(null);
        await WaitUntilAsync(() => saves.WipeWorldCalls == 1);
        Assert.Equal(1, saves.WipeWorldCalls);
    }

    [Fact]
    public async Task IsBusy_IsTrueWhileASaveOperationRuns()
    {
        var saves = new FakeSaveGameService();
        var gate = new TaskCompletionSource();
        saves.WipeWorldGate = gate;

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(), saveGameService: saves);

        Assert.False(vm.IsBusy);

        vm.WipeWorldCommand.Execute(null);
        await WaitUntilAsync(() => saves.WipeWorldCalls == 1);

        // Start Server relies on IsBusy to avoid launching while a save operation
        // is still mutating (or deleting) the live storage folder.
        Assert.True(vm.IsSaveBusy);
        Assert.True(vm.IsBusy);

        gate.SetResult();
        await WaitUntilAsync(() => !vm.IsBusy);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task DeleteSave_AsksConfirmation_AndDeletesSelected()
    {
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        SelectSave(vm, "Alpha");

        vm.DeleteSaveCommand.Execute(null);
        Assert.Equal(0, saves.DeleteSaveCalls);

        dialogs.ConfirmResult = true;
        vm.DeleteSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.DeleteSaveCalls == 1);
        Assert.Equal(1, saves.DeleteSaveCalls);
        Assert.Equal("Alpha", saves.LastDeleteName);
    }

    [Fact]
    public async Task AddSave_PassesPresetSavesFolderAndInstanceId()
    {
        var dialogs = new FakeDialogs { AskTextResult = "First" };
        var saves = new FakeSaveGameService();

        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

        vm.AddSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.AddSaveCalls == 1);

        Assert.Equal(PresetSavesFolder(MapName), saves.LastAddSavesFolder);
        Assert.Equal(1, saves.LastInstanceId);
    }

    [Fact]
    public async Task LoadSave_PassesPresetSavesFolderAndInstanceId()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        SelectSave(vm, "Alpha");

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.Equal(PresetSavesFolder(MapName), saves.LastLoadSavesFolder);
        Assert.Equal(1, saves.LastInstanceId);
    }

    [Fact]
    public async Task LoadSave_DoesNotChangeTheConfiguredTypesOrEconomy()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };
        var types = new FakeTypesService();
        var store = new FakeTypesConfigStore();
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            config, types, dialogs, saveGameService: saves, typesConfigStore: store);
        SelectSave(vm, "Alpha");

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        // A save only restores world state; the preset's types/economy are untouched.
        Assert.Equal(1, saves.LoadSaveCalls);
        Assert.False(store.SaveCalled);
        Assert.Equal(0, types.SyncEconomyCoreCalls);
        Assert.Contains(config.Maps[MapName].Mods.Single().GeneratedFiles, f => f.EndsWith("CF_types.xml"));
    }

    [Fact]
    public async Task WipeWorld_UsesThePresetInstanceId_AndLeavesTypesAlone()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService();
        var types = new FakeTypesService();

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs, saveGameService: saves);

        vm.WipeWorldCommand.Execute(null);
        await WaitUntilAsync(() => saves.WipeWorldCalls == 1);

        Assert.Equal(1, saves.LastInstanceId);
        Assert.Equal(0, types.SyncEconomyCoreCalls);
    }

    [Fact]
    public void TypesEditingEnabled_WhenNoWorldExists()
    {
        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), new FakeDialogs());

        Assert.True(vm.TypesEditingAllowed);
        Assert.False(vm.IsTypesLocked);
        Assert.True(vm.ConfigureModCommand.CanExecute("@CF"));
        Assert.True(vm.OpenModTypesFolderCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty(vm.TypesLockedMessage));
        Assert.Equal("Open ModTypes folder in File Explorer", vm.OpenModTypesFolderToolTip);
    }

    [Fact]
    public void TypesEditingDisabled_WhenWorldExists()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Path.Combine(ServerPath, "mpmissions", MapName, "storage_1"));
        var types = new FakeTypesService();
        var launcher = new FakeProcessLauncher();

        (MapTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(
            AppliedConfig(), types, new FakeDialogs(), fileSystem: fs, processLauncher: launcher);

        Assert.False(vm.TypesEditingAllowed);
        Assert.True(vm.IsTypesLocked);
        Assert.False(vm.ConfigureModCommand.CanExecute("@CF"));
        Assert.False(vm.OpenModTypesFolderCommand.CanExecute(null));
        Assert.False(vm.CleanInvalidCommand.CanExecute(null));
        Assert.False(string.IsNullOrEmpty(vm.TypesLockedMessage));
        Assert.Equal(vm.TypesLockedMessage, vm.OpenModTypesFolderToolTip);

        vm.ConfigureModCommand.Execute("@CF");
        Assert.Equal(0, fakeTypes.ConfigureModCalls);

        // The disabled folder button must not open anything.
        vm.OpenModTypesFolderCommand.Execute(null);
        Assert.Null(launcher.LastOpenedFolder);
    }

    [Fact]
    public void OpenModTypesFolderCommand_CreatesFolderAndOpensIt()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();
        string expected = PresetModTypesFolder(MapName);

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            fileSystem: fs, processLauncher: launcher);

        vm.OpenModTypesFolderCommand.Execute(null);

        Assert.True(fs.DirectoryExists(expected));
        Assert.Equal(expected, launcher.LastOpenedFolder);
    }

    [Fact]
    public void OpenMapProfilesFolderCommand_WithCurrentMap_CreatesFolderAndOpensIt()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();
        string expected = PresetProfilesFolder(MapName);

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            fileSystem: fs, processLauncher: launcher);

        vm.OpenMapProfilesFolderCommand.Execute(null);

        Assert.True(fs.DirectoryExists(expected));
        Assert.Equal(expected, launcher.LastOpenedFolder);
    }

    [Fact]
    public void OpenMapProfilesFolderCommand_WithNoCurrentMap_OpensPresetsRoot()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();
        string expected = Path.Combine(@"D:\data", "Presets");

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            new TypesConfig(), new FakeTypesService(), new FakeDialogs(),
            fileSystem: fs, processLauncher: launcher);

        vm.OpenMapProfilesFolderCommand.Execute(null);

        Assert.True(fs.DirectoryExists(expected));
        Assert.Equal(expected, launcher.LastOpenedFolder);
    }

    [Fact]
    public void OpenMapProfilesFolderCommand_IsAlwaysEnabled()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Path.Combine(ServerPath, "mpmissions", MapName, "storage_1"));

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(), fileSystem: fs);

        // Unlike the ModTypes button, a world present must not disable it.
        Assert.False(vm.TypesEditingAllowed);
        Assert.True(vm.OpenMapProfilesFolderCommand.CanExecute(null));
    }

    [Fact]
    public void OpenMapProfilesFolderCommand_WithNoServerPath_DoesNotOpen()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();

        MapTypesViewModel vm = Create(new TypesConfig(), fileSystem: fs, processLauncher: launcher);
        // No Refresh -> no server path applied.

        vm.OpenMapProfilesFolderCommand.Execute(null);

        Assert.Null(launcher.LastOpenedFolder);
    }

    [Fact]
    public async Task LoadSave_ConfirmsOnce_AndDoesNotWarnAboutConfiguration()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        SelectSave(vm, "Alpha");

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Equal(0, dialogs.ConfirmWithWarningCalls);
        Assert.Equal(0, dialogs.ShowWarningCalls);
        Assert.Equal(1, saves.LoadSaveCalls);
    }

    [Fact]
    public void AddSave_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var saves = new FakeSaveGameService();
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), dialogs: dialogs, saveGameService: saves, serverProcess: process);

        vm.AddSaveCommand.Execute(null);

        Assert.Equal(0, saves.AddSaveCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void LoadSave_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), dialogs: dialogs, saveGameService: saves, serverProcess: process);
        vm.SelectedSave = new SaveListEntryViewModel("Alpha", isOrphaned: false);

        vm.LoadSaveCommand.Execute(null);

        Assert.Equal(0, saves.LoadSaveCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void WipeWorld_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var saves = new FakeSaveGameService();
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), dialogs: dialogs, saveGameService: saves, serverProcess: process);

        vm.WipeWorldCommand.Execute(null);

        Assert.Equal(0, saves.WipeWorldCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void ConfigureMod_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService();
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.Equal(0, types.ConfigureModCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void RemoveSelected_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService();
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);
        vm.SelectedTypesRows.Add(new TypesRowViewModel("@CF", "CF_types.xml", isInactive: false));

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Equal(0, types.RemoveFilesCalls);
        Assert.Equal(0, dialogs.ConfirmCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void CleanInvalid_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService();
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);

        vm.CleanInvalidCommand.Execute(null);

        Assert.Equal(0, types.CleanInvalidCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task MapSwitch_Blocked_WhenServerRunning()
    {
        const string other = "dayzOffline.enoch";
        var config = new TypesConfig { CurrentMap = MapName };
        var dialogs = new FakeDialogs();
        var process = new FakeServerProcessState { Running = true };
        var store = new FakeTypesConfigStore();
        var mapService = new FakeMapService(
            new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"),
            new MapInfo(other, $@"{ServerPath}\mpmissions\{other}"));

        MapTypesViewModel vm = Create(config, mapService: mapService, typesConfigStore: store, dialogs: dialogs, serverProcess: process);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        vm.SelectedMap = other;
        await WaitUntilAsync(() => dialogs.LastErrorMessage is not null);

        Assert.Contains("server is running", dialogs.LastErrorMessage);
        // The switch was refused: the applied map, the dropdown and the persisted
        // config must all stay on the original map.
        Assert.Equal(MapName, config.CurrentMap);
        Assert.Equal(MapName, vm.SelectedMap);
        Assert.False(store.SaveCalled);
    }

    [Fact]
    public void Sync_BuildsPresetList_DefaultFirst()
    {
        var config = AppliedConfig();
        var presets = new FakePresetService();
        presets.PresetNames.Add("Hardcore");
        MapTypesViewModel vm = Create(config, presetService: presets);

        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        Assert.Equal(new[] { PresetPaths.DefaultPresetName, "Hardcore" }, vm.Presets.Select(p => p.Name));
        Assert.True(vm.Presets[0].IsDefault);
        Assert.Equal(PresetPaths.DefaultPresetName, vm.SelectedPreset!.Name);
    }

    [Fact]
    public async Task SelectingPreset_ActivatesIt()
    {
        var config = AppliedConfig();
        var presets = new FakePresetService();
        presets.PresetNames.Add("Hardcore");
        MapTypesViewModel vm = Create(config, presetService: presets);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        string? activatedMap = null;
        string? activatedPreset = null;
        vm.ActivatePreset = (map, preset) =>
        {
            activatedMap = map;
            activatedPreset = preset;
            vm.ActivePresetName = preset;
            return Task.FromResult(true);
        };

        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Hardcore");
        await WaitUntilAsync(() => activatedPreset is not null);

        Assert.Equal(MapName, activatedMap);
        Assert.Equal("Hardcore", activatedPreset);
        Assert.Equal("Hardcore", vm.ActivePresetName);
    }

    [Fact]
    public async Task AddPreset_CreatesWithCopyProfiles_AndActivates()
    {
        var config = AppliedConfig();
        var presets = new FakePresetService();
        var dialogs = new FakeDialogs { AskAddPresetResult = new AddPresetRequest("Hardcore", true) };
        MapTypesViewModel vm = Create(config, presetService: presets, dialogs: dialogs);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        string? activated = null;
        vm.ActivatePreset = (_, preset) =>
        {
            activated = preset;
            vm.ActivePresetName = preset;
            return Task.FromResult(true);
        };

        vm.AddPresetCommand.Execute(null);
        await WaitUntilAsync(() => activated is not null);

        Assert.Contains(("Hardcore", true), presets.Created);
        Assert.Contains(vm.Presets, p => p.Name == "Hardcore");
        Assert.Equal("Hardcore", activated);
    }

    [Fact]
    public async Task DuplicatePreset_CopiesCurrentPreset_AndActivates()
    {
        var config = AppliedConfig();
        var presets = new FakePresetService();
        var dialogs = new FakeDialogs { AskDuplicatePresetResult = new DuplicatePresetRequest("HardcoreCopy", true) };
        MapTypesViewModel vm = Create(config, presetService: presets, dialogs: dialogs);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        // The freshly refreshed selection is the reserved default preset.
        Assert.True(vm.SelectedPreset!.IsDefault);

        string? activated = null;
        vm.ActivatePreset = (_, preset) =>
        {
            activated = preset;
            vm.ActivePresetName = preset;
            return Task.FromResult(true);
        };

        vm.DuplicatePresetCommand.Execute(null);
        await WaitUntilAsync(() => activated is not null);

        Assert.Contains((PresetPaths.DefaultPresetName, "HardcoreCopy", true), presets.Duplicated);
        Assert.Contains(vm.Presets, p => p.Name == "HardcoreCopy");
        Assert.Equal("HardcoreCopy", activated);
    }

    [Fact]
    public void DeletePreset_RefusesDefault()
    {
        var config = AppliedConfig();
        MapTypesViewModel vm = Create(config);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        vm.SelectedPreset = vm.Presets.First(p => p.IsDefault);

        Assert.False(vm.DeletePresetCommand.CanExecute(null));
        Assert.False(vm.RenamePresetCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeletePreset_RemovesUserPreset()
    {
        var config = AppliedConfig();
        var presets = new FakePresetService();
        MapTypesViewModel vm = Create(config, presetService: presets);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });
        presets.CreatePreset(ServerPath, @"D:\data", MapName, "Hardcore", false);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        vm.ActivatePreset = (_, preset) => { vm.ActivePresetName = preset; return Task.FromResult(true); };
        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Hardcore");
        await WaitUntilAsync(() => string.Equals(vm.ActivePresetName, "Hardcore", StringComparison.Ordinal));

        vm.DeletePresetCommand.Execute(null);

        Assert.Contains("Hardcore", presets.Deleted);
        Assert.DoesNotContain(vm.Presets, p => p.Name == "Hardcore");
    }

    [Fact]
    public async Task DeletePreset_RemovesItsJunctionFolder()
    {
        var config = AppliedConfig();
        var presets = new FakePresetService();
        var junctions = new FakeJunctionService();
        var dialogs = new FakeDialogs { ConfirmResult = true };
        MapTypesViewModel vm = Create(config, presetService: presets, junctionService: junctions, dialogs: dialogs);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });
        presets.CreatePreset(ServerPath, @"D:\data", MapName, "Hardcore", false);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        vm.ActivatePreset = (_, preset) => { vm.ActivePresetName = preset; return Task.FromResult(true); };
        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Hardcore");
        await WaitUntilAsync(() => string.Equals(vm.ActivePresetName, "Hardcore", StringComparison.Ordinal));

        vm.DeletePresetCommand.Execute(null);

        Assert.Contains((ServerPath, "1"), junctions.DeletedFolders);
    }

    [Fact]
    public async Task RenameSave_InvokesSaveService()
    {
        var dialogs = new FakeDialogs { AskTextResult = "Beta" };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };
        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        SelectSave(vm, "Alpha");

        vm.RenameSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.RenameSaveCalls == 1);

        Assert.Equal(PresetSavesFolder(MapName), saves.LastRenameSavesFolder);
        Assert.Equal("Alpha", saves.LastRenameOldName);
        Assert.Equal("Beta", saves.LastRenameNewName);
    }

    [Fact]
    public void RefreshSaves_AppendsOrphanStorageEntries()
    {
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            StorageInstanceIds = new[] { 1, 5 },
        };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(), saveGameService: saves);

        Assert.Equal(new[] { "Alpha", "storage_5" }, vm.SaveNames.Select(entry => entry.Name));
        Assert.False(vm.SaveNames.Single(entry => entry.Name == "Alpha").IsOrphaned);

        SaveListEntryViewModel orphan = vm.SaveNames.Single(entry => entry.Name == "storage_5");
        Assert.True(orphan.IsOrphaned);
        Assert.Equal(5, orphan.InstanceId);
        Assert.Equal("storage_5 (orphaned)", orphan.DisplayName);
    }

    [Fact]
    public void OrphanEntry_DisablesLoadAndRename_ButAllowsDelete()
    {
        var saves = new FakeSaveGameService { StorageInstanceIds = new[] { 5 } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(), saveGameService: saves);

        vm.SelectedSave = vm.SaveNames.Single(entry => entry.IsOrphaned);

        Assert.False(vm.LoadSaveCommand.CanExecute(null));
        Assert.False(vm.RenameSaveCommand.CanExecute(null));
        Assert.True(vm.DeleteSaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteSave_OnOrphan_DeletesStorage()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService { StorageInstanceIds = new[] { 5 } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

        vm.SelectedSave = vm.SaveNames.Single(entry => entry.IsOrphaned);

        vm.DeleteSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.DeleteStorageCalls == 1);

        Assert.Equal(5, saves.LastDeleteStorageInstanceId);
        Assert.Equal(0, saves.DeleteSaveCalls);
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

        public IReadOnlyList<TypeFileSelection>? ConfigureModSelections { get; private set; }

        public int ConfigureModCalls { get; private set; }

        public IReadOnlySet<string>? LastRemoveLeaves { get; private set; }

        public int RemoveFilesCalls { get; private set; }

        public IReadOnlySet<string>? LastCleanValidMods { get; private set; }

        public int CleanInvalidCalls { get; private set; }

        public int RemoveUntrackedCalls { get; private set; }

        public IReadOnlySet<string>? LastUntrackedLeaves { get; private set; }

        public IReadOnlyList<string> DiscoverXmlFiles(string workshopPath, string modName) => DiscoveryFiles;

        public IReadOnlyList<ConfiguredTypeFile> GetConfiguredFiles(TypesConfig config, string mapName, string modName)
        {
            var files = new List<ConfiguredTypeFile>();
            if (!config.Maps.TryGetValue(mapName, out MapTypesConfig? map))
            {
                return files;
            }

            ModTypesEntry? entry = map.Mods.FirstOrDefault(m =>
                string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return files;
            }

            for (int i = 0; i < entry.GeneratedFiles.Count; i++)
            {
                string generated = entry.GeneratedFiles[i];
                string leaf = Path.GetFileName(generated);
                if (string.IsNullOrEmpty(leaf))
                {
                    continue;
                }

                string source = i < entry.SourceFiles.Count ? entry.SourceFiles[i] : string.Empty;
                TypesFileRole role = entry.FileRoles.TryGetValue(leaf, out string? stored)
                    ? TypesFileRoles.ToRole(stored)
                    : TypesFileRole.Types;
                files.Add(new ConfiguredTypeFile(source, leaf, role));
            }

            return files;
        }

        public TypesOperationResult ConfigureMod(
            TypesConfig config, string mapName, TypesTarget target, string workshopPath, string modName,
            IReadOnlyList<TypeFileSelection> selections, IReadOnlySet<string> loadedModNames)
        {
            ConfigureModCalls++;
            ConfigureModSelections = selections.ToList();
            ConfigureModSourceFiles = selections.Select(s => s.SourceFile).ToList();
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult RemoveFiles(
            TypesConfig config, string mapName, TypesTarget target, string modName,
            IReadOnlySet<string> fileLeaves, IReadOnlySet<string> loadedModNames)
        {
            RemoveFilesCalls++;
            LastRemoveLeaves = fileLeaves;
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult CleanInvalid(
            TypesConfig config, string mapName, TypesTarget target,
            IReadOnlySet<string> validModNames, IReadOnlySet<string> loadedModNames)
        {
            CleanInvalidCalls++;
            LastCleanValidMods = validModNames;
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesOperationResult RemoveUntrackedFiles(
            TypesConfig config, string mapName, TypesTarget target, IReadOnlySet<string> fileLeaves)
        {
            RemoveUntrackedCalls++;
            LastUntrackedLeaves = fileLeaves;
            LastTarget = target;
            return new TypesOperationResult { Success = true };
        }

        public TypesTarget? LastTarget { get; private set; }

        public int SyncEconomyCoreCalls { get; private set; }

        public IReadOnlySet<string>? LastPreviouslyOwned { get; private set; }

        public string? LastEconomyFolder { get; private set; }

        public MapTypesConfig? LastEconomyMap { get; private set; }

        public bool SyncEconomyCore(MapTypesConfig? map, string missionPath, string folder, IReadOnlySet<string> loadedModNames, IReadOnlySet<string>? previouslyOwned = null)
        {
            SyncEconomyCoreCalls++;
            LastEconomyMap = map;
            LastEconomyFolder = folder;
            LastPreviouslyOwned = previouslyOwned;
            return true;
        }

        public string? ActiveTypesFolder { get; set; }

        public string? GetActiveTypesFolder(string missionPath) => ActiveTypesFolder;

        public IReadOnlyList<string> ActiveTypeFiles { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> GetActiveTypeFileNames(
            TypesConfig config, string mapName, IReadOnlySet<string> loadedModNames) => ActiveTypeFiles;
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

        public bool WriteInstanceId(string serverConfigPath, int instanceId) => true;
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public string? LastOpenedFolder { get; private set; }

        public void Launch(string filePath, string workingDirectory) { }

        public void OpenFolder(string path) => LastOpenedFolder = path;
    }

    private sealed class FakeBatchFileService : IBatchFileService
    {
        public bool HasModListLine(string batFilePath) => true;

        public bool WriteModList(string batFilePath, IReadOnlyList<string> modNames) => true;

        public string? LastServerProfile { get; private set; }

        public bool WriteServerProfile(string batFilePath, string relativeProfile)
        {
            LastServerProfile = relativeProfile;
            return true;
        }

        public string? LastServerConfig { get; private set; }

        public bool WriteServerConfig(string batFilePath, string serverConfigPath)
        {
            LastServerConfig = serverConfigPath;
            return true;
        }
    }

    private sealed class FakeSaveGameService : ISaveGameService
    {
        public IReadOnlyList<string> StoredSaves { get; set; } = Array.Empty<string>();

        public int AddSaveCalls { get; private set; }

        public string? LastAddName { get; private set; }

        public bool LastAddOverwrite { get; private set; }

        public string? LastAddSavesFolder { get; private set; }

        public int LoadSaveCalls { get; private set; }

        public string? LastLoadName { get; private set; }

        public string? LastLoadSavesFolder { get; private set; }

        public int WipeWorldCalls { get; private set; }

        /// <summary>When set, WipeWorld blocks on this gate so tests can observe the in-flight state.</summary>
        public TaskCompletionSource? WipeWorldGate { get; set; }

        public int DeleteSaveCalls { get; private set; }

        public string? LastDeleteName { get; private set; }

        public string? LastDeleteSavesFolder { get; private set; }

        public int LastInstanceId { get; private set; }

        public string GetStorageFolderPath(string serverPath, string mapName, int instanceId) =>
            Path.Combine(serverPath, "mpmissions", mapName, $"storage_{instanceId}");

        public IReadOnlyList<int> StorageInstanceIds { get; set; } = Array.Empty<int>();

        public int DeleteStorageCalls { get; private set; }

        public int LastDeleteStorageInstanceId { get; private set; }

        public IReadOnlyList<int> ListStorageInstanceIds(string serverPath, string mapName) => StorageInstanceIds;

        public SaveGameResult DeleteStorage(string serverPath, string mapName, int instanceId)
        {
            DeleteStorageCalls++;
            LastDeleteStorageInstanceId = instanceId;
            return new SaveGameResult { Success = true, Message = $"Deleted storage_{instanceId}." };
        }

        public IReadOnlyList<string> ListSaves(string savesFolder) => StoredSaves;

        public string GetSaveFolderPath(string savesFolder, string saveName) =>
            Path.Combine(savesFolder, saveName);

        public SaveGameResult AddSave(
            string serverPath, string mapName, string savesFolder, int instanceId, string saveName, bool overwrite)
        {
            AddSaveCalls++;
            LastAddName = saveName;
            LastAddOverwrite = overwrite;
            LastAddSavesFolder = savesFolder;
            LastInstanceId = instanceId;
            return new SaveGameResult { Success = true, Message = $"Saved \"{saveName}\"." };
        }

        public SaveGameResult LoadSave(
            string serverPath, string mapName, string savesFolder, int instanceId, string saveName)
        {
            LoadSaveCalls++;
            LastLoadName = saveName;
            LastLoadSavesFolder = savesFolder;
            LastInstanceId = instanceId;
            return new SaveGameResult { Success = true, Message = $"Loaded \"{saveName}\"." };
        }

        public ConfigLoadResult<SaveMetaData> GetMeta(string savesFolder, string saveName) =>
            ConfigLoadResult<SaveMetaData>.Missing();

        public SaveGameResult WipeWorld(string serverPath, string mapName, int instanceId)
        {
            WipeWorldCalls++;
            LastInstanceId = instanceId;
            WipeWorldGate?.Task.GetAwaiter().GetResult();
            return new SaveGameResult { Success = true, Message = "World wiped." };
        }

        public SaveGameResult DeleteSave(string savesFolder, string saveName)
        {
            DeleteSaveCalls++;
            LastDeleteName = saveName;
            LastDeleteSavesFolder = savesFolder;
            return new SaveGameResult { Success = true, Message = $"Deleted \"{saveName}\"." };
        }

        public int RenameSaveCalls { get; private set; }

        public string? LastRenameSavesFolder { get; private set; }

        public string? LastRenameOldName { get; private set; }

        public string? LastRenameNewName { get; private set; }

        public SaveGameResult RenameSave(string savesFolder, string saveName, string newName)
        {
            RenameSaveCalls++;
            LastRenameSavesFolder = savesFolder;
            LastRenameOldName = saveName;
            LastRenameNewName = newName;
            return new SaveGameResult { Success = true, Message = $"Renamed \"{saveName}\" to \"{newName}\"." };
        }
    }

    private sealed class FakePresetService : IPresetService
    {
        public List<string> PresetNames { get; } = new() { PresetPaths.DefaultPresetName };

        public List<(string Name, bool CopyProfiles)> Created { get; } = new();

        public List<(string Source, string NewName, bool CopyProfiles)> Duplicated { get; } = new();

        public List<(string OldName, string NewName)> Renamed { get; } = new();

        public List<string> Deleted { get; } = new();

        public bool IsDefaultPreset(string? presetName) => PresetPaths.IsDefaultPreset(presetName);

        public string GetPresetFolder(string dataDirectory, string mapName, string presetName) =>
            PresetPaths.PresetFolder(dataDirectory, mapName, presetName);

        public bool PresetExists(string dataDirectory, string mapName, string presetName) =>
            PresetNames.Any(n => string.Equals(n, presetName, StringComparison.Ordinal));

        public IReadOnlyList<string> ListPresetNames(string dataDirectory, string mapName) =>
            PresetNames.ToList();

        public int ReadInstanceId(string dataDirectory, string mapName, string presetName) => 1;

        public int AllocateInstanceId(string dataDirectory) => 1;

        public PresetResult EnsureDefaultPreset(string serverPath, string dataDirectory, string mapName)
        {
            if (!PresetNames.Any(PresetPaths.IsDefaultPreset))
            {
                PresetNames.Insert(0, PresetPaths.DefaultPresetName);
            }

            return new() { Success = true };
        }

        public PresetResult CreatePreset(
            string serverPath, string dataDirectory, string mapName, string presetName, bool copyProfilesFromDefault)
        {
            if (PresetNames.Any(n => string.Equals(n, presetName, StringComparison.OrdinalIgnoreCase)))
            {
                return new() { Success = false, Message = $"A preset named \"{presetName}\" already exists." };
            }

            PresetNames.Add(presetName);
            Created.Add((presetName, copyProfilesFromDefault));
            return new() { Success = true, Message = $"Created \"{presetName}\"." };
        }

        public PresetResult DuplicatePreset(
            string serverPath, string dataDirectory, string mapName,
            string sourcePresetName, string newPresetName, bool copyProfiles)
        {
            if (PresetNames.Any(n => string.Equals(n, newPresetName, StringComparison.OrdinalIgnoreCase)))
            {
                return new() { Success = false, Message = $"A preset named \"{newPresetName}\" already exists." };
            }

            PresetNames.Add(newPresetName);
            Duplicated.Add((sourcePresetName, newPresetName, copyProfiles));
            return new() { Success = true, Message = $"Duplicated \"{sourcePresetName}\" to \"{newPresetName}\"." };
        }

        public PresetResult RenamePreset(string dataDirectory, string mapName, string presetName, string newName)
        {
            int index = PresetNames.FindIndex(n => string.Equals(n, presetName, StringComparison.Ordinal));
            if (index < 0)
            {
                return new() { Success = false, Message = "missing" };
            }

            PresetNames[index] = newName;
            Renamed.Add((presetName, newName));
            return new() { Success = true };
        }

        public PresetResult DeletePreset(string dataDirectory, string mapName, string presetName)
        {
            PresetNames.RemoveAll(n => string.Equals(n, presetName, StringComparison.Ordinal));
            Deleted.Add(presetName);
            return new() { Success = true };
        }
    }

    private sealed class FakeJunctionService : IJunctionService
    {
        public List<(string ServerPath, string PresetKey)> DeletedFolders { get; } = new();

        public JunctionSyncResult Sync(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            new();

        public JunctionSyncResult PrepareLoaded(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            new();

        public JunctionSyncResult Finalize(string serverPath, string workshopPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            new();

        public IReadOnlyList<string> FindOrphanedJunctions(string serverPath, string presetKey, IReadOnlySet<string> validModNames) =>
            Array.Empty<string>();

        public IReadOnlyList<string> Verify(string serverPath, string presetKey, IReadOnlyList<string> loadedMods) =>
            Array.Empty<string>();

        public void DeleteJunctionFolder(string serverPath, string presetKey) =>
            DeletedFolders.Add((serverPath, presetKey));
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path) => _files.Add(path);

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();

        public bool FileExists(string path) => _files.Contains(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive)
        {
            string prefix = EnsureTrailingSeparator(path);
            return _files
                .Where(f => recursive
                    ? f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    : ParentOf(f).Equals(path, StringComparison.OrdinalIgnoreCase))
                .Where(f => searchPattern == "*" || Path.GetFileName(f).EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public void CopyFile(string sourcePath, string destinationPath) { }

        public void DeleteFile(string path) => _files.Remove(path);

        public string ReadAllText(string path) => string.Empty;

        public void WriteAllText(string path, string contents) { }

        public void CreateDirectory(string path) => _directories.Add(path);

        public void CopyDirectory(string sourcePath, string destinationPath) { }

        public void DeleteDirectory(string path, bool recursive) => _directories.Remove(path);

        public void MoveDirectory(string sourcePath, string destinationPath) { }

        private static string EnsureTrailingSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

        private static string ParentOf(string filePath) => Path.GetDirectoryName(filePath) ?? string.Empty;
    }

    private sealed class FakeDataDirectoryProvider : IDataDirectoryProvider
    {
        public string Current => @"D:\data";

        public void Initialize() { }

        public string Resolve(Settings settings) => Current;

        public void MoveTo(string directory, Settings settings) { }
    }

    private sealed class FakeServerProcessState : IDayZServerProcessState
    {
        public bool Running { get; set; }

        public bool IsDayZServerRunning() => Running;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public int ConfirmCalls { get; private set; }

        public string? LastConfirmMessage { get; private set; }

        public int ConfirmWithWarningCalls { get; private set; }

        public int ShowWarningCalls { get; private set; }

        public string? LastWarningMessage { get; private set; }

        public string? LastWarningNote { get; private set; }

        public IReadOnlyList<TypeFileSelection>? SelectedFiles { get; set; }

        public IReadOnlySet<string>? LastActiveFiles { get; private set; }

        public IReadOnlyDictionary<string, TypesFileRole>? LastActiveRoles { get; private set; }

        public string? LastModFolderPath { get; private set; }

        public string? AskTextResult { get; set; } = null;

        public string? LastErrorMessage { get; private set; }

        public string? LastErrorTitle { get; private set; }

        public void ShowMessage(string message, string title, bool isError = false)
        {
            if (isError)
            {
                LastErrorMessage = message;
                LastErrorTitle = title;
            }
        }

        public bool Confirm(string message, string title)
        {
            ConfirmCalls++;
            LastConfirmMessage = message;
            return ConfirmResult;
        }

        public bool ConfirmWithWarning(string message, string title, string warning, string note = "")
        {
            ConfirmWithWarningCalls++;
            LastWarningMessage = warning;
            LastWarningNote = note;
            return ConfirmResult;
        }

        public void ShowWarning(string message, string title, string warning, string note = "")
        {
            ShowWarningCalls++;
            LastWarningMessage = warning;
            LastWarningNote = note;
        }

        public string? AskText(string title, string prompt, string defaultValue = "") => AskTextResult;

        public string? PickFolder(string title = "Select a folder") => null;

        public string? PickFile(string title, string filter, string initialDirectory) => null;

        public IReadOnlyList<TypeFileSelection>? PickTypeFiles(
            string modName, IReadOnlyList<string> files, IReadOnlySet<string>? activeFiles = null,
            IReadOnlyDictionary<string, TypesFileRole>? activeRoles = null, string? modFolderPath = null)
        {
            LastActiveFiles = activeFiles;
            LastActiveRoles = activeRoles;
            LastModFolderPath = modFolderPath;
            return SelectedFiles;
        }

        public AddPresetRequest? AskAddPresetResult { get; set; }

        public AddPresetRequest? AskAddPreset(string mapName) => AskAddPresetResult;

        public DuplicatePresetRequest? AskDuplicatePresetResult { get; set; }

        public DuplicatePresetRequest? AskDuplicatePreset(string sourcePresetName) => AskDuplicatePresetResult;
    }
}
