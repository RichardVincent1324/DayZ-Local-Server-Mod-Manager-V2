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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
            GeneratedFiles = { @"db\type_files\CF_casual_types.xml" },
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_casual_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_types.xml"));
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
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_types.xml"));
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
        var config = ConfigWithEntry(Entry("@Ghost", @"db\type_files\Ghost_types.xml"));
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
        var config = ConfigWithEntry(Entry("@Ghost", @"db\type_files\Ghost_types.xml"));
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
        var config = new TypesConfig { CurrentMap = MapName };
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs();

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            config, types, dialogs, workshopMods: new[] { "@CF" }, loadedMods: new[] { "@CF" });

        vm.CleanInvalidCommand.Execute(null);

        Assert.Equal(0, fakeDialogs.ConfirmCalls);
        Assert.Equal(1, fakeTypes.CleanInvalidCalls);
    }

    [Fact]
    public void Sync_AddsUntrackedRows_ForFilesInTypeFilesNotInConfig()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_types.xml"));
        var fs = new FakeFileSystem();
        fs.AddFile($@"{TypeFilesFolderPath()}\Orphan_types.xml");

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
            GeneratedFiles = { @"db\type_files\CF_types.xml", @"db\type_files\CF_spawn.xml" },
            FileRoles = { { "CF_types.xml", "types" }, { "CF_spawn.xml", "spawnabletypes" } },
        });
        var fs = new FakeFileSystem();
        fs.AddFile($@"{TypeFilesFolderPath()}\Orphan_types.xml");

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(config, new FakeTypesService(), new FakeDialogs(), fileSystem: fs);

        Assert.Equal("type", vm.TypesRows.Single(r => r.FileName == "CF_types.xml").FileType);
        Assert.Equal("spawnable", vm.TypesRows.Single(r => r.FileName == "CF_spawn.xml").FileType);
        Assert.Equal(string.Empty, vm.TypesRows.Single(r => r.IsUntracked).FileType);
    }

    [Fact]
    public async Task RemoveSelected_DeletesUntrackedRows_WithoutTouchingTrackedFiles()
    {
        var config = ConfigWithEntry(Entry("@CF", @"db\type_files\CF_types.xml"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var fs = new FakeFileSystem();
        fs.AddFile($@"{TypeFilesFolderPath()}\Orphan_types.xml");

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
        Assert.True(vm.OpenTypeFilesFolderCommand.CanExecute(null));
        Assert.True(vm.CleanInvalidCommand.CanExecute(null));
        Assert.Equal("Open type_files folder in File Explorer", vm.OpenTypeFilesFolderToolTip);
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
        Assert.True(vm.OpenTypeFilesFolderCommand.CanExecute(null));
        Assert.True(vm.CleanInvalidCommand.CanExecute(null));

        vm.OpenTypeFilesFolderCommand.Execute(null);
        Assert.NotNull(launcher.LastOpenedFolder);
    }

    [Fact]
    public void OpenTypeFilesFolderCommand_CreatesFolderAndOpensIt()
    {
        var fs = new FakeFileSystem();
        var launcher = new FakeProcessLauncher();
        string expected = PresetTypeFilesFolder(MapName);

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            fileSystem: fs, processLauncher: launcher);

        vm.OpenTypeFilesFolderCommand.Execute(null);

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

    [Fact]
    public void PreflightTypesIssues_FormatsMissingModsAndFiles()
    {
        var types = new FakeTypesService
        {
            LaunchIssues = new TypesLaunchIssues(new[] { "@Ghost" }, new[] { "CF_types.xml" }),
        };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            ConfigWithEntry(Entry("@CF", @"db\type_files\CF_types.xml")), types, new FakeDialogs());

        IReadOnlyList<string> issues = vm.PreflightTypesIssues();

        Assert.Contains(issues, m => m.Contains("@Ghost"));
        Assert.Contains(issues, m => m.Contains("CF_types.xml"));
    }

    [Fact]
    public void PreflightTypesIssues_NoCurrentMap_ReturnsEmpty()
    {
        var types = new FakeTypesService
        {
            LaunchIssues = new TypesLaunchIssues(new[] { "@Ghost" }, Array.Empty<string>()),
        };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(new TypesConfig(), types, new FakeDialogs());

        Assert.Empty(vm.PreflightTypesIssues());
    }

    [Fact]
    public void SyncEconomyCore_ReturnsServiceResult()
    {
        var active = new TypesConfig
        {
            CurrentMap = MapName,
            Mods = { Entry("@CF", "CF_types.xml") },
        };
        var types = new FakeTypesService();
        PresetTypesViewModel vm = Create(active, typesService: types);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        Assert.True(vm.SyncEconomyCore());
        Assert.Equal(1, types.SyncEconomyCoreCalls);
        Assert.Equal(EconomyCoreService.ManagedFolder, types.LastEconomyFolder);
        Assert.Equal("db/DZMMV2_Types", types.LastEconomyFolder);
        Assert.DoesNotContain("..", types.LastEconomyFolder);
    }

    [Fact]
    public void SyncEconomyCore_UnmanagedLegacyBlock_ShowsAlert()
    {
        var active = new TypesConfig
        {
            CurrentMap = MapName,
            Mods = { Entry("@CF", "CF_types.xml") },
        };
        var types = new FakeTypesService
        {
            SyncEconomyCoreResult = new TypesOperationResult { Success = true, UnmanagedLegacyBlockDetected = true },
        };
        var dialogs = new FakeDialogs();
        PresetTypesViewModel vm = Create(active, typesService: types, dialogs: dialogs);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        Assert.True(vm.SyncEconomyCore());

        Assert.Equal(1, dialogs.ShowAlertCalls);
        Assert.Contains("unmanaged", dialogs.LastAlertMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cfgeconomycore.xml", dialogs.LastAlertNote!);
    }

    [Fact]
    public void PreflightTypesIssues_ReportsUnsynchronizedEconomyBlock()
    {
        string missionPath = $@"{ServerPath}\mpmissions\{MapName}";
        string expectedFolder = EconomyCoreService.ManagedFolder;
        var types = new FakeTypesService
        {
            ActiveTypesFolder = expectedFolder,
            ActiveTypeFiles = new[] { "CF_types.xml" },
            ManagedTypeFileNames = new[] { "Old_types.xml" },
        };
        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        IReadOnlyList<string> issues = vm.PreflightTypesIssues();

        Assert.Contains(issues, i => i.Contains("cfgeconomycore.xml is not synchronized"));
    }

    [Fact]
    public void PreflightTypesIssues_DoesNotReport_WhenEconomyBlockMatches()
    {
        string missionPath = $@"{ServerPath}\mpmissions\{MapName}";
        string expectedFolder = EconomyCoreService.ManagedFolder;
        var types = new FakeTypesService
        {
            ActiveTypesFolder = expectedFolder,
            ActiveTypeFiles = new[] { "CF_types.xml" },
            ManagedTypeFileNames = new[] { "CF_types.xml" },
        };
        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        Assert.DoesNotContain(
            vm.PreflightTypesIssues(), i => i.Contains("cfgeconomycore.xml is not synchronized"));
    }

    [Fact]
    public void PreflightTypesIssues_ReportsMismatchedFileRole_EvenWhenNamesMatch()
    {
        var types = new FakeTypesService
        {
            ActiveTypesFolder = EconomyCoreService.ManagedFolder,
            ActiveTypeFileEntries = new[]
            {
                new ManagedTypeFileEntry("CF_types.xml", "spawnabletypes"),
            },
            ManagedTypeFileEntries = new[]
            {
                new ManagedTypeFileEntry("CF_types.xml", "types"),
            },
        };
        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        IReadOnlyList<string> issues = vm.PreflightTypesIssues();

        Assert.Contains(issues, i => i.Contains("cfgeconomycore.xml is not synchronized"));
    }

    [Fact]
    public void PreflightTypesIssues_ReportsUnsynchronizedTypeFilesJunction()
    {
        var types = new FakeTypesService
        {
            ActiveTypesFolder = EconomyCoreService.ManagedFolder,
        };
        var junctions = new FakeTypeFileJunctionService { VerifyResult = false };
        PresetTypesViewModel vm = Create(
            AppliedConfig(), typesService: types, typeFileJunctionService: junctions);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        IReadOnlyList<string> issues = vm.PreflightTypesIssues();

        Assert.Contains(issues, i => i.Contains("managed type files junction"));
        Assert.Equal(1, junctions.VerifyCalls);
        Assert.Equal(Path.Combine(ServerPath, "mpmissions", MapName), junctions.LastMissionPath);
        Assert.Equal(TypeFilesFolderPath(), junctions.LastTargetFolder);
    }
}

