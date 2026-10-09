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
    public async Task AddSave_UsesEnteredName_ForNewSave()
    {
        var dialogs = new FakeDialogs { AskTextResult = "First" };
        var saves = new FakeSaveGameService();

        (PresetTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs, saveGameService: saves);

        vm.WipeWorldCommand.Execute(null);
        await WaitUntilAsync(() => saves.WipeWorldCalls == 1);

        Assert.Equal(1, saves.LastInstanceId);
        Assert.Equal(0, types.SyncEconomyCoreCalls);
    }

    [Fact]
    public async Task LoadSave_ConfirmsOnce_AndDoesNotWarnAboutConfiguration()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
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

        PresetTypesViewModel vm = Create(AppliedConfig(), dialogs: dialogs, saveGameService: saves, serverProcess: process);

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

        PresetTypesViewModel vm = Create(AppliedConfig(), dialogs: dialogs, saveGameService: saves, serverProcess: process);
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

        PresetTypesViewModel vm = Create(AppliedConfig(), dialogs: dialogs, saveGameService: saves, serverProcess: process);

        vm.WipeWorldCommand.Execute(null);

        Assert.Equal(0, saves.WipeWorldCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task RenameSave_InvokesSaveService()
    {
        var dialogs = new FakeDialogs { AskTextResult = "Beta" };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs, saveGameService: saves);

        vm.SelectedSave = vm.SaveNames.Single(entry => entry.IsOrphaned);

        vm.DeleteSaveCommand.Execute(null);
        await WaitUntilAsync(() => saves.DeleteStorageCalls == 1);

        Assert.Equal(5, saves.LastDeleteStorageInstanceId);
        Assert.Equal(0, saves.DeleteSaveCalls);
    }
}

