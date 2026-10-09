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
    public void ConfigureMod_AsksConfirmation_WhenSelectionDeletesActiveFiles()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        const string hardcore = @"D:\workshop\@CF\hardcore_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual, hardcore } };
        var dialogs = new FakeDialogs { ConfirmResult = false, SelectedFiles = new[] { new TypeFileSelection(hardcore, TypesFileRole.Types) } };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

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

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

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

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

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

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Equal(0, fakeTypes.ConfigureModCalls);
    }

    [Fact]
    public void ConfigureMod_ShowsSaveWarning_WhenPresetHasWorldData()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs
        {
            ConfirmResult = true,
            SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) },
        };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            AppliedConfig(), types, dialogs, saveGameService: saves);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Contains("This preset has existing saves", fakeDialogs.LastConfirmMessage);
        Assert.Equal(1, fakeTypes.ConfigureModCalls);
    }

    [Fact]
    public void ConfigureMod_StacksSaveWarningAboveOverwriteWarning()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_casual_types.xml"));
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs
        {
            ConfirmResult = true,
            SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) },
        };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (PresetTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, saveGameService: saves);

        vm.ConfigureModCommand.Execute("@CF");

        string message = Assert.IsType<string>(fakeDialogs.LastConfirmMessage);
        int saveIndex = message.IndexOf("This preset has existing saves", StringComparison.Ordinal);
        int overwriteIndex = message.IndexOf("already has configured type file(s)", StringComparison.Ordinal);
        Assert.True(saveIndex >= 0);
        Assert.True(overwriteIndex >= 0);
        Assert.True(saveIndex < overwriteIndex, "The save warning must appear above the overwrite warning.");
    }

    [Fact]
    public void ConfigureMod_NoConfirm_WhenNoWorldDataAndNothingConfigured()
    {
        const string casual = @"D:\workshop\@CF\casual_types.xml";
        var types = new FakeTypesService { DiscoveryFiles = new[] { casual } };
        var dialogs = new FakeDialogs
        {
            ConfirmResult = true,
            SelectedFiles = new[] { new TypeFileSelection(casual, TypesFileRole.Types) },
        };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            AppliedConfig(), types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.Equal(0, fakeDialogs.ConfirmCalls);
        Assert.Equal(1, fakeTypes.ConfigureModCalls);
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

        (PresetTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

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

        (PresetTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);

        vm.ConfigureModCommand.Execute("@CF");

        Assert.Equal(Path.Combine(@"D:\workshop", "@CF"), fakeDialogs.LastModFolderPath);
    }

    [Fact]
    public void RefreshModNames_LeavesSelectionEmpty()
    {
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), new FakeDialogs());

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

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(config, types, dialogs);
        Assert.Null(vm.SelectedMod);

        vm.SelectedMod = "@CF";
        await WaitUntilAsync(() => fakeTypes.ConfigureModCalls == 1);

        Assert.Equal(1, fakeTypes.ConfigureModCalls);
        Assert.Null(vm.SelectedMod);
        Assert.True(vm.IsModSelectionEmpty);
    }

    [Fact]
    public async Task SelectingMod_WithLiveWorld_ConfiguresAfterConfirmation()
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

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(config, types, dialogs, fileSystem: fs);

        // A world exists, but the edit is no longer blocked: it warns and proceeds.
        vm.SelectedMod = "@CF";
        await WaitUntilAsync(() => fakeTypes.ConfigureModCalls == 1);

        Assert.Equal(1, fakeTypes.ConfigureModCalls);
        Assert.Contains("This preset has existing saves", dialogs.LastConfirmMessage);
        Assert.Null(vm.SelectedMod);
    }

    [Fact]
    public void RemoveSelected_AsksConfirmation_BeforeDeleting()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = false };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);
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

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(config, types, dialogs);
        vm.SelectedTypesRows.Add(new TypesRowViewModel("@CF", "CF_types.xml", isInactive: false));

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Equal(1, fakeTypes.RemoveFilesCalls);
        Assert.NotNull(fakeTypes.LastRemoveLeaves);
        Assert.Contains("CF_types.xml", fakeTypes.LastRemoveLeaves);
    }

    [Fact]
    public void RemoveSelected_ShowsSaveWarning_WhenPresetHasWorldData()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, saveGameService: saves);
        vm.SelectedTypesRows.Add(new TypesRowViewModel("@CF", "CF_types.xml", isInactive: false));

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Contains("This preset has existing saves", fakeDialogs.LastConfirmMessage);
        Assert.Equal(0, fakeTypes.RemoveFilesCalls);
    }

    [Fact]
    public void CleanInvalid_AsksConfirmation_WhenInvalidModsExist()
    {
        var config = ConfigWithEntry(Entry("@Ghost", @"db\ModTypes\Ghost_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = false };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, workshopMods: new[] { "@CF" }, loadedMods: new[] { "@CF" });

        vm.CleanInvalidCommand.Execute(null);

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.Contains("@Ghost", fakeDialogs.LastConfirmMessage);
        Assert.Equal(0, fakeTypes.CleanInvalidCalls);
    }

    [Fact]
    public void CleanInvalid_PrependsSaveWarning_WhenPresetHasWorldData()
    {
        var config = ConfigWithEntry(Entry("@Ghost", @"db\ModTypes\Ghost_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var saves = new FakeSaveGameService { StoredSaves = new[] { "Alpha" } };

        (PresetTypesViewModel vm, _, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, workshopMods: new[] { "@CF" }, loadedMods: new[] { "@CF" },
            saveGameService: saves);

        vm.CleanInvalidCommand.Execute(null);

        Assert.Contains("This preset has existing saves", fakeDialogs.LastConfirmMessage);
        Assert.Contains("@Ghost", fakeDialogs.LastConfirmMessage);
    }

    [Fact]
    public void CleanInvalid_DoesNotPrompt_WhenNothingInvalid()
    {
        var config = new TypesConfig { CurrentMap = MapName, Maps = { [MapName] = new MapTypesConfig() } };
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs();

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, workshopMods: new[] { "@CF" }, loadedMods: new[] { "@CF" });

        vm.CleanInvalidCommand.Execute(null);

        Assert.Equal(0, fakeDialogs.ConfirmCalls);
        Assert.Equal(1, fakeTypes.CleanInvalidCalls);
    }

    [Fact]
    public void Sync_AddsUntrackedRows_ForFilesInModTypesNotInConfig()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\ModTypes\CF_types.xml"));
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ModTypesFolderPath()}\Orphan_types.xml");

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(config, new FakeTypesService(), new FakeDialogs(), fileSystem: fs);

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

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(config, new FakeTypesService(), new FakeDialogs(), fileSystem: fs);

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

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(config, types, dialogs, fileSystem: fs);

        TypesRowViewModel orphan = vm.TypesRows.Single(r => r.IsUntracked);
        vm.SelectedTypesRows.Add(orphan);

        vm.RemoveSelectedCommand.Execute(null);
        await WaitUntilAsync(() => fakeTypes.RemoveUntrackedCalls == 1);

        Assert.Equal(1, fakeTypes.RemoveUntrackedCalls);
        Assert.Equal(0, fakeTypes.RemoveFilesCalls);
        Assert.Contains("Orphan_types.xml", fakeTypes.LastUntrackedLeaves!);
    }

    [Fact]
    public void TypesCommands_Enabled_WithMapApplied()
    {
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), new FakeDialogs());

        Assert.True(vm.ConfigureModCommand.CanExecute("@CF"));
        Assert.True(vm.OpenModTypesFolderCommand.CanExecute(null));
        Assert.True(vm.CleanInvalidCommand.CanExecute(null));
        Assert.Equal("Open ModTypes folder in File Explorer", vm.OpenModTypesFolderToolTip);
    }

    [Fact]
    public void TypesCommands_StayEnabled_WhenWorldExists()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Path.Combine(ServerPath, "mpmissions", MapName, "storage_1"));
        var types = new FakeTypesService();
        var launcher = new FakeProcessLauncher();

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, new FakeDialogs(), fileSystem: fs, processLauncher: launcher);

        // A world no longer disables the types operations; they warn instead.
        Assert.True(vm.ConfigureModCommand.CanExecute("@CF"));
        Assert.True(vm.OpenModTypesFolderCommand.CanExecute(null));
        Assert.True(vm.CleanInvalidCommand.CanExecute(null));

        vm.OpenModTypesFolderCommand.Execute(null);
        Assert.NotNull(launcher.LastOpenedFolder);
    }

    [Fact]
    public void OpenModTypesFolderCommand_CreatesFolderAndOpensIt()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();
        string expected = PresetModTypesFolder(MapName);

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            fileSystem: fs, processLauncher: launcher);

        vm.OpenModTypesFolderCommand.Execute(null);

        Assert.True(fs.DirectoryExists(expected));
        Assert.Equal(expected, launcher.LastOpenedFolder);
    }

    [Fact]
    public void ConfigureMod_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService();
        var process = new FakeServerProcessState { Running = true };

        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);

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

        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);
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

        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);

        vm.CleanInvalidCommand.Execute(null);

        Assert.Equal(0, types.CleanInvalidCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }
}

