using System.IO;
using DayZModManager.Core;
using DayZModManager.App.ViewModels;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Tests.ViewModels;

public partial class PresetTypesViewModelTests
{
    private static FakeTypesConfigStore StoreWith(params (string Name, string Role)[] assignments) =>
        new()
        {
            LoadResult = ConfigLoadResult<Dictionary<string, string>>.Success(
                assignments.ToDictionary(entry => entry.Name, entry => entry.Role, StringComparer.OrdinalIgnoreCase)),
        };

    private static FakeFileSystem TypeFilesSystem(params string[] files)
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(TypeFilesFolderPath());
        foreach (string file in files)
        {
            fs.AddFile(Path.Combine(TypeFilesFolderPath(), file), "<types/>");
        }

        return fs;
    }

    [Fact]
    public void Sync_PopulatesRowsFromFilesystem_InNaturalOrder()
    {
        FakeFileSystem fs = TypeFilesSystem("10_weapons.xml", "2_base.xml", "99_override.xml");
        FakeTypesConfigStore store = StoreWith(
            ("2_base.xml", "types"),
            ("99_override.xml", "spawnabletypes"));

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            fileSystem: fs, typesConfigStore: store);

        Assert.Equal(
            new[] { "2_base.xml", "10_weapons.xml", "99_override.xml" },
            vm.TypesRows.Select(row => row.FileName));
        Assert.Equal("types", vm.TypesRows.Single(row => row.FileName == "2_base.xml").FileType);
        Assert.Equal("spawnabletypes", vm.TypesRows.Single(row => row.FileName == "99_override.xml").FileType);
        Assert.Null(vm.TypesRows.Single(row => row.FileName == "10_weapons.xml").FileType);
        Assert.Equal(
            PresetTypesViewModel.UnassignedFileTypeLabel,
            vm.TypesRows.Single(row => row.FileName == "10_weapons.xml").FileTypeSelection);
    }

    [Fact]
    public void SettingFileType_PersistsAssignmentAndSyncsCe()
    {
        FakeFileSystem fs = TypeFilesSystem("custom.xml");
        var store = new FakeTypesConfigStore();
        var types = new FakeTypesService();
        var log = new LogViewModel();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, new FakeDialogs(),
            fileSystem: fs, typesConfigStore: store, log: log);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileTypeSelection = "types";

        Assert.True(store.SaveCalled);
        Assert.Equal("types", store.LastSaved["custom.xml"]);
        Assert.Equal("types", row.FileType);
        Assert.Equal(1, types.SyncEconomyCoreCalls);
        Assert.Contains(log.Entries, entry =>
            entry.Level == LogLevel.Info && entry.Message.Contains("Assigned custom.xml to types, CE synced"));
    }

    [Fact]
    public void ClearingFileType_RemovesAssignmentAndSyncsCe()
    {
        FakeFileSystem fs = TypeFilesSystem("custom.xml");
        FakeTypesConfigStore store = StoreWith(("custom.xml", "spawnabletypes"));
        var types = new FakeTypesService();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, new FakeDialogs(),
            fileSystem: fs, typesConfigStore: store);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileTypeSelection = PresetTypesViewModel.UnassignedFileTypeLabel;

        Assert.True(store.SaveCalled);
        Assert.False(store.LastSaved.ContainsKey("custom.xml"));
        Assert.Null(row.FileType);
        Assert.Equal(1, types.SyncEconomyCoreCalls);
    }

    [Fact]
    public void SettingFileType_SaveFailure_RestoresPreviousSelection()
    {
        FakeFileSystem fs = TypeFilesSystem("custom.xml");
        var store = new FakeTypesConfigStore { SaveException = new IOException("disk full") };
        var dialogs = new FakeDialogs();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            fileSystem: fs, typesConfigStore: store);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileTypeSelection = "types";

        Assert.Null(row.FileType);
        Assert.Equal(PresetTypesViewModel.UnassignedFileTypeLabel, row.FileTypeSelection);
        Assert.Contains("disk full", dialogs.LastErrorMessage);
    }

    [Fact]
    public void SettingFileType_CeSyncFailure_KeepsAssignmentAndShowsError()
    {
        FakeFileSystem fs = TypeFilesSystem("custom.xml");
        var store = new FakeTypesConfigStore();
        var types = new FakeTypesService
        {
            SyncEconomyCoreResult = new TypesOperationResult { Success = false },
        };
        var dialogs = new FakeDialogs();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs,
            fileSystem: fs, typesConfigStore: store);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileTypeSelection = "types";

        Assert.Equal("types", row.FileType);
        Assert.Equal("types", store.LastSaved["custom.xml"]);
        Assert.Contains("cfgeconomycore.xml", dialogs.LastErrorMessage);
    }

    [Fact]
    public void RenameFile_MovesPhysicalFileAndPreservesRole()
    {
        FakeFileSystem fs = TypeFilesSystem("10_types.xml");
        FakeTypesConfigStore store = StoreWith(("10_types.xml", "types"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs,
            fileSystem: fs, typesConfigStore: store);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileName = "20_types.xml";

        Assert.Equal("20_types.xml", row.FileName);
        Assert.False(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "10_types.xml")));
        Assert.True(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "20_types.xml")));
        Assert.True(store.SaveCalled);
        Assert.Equal("types", store.LastSaved["20_types.xml"]);
        Assert.False(store.LastSaved.ContainsKey("10_types.xml"));
        Assert.Equal(1, types.SyncEconomyCoreCalls);
    }

    [Fact]
    public void RenameFile_RejectsCollisionWithoutMoving()
    {
        FakeFileSystem fs = TypeFilesSystem("10_types.xml", "20_types.xml");
        var dialogs = new FakeDialogs();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            fileSystem: fs);

        TypesRowViewModel row = vm.TypesRows.Single(row => row.FileName == "10_types.xml");
        row.FileName = "20_types.xml";

        Assert.Equal("10_types.xml", row.FileName);
        Assert.True(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "10_types.xml")));
        Assert.Contains("already exists", dialogs.LastErrorMessage);
    }

    [Fact]
    public void RenameFile_RejectsPathTraversal()
    {
        FakeFileSystem fs = TypeFilesSystem("10_types.xml");
        var dialogs = new FakeDialogs();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            fileSystem: fs);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileName = @"..\evil.xml";

        Assert.Equal("10_types.xml", row.FileName);
        Assert.Contains("invalid", dialogs.LastErrorMessage);
    }

    [Fact]
    public void RenameFile_SaveFailure_RollsBackMove()
    {
        FakeFileSystem fs = TypeFilesSystem("10_types.xml");
        var store = new FakeTypesConfigStore { SaveException = new IOException("locked") };
        var dialogs = new FakeDialogs();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            fileSystem: fs, typesConfigStore: store);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileName = "20_types.xml";

        Assert.Equal("10_types.xml", row.FileName);
        Assert.True(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "10_types.xml")));
        Assert.False(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "20_types.xml")));
        Assert.Contains("locked", dialogs.LastErrorMessage);
    }

    [Fact]
    public void RenameFile_CeSyncFailure_KeepsRenameAndShowsError()
    {
        FakeFileSystem fs = TypeFilesSystem("10_types.xml");
        FakeTypesConfigStore store = StoreWith(("10_types.xml", "types"));
        var types = new FakeTypesService
        {
            SyncEconomyCoreResult = new TypesOperationResult { Success = false },
        };
        var dialogs = new FakeDialogs();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs,
            fileSystem: fs, typesConfigStore: store);

        TypesRowViewModel row = vm.TypesRows.Single();
        row.FileName = "20_types.xml";

        Assert.Equal("20_types.xml", row.FileName);
        Assert.True(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "20_types.xml")));
        Assert.Equal("types", store.LastSaved["20_types.xml"]);
        Assert.Contains("cfgeconomycore.xml", dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task Import_CopiesSelectedFilesWithoutRoles()
    {
        var fs = new FakeFileSystem();
        var types = new FakeTypesService
        {
            DiscoveryFiles = new[] { @"D:\workshop\@CF\types.xml" },
        };
        var dialogs = new FakeDialogs
        {
            SelectedFiles = new[] { @"D:\workshop\@CF\types.xml" },
        };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs, fileSystem: fs);

        Assert.True(await vm.ImportTypesAsync("@CF"));

        Assert.Equal(1, fakeTypes.ImportFilesCalls);
        Assert.Equal(new[] { @"D:\workshop\@CF\types.xml" }, fakeTypes.LastImportSourceFiles);
        Assert.False(fakeTypes.LastImportOverwrite);
    }

    [Fact]
    public async Task Import_CollisionPrompt_OverwritesWhenConfirmed()
    {
        FakeFileSystem fs = TypeFilesSystem("types.xml");
        var types = new FakeTypesService
        {
            DiscoveryFiles = new[] { @"D:\workshop\@CF\types.xml" },
        };
        var dialogs = new FakeDialogs
        {
            ConfirmResult = true,
            SelectedFiles = new[] { @"D:\workshop\@CF\types.xml" },
        };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, FakeDialogs fakeDialogs) = CreateTypesVm(
            AppliedConfig(), types, dialogs, fileSystem: fs);

        await vm.ImportTypesAsync("@CF");

        Assert.True(fakeDialogs.ConfirmCalls >= 1);
        Assert.True(fakeTypes.LastImportOverwrite);
    }

    [Fact]
    public async Task Import_CollisionPrompt_SkipsWhenDeclined()
    {
        FakeFileSystem fs = TypeFilesSystem("types.xml");
        var types = new FakeTypesService
        {
            DiscoveryFiles = new[] { @"D:\workshop\@CF\types.xml" },
        };
        var dialogs = new FakeDialogs
        {
            ConfirmResult = false,
            SelectedFiles = new[] { @"D:\workshop\@CF\types.xml" },
        };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs, fileSystem: fs);

        await vm.ImportTypesAsync("@CF");

        Assert.False(fakeTypes.LastImportOverwrite);
    }

    [Fact]
    public void RemoveSelected_DeletesFilesAndAssignments()
    {
        FakeFileSystem fs = TypeFilesSystem("types.xml", "spawnable.xml");
        FakeTypesConfigStore store = StoreWith(("types.xml", "types"), ("spawnable.xml", "spawnabletypes"));
        var types = new FakeTypesService();
        var dialogs = new FakeDialogs { ConfirmResult = true };

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs, fileSystem: fs, typesConfigStore: store);

        TypesRowViewModel row = vm.TypesRows.Single(r => r.FileName == "types.xml");
        vm.SelectedTypesRows.Add(row);

        vm.RemoveSelectedCommand.Execute(null);

        Assert.True(store.SaveCalled);
        Assert.False(store.LastSaved.ContainsKey("types.xml"));
        Assert.False(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "types.xml")));
        Assert.True(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "spawnable.xml")));
        Assert.Equal(1, fakeTypes.SyncEconomyCoreCalls);
    }

    [Fact]
    public void RemoveSelected_CeSyncFailure_KeepsRemovalAndShowsError()
    {
        FakeFileSystem fs = TypeFilesSystem("types.xml");
        FakeTypesConfigStore store = StoreWith(("types.xml", "types"));
        var types = new FakeTypesService
        {
            SyncEconomyCoreResult = new TypesOperationResult { Success = false },
        };
        var dialogs = new FakeDialogs { ConfirmResult = true };

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, dialogs,
            fileSystem: fs, typesConfigStore: store);

        vm.SelectedTypesRows.Add(vm.TypesRows.Single());
        vm.RemoveSelectedCommand.Execute(null);

        Assert.True(store.SaveCalled);
        Assert.False(store.LastSaved.ContainsKey("types.xml"));
        Assert.False(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "types.xml")));
        Assert.Contains("cfgeconomycore.xml", dialogs.LastErrorMessage);
    }

    [Fact]
    public void RemoveSelected_SaveFailure_DoesNotDeleteFiles()
    {
        FakeFileSystem fs = TypeFilesSystem("types.xml");
        FakeTypesConfigStore store = StoreWith(("types.xml", "types"));
        store.SaveException = new IOException("locked");
        var dialogs = new FakeDialogs { ConfirmResult = true };

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), dialogs,
            fileSystem: fs, typesConfigStore: store);

        vm.SelectedTypesRows.Add(vm.TypesRows.Single());
        vm.RemoveSelectedCommand.Execute(null);

        Assert.True(fs.FileExists(Path.Combine(TypeFilesFolderPath(), "types.xml")));
        Assert.Contains("locked", dialogs.LastErrorMessage);
    }

    [Fact]
    public void SyncEconomyCore_PassesPresetFolderAndAssignments()
    {
        FakeFileSystem fs = TypeFilesSystem("types.xml");
        FakeTypesConfigStore store = StoreWith(("types.xml", "spawnabletypes"));
        var types = new FakeTypesService();

        (PresetTypesViewModel vm, FakeTypesService fakeTypes, _) = CreateTypesVm(
            AppliedConfig(), types, new FakeDialogs(), fileSystem: fs, typesConfigStore: store);

        Assert.True(vm.SyncEconomyCore());
        Assert.Equal(1, fakeTypes.SyncEconomyCoreCalls);
        Assert.Equal(EconomyCoreService.ManagedFolder, fakeTypes.LastEconomyFolder);
        Assert.Equal("db/DZMMV2_Types", fakeTypes.LastEconomyFolder);
        Assert.Equal(TypeFilesFolderPath(), fakeTypes.LastTypeFilesFolder);
        Assert.Equal("spawnabletypes", fakeTypes.LastAssignments!["types.xml"]);
    }

    [Fact]
    public void SyncEconomyCore_CorruptConfig_DoesNotSync()
    {
        var store = new FakeTypesConfigStore
        {
            LoadResult = ConfigLoadResult<Dictionary<string, string>>.Corrupt(),
        };
        var types = new FakeTypesService();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), types, new FakeDialogs(), typesConfigStore: store);

        Assert.False(vm.SyncEconomyCore());
        Assert.Equal(0, types.SyncEconomyCoreCalls);
    }

    [Fact]
    public void PreflightTypesIssues_ReportsUnsynchronizedEconomyBlock()
    {
        var types = new FakeTypesService
        {
            ActiveTypesFolder = EconomyCoreService.ManagedFolder,
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
        var types = new FakeTypesService
        {
            ActiveTypesFolder = EconomyCoreService.ManagedFolder,
            ActiveTypeFiles = new[] { "CF_types.xml" },
            ManagedTypeFileNames = new[] { "CF_types.xml" },
        };
        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types);
        vm.Refresh(Settings(), new[] { "@CF" }, new[] { "@CF" });

        Assert.DoesNotContain(
            vm.PreflightTypesIssues(), i => i.Contains("cfgeconomycore.xml is not synchronized"));
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

    [Fact]
    public void TypesCommands_Enabled_WithMapApplied()
    {
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), new FakeTypesService(), new FakeDialogs());

        Assert.True(vm.ConfigureModCommand.CanExecute("@CF"));
        Assert.True(vm.OpenTypeFilesFolderCommand.CanExecute(null));
        Assert.Equal("Open type_files folder in File Explorer", vm.OpenTypeFilesFolderToolTip);
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
    public async Task ConfigureMod_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService();
        var process = new FakeServerProcessState { Running = true };

        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);

        Assert.False(await vm.ImportTypesAsync("@CF"));

        Assert.Equal(0, types.ImportFilesCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public void RemoveSelected_Blocked_WhenServerRunning()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService();
        var process = new FakeServerProcessState { Running = true };

        PresetTypesViewModel vm = Create(AppliedConfig(), typesService: types, dialogs: dialogs, serverProcess: process);
        vm.SelectedTypesRows.Add(new TypesRowViewModel(
            "CF_types.xml", null, (_, _, _) => true, (_, _, _) => true));

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Equal(0, dialogs.ConfirmCalls);
        Assert.Contains("server is running", dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task ImportTypes_NoXmlFiles_ReturnsFalse_AndDoesNotOpenPicker()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService { DiscoveryFiles = Array.Empty<string>() };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), types, dialogs);

        bool imported = await vm.ImportTypesAsync("@CF");

        Assert.False(imported);
        Assert.Equal(0, dialogs.PickTypeFilesCalls);
        Assert.Equal(1, dialogs.ShowMessageCalls);
        Assert.Contains("No XML files", dialogs.LastMessage);
        Assert.Equal(0, types.ImportFilesCalls);
    }

    [Fact]
    public async Task ImportTypes_Cancelled_ReturnsFalse_AndDoesNotCopy()
    {
        var dialogs = new FakeDialogs { SelectedFiles = null };
        var types = new FakeTypesService { DiscoveryFiles = new[] { @"D:\workshop\@CF\cf_types.xml" } };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), types, dialogs);

        bool imported = await vm.ImportTypesAsync("@CF");

        Assert.False(imported);
        Assert.Equal(1, dialogs.PickTypeFilesCalls);
        Assert.Equal(0, types.ImportFilesCalls);
    }

    [Fact]
    public async Task ImportTypes_Success_ReturnsTrue_AndCopiesSelectedFiles()
    {
        string[] files = { @"D:\workshop\@CF\configs\cf_types.xml" };
        var dialogs = new FakeDialogs { SelectedFiles = files };
        var types = new FakeTypesService { DiscoveryFiles = files };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), types, dialogs);

        bool imported = await vm.ImportTypesAsync("@CF");

        Assert.True(imported);
        Assert.Equal(1, types.ImportFilesCalls);
        Assert.Equal(files[0], Assert.Single(types.LastImportSourceFiles!));
    }

    [Fact]
    public async Task ImportTypes_FailedCopy_ReturnsFalse()
    {
        string[] files = { @"D:\workshop\@CF\cf_types.xml" };
        var dialogs = new FakeDialogs { SelectedFiles = files };
        var types = new FakeTypesService
        {
            DiscoveryFiles = files,
            ImportResult = new TypesOperationResult { Success = false },
        };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), types, dialogs);

        Assert.False(await vm.ImportTypesAsync("@CF"));
    }

    [Fact]
    public async Task ImportTypes_NoAppliedMap_ReturnsFalse_AndDoesNotOpenPicker()
    {
        var dialogs = new FakeDialogs();
        var types = new FakeTypesService { DiscoveryFiles = new[] { @"D:\workshop\@CF\cf_types.xml" } };
        PresetTypesViewModel vm = Create(
            new TypesConfig { CurrentMap = string.Empty }, typesService: types, dialogs: dialogs);

        Assert.False(await vm.ImportTypesAsync("@CF"));
        Assert.Equal(0, dialogs.PickTypeFilesCalls);
    }

    [Fact]
    public async Task ImportTypes_TellsThePickerWhichPresetIsTheTarget()
    {
        string[] files = { @"D:\workshop\@CF\cf_types.xml" };
        var dialogs = new FakeDialogs { SelectedFiles = files };
        var types = new FakeTypesService { DiscoveryFiles = files };
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(AppliedConfig(), types, dialogs);

        await vm.ImportTypesAsync("@CF");

        Assert.Contains(MapName, dialogs.LastTargetDescription);
        Assert.Contains(PresetPaths.DefaultPresetName, dialogs.LastTargetDescription);
    }
}
