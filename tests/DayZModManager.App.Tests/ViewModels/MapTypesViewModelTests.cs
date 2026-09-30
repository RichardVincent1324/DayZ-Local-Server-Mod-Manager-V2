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
        FakeSaveGameService? saveGameService = null,
        FakeServerProcessState? serverProcess = null,
        FakeFileSystem? fileSystem = null,
        FakeBatchFileService? batchFileService = null,
        FakeTypesBackupService? typesBackup = null,
        FakeProcessLauncher? processLauncher = null)
    {
        mapService ??= new FakeMapService(new MapInfo(MapName, $@"{ServerPath}\mpmissions\{MapName}"));
        typesConfigStore ??= new FakeTypesConfigStore();
        typesService ??= new FakeTypesService();
        dialogs ??= new FakeDialogs();
        saveGameService ??= new FakeSaveGameService();
        serverProcess ??= new FakeServerProcessState();
        fileSystem ??= new FakeFileSystem();
        batchFileService ??= new FakeBatchFileService();
        typesBackup ??= new FakeTypesBackupService();
        processLauncher ??= new FakeProcessLauncher();

        return new MapTypesViewModel(
            mapService,
            typesService,
            saveGameService,
            typesBackup,
            typesConfigStore,
            new FakeServerConfigService(),
            batchFileService,
            fileSystem,
            dialogs,
            new LogViewModel(),
            config,
            new FakeDataDirectoryProvider(),
            serverProcess,
            processLauncher);
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
    public void ApplyingAMap_NamesProfileFolderAfterMission()
    {
        var config = new TypesConfig();
        var batch = new FakeBatchFileService();
        var fs = new FakeFileSystem();

        MapTypesViewModel vm = Create(config, batchFileService: batch, fileSystem: fs);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(MapName, config.CurrentMap);

        // The profile folder mirrors the mpmissions mission folder exactly.
        Assert.Equal($@"map_profiles\{MapName}", batch.LastServerProfile);
        Assert.True(fs.DirectoryExists($@"{ServerPath}\map_profiles\{MapName}"));
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
        FakeTypesBackupService? typesBackup = null,
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
            typesBackup: typesBackup,
            processLauncher: processLauncher);
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
        var dialogs = new FakeDialogs { ConfirmResult = false, SelectedFiles = new[] { new TypeFileSelection(hardcore, TypesFileRole.Types) } };

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
        var dialogs = new FakeDialogs { ConfirmResult = true, SelectedFiles = new[] { new TypeFileSelection(hardcore, TypesFileRole.Types) } };

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
        var dialogs = new FakeDialogs { ConfirmResult = true, SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) } };

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
        var dialogs = new FakeDialogs { ConfirmResult = false, SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) } };

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
        var config = ConfigWithEntry(new ModTypesEntry
        {
            ModName = "@CF",
            SourceFiles = { "casual_types.xml" },
            GeneratedFiles = { @"db\ModTypes\CF_casual_types.xml" },
        });
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual, hardcore } };
        var dialogs = new FakeDialogs { SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) } };

        (MapTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigXmlCommand.Execute(null);

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

        vm.ConfigXmlCommand.Execute(null);

        Assert.Equal(Path.Combine(@"D:\workshop", "@CF"), fakeDialogs.LastModFolderPath);
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
        $@"{ServerPath}\mpmissions\{MapName}\db\ModTypes";

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
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmWithWarningCalls >= 1);
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
    public async Task IsBusy_IsTrueWhileASaveOperationRuns()
    {
        var saves = new FakeSaveGameService();
        var gate = new TaskCompletionSource();
        saves.NewGameGate = gate;

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(), saveGameService: saves);

        Assert.False(vm.IsBusy);

        vm.NewGameCommand.Execute(null);
        await WaitUntilAsync(() => saves.NewGameCalls == 1);

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
        vm.SelectedSave = "Alpha";

        vm.DeleteSaveCommand.Execute(null);
        Assert.Equal(0, saves.DeleteSaveCalls);

        dialogs.ConfirmResult = true;
        vm.DeleteSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.DeleteSaveCalls == 1);
        Assert.Equal(1, saves.DeleteSaveCalls);
        Assert.Equal("Alpha", saves.LastDeleteName);
    }

    [Fact]
    public async Task AddSave_CapturesConfigurationSnapshot()
    {
        var dialogs = new FakeDialogs { AskTextResult = "First" };
        var saves = new FakeSaveGameService();
        var types = new FakeTypesService { ActiveTypeFiles = new[] { "CF_types.xml" } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), types, dialogs, saveGameService: saves);

        vm.AddSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.AddSaveCalls == 1);

        Assert.NotNull(saves.LastAddMeta);
        Assert.Equal(MapName, saves.LastAddMeta.Map);
        Assert.Equal("storage_1", saves.LastAddMeta.StorageFolder);
        Assert.Equal(new[] { "@CF" }, saves.LastAddMeta.ModList);
        Assert.Equal(new[] { "CF_types.xml" }, saves.LastAddMeta.TypesFiles);
        Assert.NotEqual(default, saves.LastAddMeta.SavedAtUtc);
    }

    [Fact]
    public async Task AddSave_StoresTypesMappingSnapshot()
    {
        var dialogs = new FakeDialogs { AskTextResult = "First" };
        var saves = new FakeSaveGameService();
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));

        (MapTypesViewModel vm, _, _) = CreateTypesVm(config, new FakeTypesService(), dialogs, saveGameService: saves);

        vm.AddSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.AddSaveCalls == 1);

        Assert.NotNull(saves.LastAddMeta!.TypesConfig);
        ModTypesEntry entry = Assert.Single(saves.LastAddMeta.TypesConfig!.Mods);
        Assert.Equal("@CF", entry.ModName);
        Assert.Contains(entry.GeneratedFiles, file => file.EndsWith("CF_types.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadSave_WithTypesMapping_RewritesConfigAndSyncsEconomy()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var savedMapping = new MapTypesConfig { Mods = { Entry("@CF", @"db\ModTypes\saved_types.xml") } };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                ModList = new List<string> { "@CF" },
                TypesFiles = new List<string> { "saved_types.xml" },
                TypesConfig = savedMapping,
            }),
        };
        var types = new FakeTypesService();
        var store = new FakeTypesConfigStore();
        var backup = new FakeTypesBackupService();
        var config = AppliedConfig();

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            config, types, dialogs, saveGameService: saves, typesBackup: backup, typesConfigStore: store);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1 && types.SyncEconomyCoreCalls == 1);

        Assert.Equal(1, backup.EnsureCapturedCalls);
        Assert.True(store.SaveCalled);
        Assert.Same(savedMapping, config.Maps[MapName]);
        Assert.Equal(1, types.SyncEconomyCoreCalls);
    }

    [Fact]
    public async Task LoadSave_RemovesEconomyEntriesForFilesReplacedByTheSave()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var savedMapping = new MapTypesConfig { Mods = { Entry("@CF", @"db\ModTypes\saved_types.xml") } };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                ModList = new List<string> { "@CF" },
                TypesFiles = new List<string> { "saved_types.xml" },
                TypesConfig = savedMapping,
            }),
        };
        var types = new FakeTypesService();
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_old_types.xml"));

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            config, types, dialogs, saveGameService: saves,
            typesBackup: new FakeTypesBackupService(), typesConfigStore: new FakeTypesConfigStore());
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1 && types.SyncEconomyCoreCalls == 1);

        // CF_old_types.xml was owned before the load and deleted from disk by the
        // save swap, so its economy entry must be treated as stale/removable.
        Assert.NotNull(types.LastPreviouslyOwned);
        Assert.Contains("CF_old_types.xml", types.LastPreviouslyOwned!);
    }

    [Fact]
    public async Task LoadSave_WithoutTypesMapping_LeavesConfigUnchanged()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                ModList = new List<string> { "@CF" },
                TypesFiles = new List<string>(),
            }),
        };
        var types = new FakeTypesService();
        var store = new FakeTypesConfigStore();
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            config, types, dialogs, saveGameService: saves, typesConfigStore: store);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.False(store.SaveCalled);
        Assert.Equal(0, types.SyncEconomyCoreCalls);
        Assert.Contains(config.Maps[MapName].Mods, entry => entry.ModName == "@CF");
    }

    [Fact]
    public async Task NewGame_RestoresConfiguredTypesBackup()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService();
        var types = new FakeTypesService();
        var store = new FakeTypesConfigStore();
        var backup = new FakeTypesBackupService
        {
            RestoreConfig = new MapTypesConfig { Mods = { Entry("@CF", @"db\ModTypes\configured_types.xml") } },
        };
        var config = AppliedConfig();

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            config, types, dialogs, saveGameService: saves, typesBackup: backup, typesConfigStore: store);

        vm.NewGameCommand.Execute(null);
        await WaitUntilAsync(() => saves.NewGameCalls == 1 && types.SyncEconomyCoreCalls == 1);

        Assert.Equal(1, backup.RestoreCalls);
        Assert.True(store.SaveCalled);
        Assert.Contains(config.Maps[MapName].Mods, entry => entry.GeneratedFiles.Contains(@"db\ModTypes\configured_types.xml"));
    }

    [Fact]
    public async Task NewGame_WithoutBackup_LeavesConfigUnchanged()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService();
        var types = new FakeTypesService();
        var store = new FakeTypesConfigStore();
        var backup = new FakeTypesBackupService { RestoreConfig = null };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs, saveGameService: saves, typesBackup: backup, typesConfigStore: store);

        vm.NewGameCommand.Execute(null);
        await WaitUntilAsync(() => backup.RestoreCalls == 1);

        Assert.False(store.SaveCalled);
        Assert.Equal(0, types.SyncEconomyCoreCalls);
    }

    [Fact]
    public void TypesEditingEnabled_WhenNoWorldExists()
    {
        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), new FakeDialogs());

        Assert.True(vm.TypesEditingAllowed);
        Assert.False(vm.IsTypesLocked);
        Assert.True(vm.ConfigXmlCommand.CanExecute(null));
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
        Assert.False(vm.ConfigXmlCommand.CanExecute(null));
        Assert.False(vm.OpenModTypesFolderCommand.CanExecute(null));
        Assert.False(vm.CleanInvalidCommand.CanExecute(null));
        Assert.False(string.IsNullOrEmpty(vm.TypesLockedMessage));
        Assert.Equal(vm.TypesLockedMessage, vm.OpenModTypesFolderToolTip);

        vm.ConfigXmlCommand.Execute(null);
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
        string expected = Path.Combine(ServerPath, "mpmissions", MapName, "db", "ModTypes");

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            fileSystem: fs, processLauncher: launcher);

        vm.OpenModTypesFolderCommand.Execute(null);

        Assert.True(fs.DirectoryExists(expected));
        Assert.Equal(expected, launcher.LastOpenedFolder);
    }

    [Fact]
    public async Task LoadSave_MismatchedConfig_LoadsWithoutWarning()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                ModList = new List<string> { "@CF", "@Extra" },
                TypesFiles = new List<string> { "Old_types.xml" },
            }),
        };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            workshopMods: new[] { "@CF", "@Extra" }, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Equal(0, dialogs.ConfirmWithWarningCalls);
        Assert.Equal(1, saves.LoadSaveCalls);
    }

    [Fact]
    public async Task LoadSave_MissingSavedMod_BlocksLoadWithRedWarning()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                ModList = new List<string> { "@CF", "@Ghost" },
                TypesFiles = new List<string>(),
            }),
        };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            workshopMods: new[] { "@CF" }, loadedMods: new[] { "@CF" }, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => dialogs.ShowWarningCalls == 1);

        Assert.Equal(1, dialogs.ShowWarningCalls);
        Assert.Contains("@Ghost", dialogs.LastWarningMessage);
        Assert.Contains("missing from the Workshop", dialogs.LastWarningMessage);
        Assert.Equal(0, dialogs.ConfirmWithWarningCalls);
        Assert.Equal(0, saves.LoadSaveCalls);
    }

    [Fact]
    public async Task LoadSave_WithAvailableMods_RestoresModList()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                ModList = new List<string> { "@CF", "@Extra" },
                TypesFiles = new List<string>(),
            }),
        };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            workshopMods: new[] { "@CF", "@Extra" }, loadedMods: new[] { "@CF" }, saveGameService: saves);

        IReadOnlyList<string>? restored = null;
        vm.RestoreModList = mods =>
        {
            restored = mods.ToList();
            return Task.FromResult(true);
        };
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => restored is not null && saves.LoadSaveCalls == 1);

        Assert.Equal(new[] { "@CF", "@Extra" }, restored);
    }

    [Fact]
    public async Task LoadSave_ThenOnApplied_AppendsModsToSaveMeta()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                ModList = new List<string> { "@CF" },
                TypesFiles = new List<string>(),
            }),
        };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            workshopMods: new[] { "@CF", "@Extra" }, loadedMods: new[] { "@CF" }, saveGameService: saves);

        vm.RestoreModList = _ => Task.FromResult(true);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        vm.OnApplied(new[] { "@CF", "@Extra" });

        Assert.Equal(1, saves.AppendMetaModListCalls);
        Assert.Equal(new[] { "@CF", "@Extra" }, saves.LastAppendedMods);
    }

    [Fact]
    public async Task LoadSave_MatchingConfig_DoesNotWarn()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Success(new SaveMetaData
            {
                Map = MapName,
                StorageFolder = "storage_1",
                ModList = new List<string> { "@CF" },
                TypesFiles = new List<string>(),
            }),
        };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.True(dialogs.ConfirmCalls >= 1);
        Assert.Equal(0, dialogs.ConfirmWithWarningCalls);
        Assert.DoesNotContain("different mod setup", dialogs.LastConfirmMessage);
        Assert.DoesNotContain("configuration snapshot", dialogs.LastConfirmMessage);
        Assert.Equal(1, saves.LoadSaveCalls);
    }

    [Fact]
    public async Task LoadSave_NoSnapshot_WarnsNotRestored()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.Equal(1, dialogs.ConfirmWithWarningCalls);
        Assert.Contains("no configuration snapshot", dialogs.LastWarningMessage);
        Assert.True(string.IsNullOrWhiteSpace(dialogs.LastWarningNote));
        Assert.Equal(1, saves.LoadSaveCalls);
    }

    [Fact]
    public async Task LoadSave_CorruptSnapshot_WarnsUnreadable()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService
        {
            StoredSaves = new[] { "Alpha" },
            MetaToReturn = ConfigLoadResult<SaveMetaData>.Corrupt(),
        };

        (MapTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.LoadSaveCalls == 1);

        Assert.Equal(1, dialogs.ConfirmWithWarningCalls);
        Assert.Contains("snapshot is unreadable", dialogs.LastWarningMessage);
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
        vm.SelectedSave = "Alpha";

        vm.LoadSaveCommand.Execute(null);

        Assert.Equal(0, saves.LoadSaveCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void NewGame_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var saves = new FakeSaveGameService();
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), dialogs: dialogs, saveGameService: saves, serverProcess: process);

        vm.NewGameCommand.Execute(null);

        Assert.Equal(0, saves.NewGameCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void ConfigureMod_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService();
        var process = new FakeServerProcessState { Running = true };

        MapTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);

        vm.ConfigXmlCommand.Execute(null);

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
            TypesConfig config, string mapName, string missionPath, string workshopPath, string modName,
            IReadOnlyList<TypeFileSelection> selections, IReadOnlySet<string> loadedModNames)
        {
            ConfigureModCalls++;
            ConfigureModSelections = selections.ToList();
            ConfigureModSourceFiles = selections.Select(s => s.SourceFile).ToList();
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

        public TypesOperationResult RemoveUntrackedFiles(
            TypesConfig config, string mapName, string missionPath, IReadOnlySet<string> fileLeaves)
        {
            RemoveUntrackedCalls++;
            LastUntrackedLeaves = fileLeaves;
            return new TypesOperationResult { Success = true };
        }

        public int SyncEconomyCoreCalls { get; private set; }

        public IReadOnlySet<string>? LastPreviouslyOwned { get; private set; }

        public bool SyncEconomyCore(TypesConfig config, string mapName, string missionPath, IReadOnlySet<string> loadedModNames, IReadOnlySet<string>? previouslyOwned = null)
        {
            SyncEconomyCoreCalls++;
            LastPreviouslyOwned = previouslyOwned;
            return true;
        }

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

    private sealed class FakeTypesBackupService : ITypesBackupService
    {
        /// <summary>Mapping returned by <see cref="Restore"/>; null mimics "no backup".</summary>
        public MapTypesConfig? RestoreConfig { get; set; }

        public int EnsureCapturedCalls { get; private set; }

        public int CaptureCalls { get; private set; }

        public int RestoreCalls { get; private set; }

        public MapTypesConfig? LastCaptured { get; private set; }

        public string GetBackupFolder(string dataDirectory, string mapName) =>
            Path.Combine(dataDirectory, "ModTypes_Backup", mapName);

        public TypesBackupResult EnsureCaptured(
            string serverPath, string mapName, string dataDirectory, MapTypesConfig? configured)
        {
            EnsureCapturedCalls++;
            return new TypesBackupResult { Success = true };
        }

        public TypesBackupResult Capture(
            string serverPath, string mapName, string dataDirectory, MapTypesConfig configured)
        {
            CaptureCalls++;
            LastCaptured = configured;
            return new TypesBackupResult { Success = true };
        }

        public TypesBackupResult Restore(string serverPath, string mapName, string dataDirectory)
        {
            RestoreCalls++;
            return RestoreConfig is null
                ? new TypesBackupResult { Success = false, Messages = new[] { "No configured types backup was found." } }
                : new TypesBackupResult { Success = true, Config = RestoreConfig };
        }
    }

    private sealed class FakeServerConfigService : IServerConfigService
    {
        public bool UpdateTemplate(string serverPath, string missionFolderName) => true;
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
    }

    private sealed class FakeSaveGameService : ISaveGameService
    {
        public IReadOnlyList<string> StoredSaves { get; set; } = Array.Empty<string>();

        public int AddSaveCalls { get; private set; }

        public string? LastAddName { get; private set; }

        public bool LastAddOverwrite { get; private set; }

        public SaveMetaData? LastAddMeta { get; private set; }

        public ConfigLoadResult<SaveMetaData> MetaToReturn { get; set; } = ConfigLoadResult<SaveMetaData>.Missing();

        public int LoadSaveCalls { get; private set; }

        public string? LastLoadName { get; private set; }

        public int NewGameCalls { get; private set; }

        /// <summary>When set, NewGame blocks on this gate so tests can observe the in-flight state.</summary>
        public TaskCompletionSource? NewGameGate { get; set; }

        public int DeleteSaveCalls { get; private set; }

        public string? LastDeleteName { get; private set; }

        public int ReadInstanceId(string serverPath) => 1;

        public string GetStorageFolderPath(string serverPath, string mapName) =>
            Path.Combine(serverPath, "mpmissions", mapName, "storage_1");

        public string GetModTypesFolderPath(string serverPath, string mapName) =>
            Path.Combine(serverPath, "mpmissions", mapName, "db", "ModTypes");

        public IReadOnlyList<string> ListSaves(string dataDirectory, string mapName) => StoredSaves;

        public string GetSaveFolderPath(string dataDirectory, string mapName, string saveName) =>
            Path.Combine(dataDirectory, "Progress_Saves", mapName, saveName);

        public string GetModTypesSnapshotPath(string dataDirectory, string mapName, string saveName) =>
            Path.Combine(dataDirectory, "Progress_Saves", mapName, saveName, "ModTypes");

        public SaveGameResult AddSave(string serverPath, string mapName, string dataDirectory, string saveName, bool overwrite, SaveMetaData? meta = null)
        {
            AddSaveCalls++;
            LastAddName = saveName;
            LastAddOverwrite = overwrite;
            LastAddMeta = meta;
            return new SaveGameResult { Success = true, Message = $"Saved \"{saveName}\"." };
        }

        public SaveGameResult LoadSave(string serverPath, string mapName, string dataDirectory, string saveName)
        {
            LoadSaveCalls++;
            LastLoadName = saveName;
            return new SaveGameResult { Success = true, Message = $"Loaded \"{saveName}\"." };
        }

        public ConfigLoadResult<SaveMetaData> GetMeta(string dataDirectory, string mapName, string saveName) => MetaToReturn;

        public int AppendMetaModListCalls { get; private set; }

        public IReadOnlyList<string>? LastAppendedMods { get; private set; }

        public SaveGameResult AppendMetaModList(string dataDirectory, string mapName, string saveName, IReadOnlyList<string> mods)
        {
            AppendMetaModListCalls++;
            LastAppendedMods = mods.ToList();
            return new SaveGameResult { Success = true, Message = "Updated mod list." };
        }

        public SaveGameResult NewGame(string serverPath, string mapName)
        {
            NewGameCalls++;
            NewGameGate?.Task.GetAwaiter().GetResult();
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
    }
}
