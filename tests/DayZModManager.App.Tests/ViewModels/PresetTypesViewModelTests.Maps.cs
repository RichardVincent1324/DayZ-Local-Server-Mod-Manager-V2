using System.IO;
using DayZModManager.Core;
using DayZModManager.App.Services;
using DayZModManager.App.ViewModels;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Tests.ViewModels;

public partial class PresetTypesViewModelTests
{
    [Fact]
    public void Refresh_WithNoCurrentMap_AppliesFirstDiscoveredMap()
    {
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();

        PresetTypesViewModel vm = Create(config, typesConfigStore: store);
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

        PresetTypesViewModel vm = Create(config, typesConfigStore: store);
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

        PresetTypesViewModel vm = Create(config, batchFileService: batch, fileSystem: fs);
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

        PresetTypesViewModel vm = Create(config, mapService: mapService, typesConfigStore: store);
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

        PresetTypesViewModel vm = Create(config, new FakeMapService());
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());
        vm.ReconcileAppliedMap();

        Assert.Equal(string.Empty, config.CurrentMap);
    }

    [Fact]
    public void ReconcileAppliedMap_FirstRun_AppliesFirstDiscoveredMap()
    {
        var config = new TypesConfig();
        var store = new FakeTypesConfigStore();

        PresetTypesViewModel vm = Create(config, typesConfigStore: store);
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

        PresetTypesViewModel vm = Create(config, typesConfigStore: store);
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

        PresetTypesViewModel vm = Create(config, typesConfigStore: store);
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

        PresetTypesViewModel vm = Create(config, typesConfigStore: store, batchFileService: batch);
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

        PresetTypesViewModel vm = Create(
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

        PresetTypesViewModel vm = Create(config, mapService: new FakeMapService(), typesConfigStore: store);
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

        PresetTypesViewModel vm = Create(config, typesConfigStore: store);
        vm.Refresh(Settings(), Array.Empty<string>(), Array.Empty<string>());

        vm.SelectedMap = deadMap;

        Assert.Equal(MapName, config.CurrentMap);
        Assert.Equal(MapName, vm.SelectedMap);
        Assert.False(store.SaveCalled, "an unresolvable map must not be applied");
    }

    [Fact]
    public void OpenMapProfilesFolderCommand_WithCurrentMap_CreatesFolderAndOpensIt()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();
        string expected = PresetProfilesFolder(MapName);

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(), fileSystem: fs);

        // The profiles button stays enabled even when a world is present.
        Assert.True(vm.OpenMapProfilesFolderCommand.CanExecute(null));
    }

    [Fact]
    public void OpenMapProfilesFolderCommand_WithNoServerPath_DoesNotOpen()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();

        PresetTypesViewModel vm = Create(new TypesConfig(), fileSystem: fs, processLauncher: launcher);
        // No Refresh -> no server path applied.

        vm.OpenMapProfilesFolderCommand.Execute(null);

        Assert.Null(launcher.LastOpenedFolder);
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

        PresetTypesViewModel vm = Create(config, mapService: mapService, typesConfigStore: store, dialogs: dialogs, serverProcess: process);
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
}

