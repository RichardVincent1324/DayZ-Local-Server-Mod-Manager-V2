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
    public void Sync_BuildsPresetList_DefaultFirst()
    {
        var config = AppliedConfig();
        var presets = new FakePresetService();
        presets.PresetNames.Add("Hardcore");
        PresetTypesViewModel vm = Create(config, presetService: presets);

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
        PresetTypesViewModel vm = Create(config, presetService: presets);
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
        PresetTypesViewModel vm = Create(config, presetService: presets, dialogs: dialogs);
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
        PresetTypesViewModel vm = Create(config, presetService: presets, dialogs: dialogs);
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
        PresetTypesViewModel vm = Create(config);
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
        PresetTypesViewModel vm = Create(config, presetService: presets);
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
        PresetTypesViewModel vm = Create(config, presetService: presets, junctionService: junctions, dialogs: dialogs);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });
        presets.CreatePreset(ServerPath, @"D:\data", MapName, "Hardcore", false);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        vm.ActivatePreset = (_, preset) => { vm.ActivePresetName = preset; return Task.FromResult(true); };
        vm.SelectedPreset = vm.Presets.First(p => p.Name == "Hardcore");
        await WaitUntilAsync(() => string.Equals(vm.ActivePresetName, "Hardcore", StringComparison.Ordinal));

        vm.DeletePresetCommand.Execute(null);

        Assert.Contains((ServerPath, "1"), junctions.DeletedFolders);
    }
}

