using System.IO;
using DayZModManager.App.Services;
using DayZModManager.App.ViewModels;
using DayZModManager.Core;
using DayZModManager.Core.Models;

namespace DayZModManager.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public void Load_ReflectsSettings_AndIsNotDirty()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        var settings = new Settings
        {
            WorkshopPath = @"D:\workshop",
            ServerPath = @"D:\server",
            BatchFile = "LocalServer.bat",
            AutoCleanServerLogs = true,
        };

        vm.Load(settings);

        Assert.False(vm.IsDirty);
        Assert.Equal(@"D:\workshop", vm.WorkshopPath);
        Assert.Equal(@"D:\server", vm.ServerPath);
        Assert.Equal("LocalServer.bat", vm.BatchFile);
        Assert.True(vm.AutoCleanServerLogs);
    }

    [Fact]
    public void SettingWorkshopPath_MarksDirty()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = @"D:\workshop" });

        vm.WorkshopPath = @"D:\other";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void TogglingAutoCleanServerLogs_MarksDirty_UntilApplied()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings());
        Assert.False(vm.AutoCleanServerLogs);

        vm.AutoCleanServerLogs = true;

        Assert.True(vm.IsDirty);
        vm.MarkApplied(vm.ToSettings());
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void ClickValueWrite_ReflectsCheckedAndUncheckedStates()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings());
        Assert.False(vm.IsDirty);

        // Same write the checkbox Click handler performs (box.IsChecked == true).
        bool checkedValue = true;
        vm.AutoCleanServerLogs = checkedValue;

        Assert.True(vm.AutoCleanServerLogs);
        Assert.True(vm.IsDirty);
        Assert.True(vm.ToSettings().AutoCleanServerLogs);

        vm.MarkApplied(vm.ToSettings());
        checkedValue = false;
        vm.AutoCleanServerLogs = checkedValue;

        Assert.False(vm.AutoCleanServerLogs);
        Assert.True(vm.IsDirty);
        Assert.False(vm.ToSettings().AutoCleanServerLogs);
    }

    [Fact]
    public void TogglingAutoClean_WithPathsConfigured_RaisesApplyRequested()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = @"D:\workshop", ServerPath = @"D:\server", BatchFile = "b.bat" });
        int raises = 0;
        vm.ApplyRequested += () => raises++;

        vm.AutoCleanServerLogs = true;
        Assert.Equal(1, raises);

        vm.AutoCleanServerLogs = false;
        Assert.Equal(2, raises);
    }

    [Fact]
    public void TogglingAutoClean_WithoutBatchFile_DoesNotAutoApply()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = @"D:\workshop", ServerPath = @"D:\server" });
        int raises = 0;
        vm.ApplyRequested += () => raises++;

        vm.AutoCleanServerLogs = true;

        Assert.Equal(0, raises);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void TogglingAutoClean_WithoutPathsConfigured_DoesNotAutoApply()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings());
        int raises = 0;
        vm.ApplyRequested += () => raises++;

        vm.AutoCleanServerLogs = true;

        Assert.Equal(0, raises);
        Assert.True(vm.IsDirty);
        Assert.True(vm.ToSettings().AutoCleanServerLogs);
    }

    [Fact]
    public void SettingSameAutoCleanValueTwice_RaisesOnlyOnce()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = @"D:\workshop", ServerPath = @"D:\server", BatchFile = "b.bat" });
        int raises = 0;
        vm.ApplyRequested += () => raises++;

        vm.AutoCleanServerLogs = true;
        vm.AutoCleanServerLogs = true;

        Assert.Equal(1, raises);
    }

    [Fact]
    public void ToSettings_RoundTripsValues()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = "ws", ServerPath = "srv", BatchFile = "b.bat" });

        vm.WorkshopPath = "ws2";
        vm.AutoCleanServerLogs = true;

        Settings result = vm.ToSettings();

        Assert.Equal("ws2", result.WorkshopPath);
        Assert.Equal("srv", result.ServerPath);
        Assert.Equal("b.bat", result.BatchFile);
        Assert.True(result.AutoCleanServerLogs);
    }

    [Fact]
    public void EffectiveDataDirectory_WithServerPath_UsesServerPathDefault()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { ServerPath = @"D:\DayZServer" });

        Assert.Equal(
            Path.Combine(@"D:\DayZServer", AppPaths.DataDirectoryName),
            vm.EffectiveDataDirectory);
    }

    [Fact]
    public void EffectiveDataDirectory_WithoutServerPath_UsesLegacyDirectory()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings());

        Assert.Equal(AppPaths.LegacyDirectory(), vm.EffectiveDataDirectory);
    }

    [Fact]
    public void MarkApplied_ClearsDirtyFlag()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = "ws", ServerPath = "srv", BatchFile = "b.bat" });

        vm.WorkshopPath = "ws2";
        Assert.True(vm.IsDirty);

        vm.MarkApplied(vm.ToSettings());

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void BrowseWorkshop_SetsPath_ButSkipsApply_WhenServerPathNotSet()
    {
        var dialogs = new FakeDialogs { Folder = @"D:\picked" };
        var vm = new SettingsViewModel(dialogs);
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseWorkshopCommand.Execute(null);

        Assert.Equal(@"D:\picked", vm.WorkshopPath);
        Assert.False(raised);
    }

    [Fact]
    public void BrowseWorkshop_RaisesApplyRequested_WhenServerAndBatchSet()
    {
        var dialogs = new FakeDialogs { Folder = @"D:\picked" };
        var vm = new SettingsViewModel(dialogs);
        vm.Load(new Settings { ServerPath = @"D:\server", BatchFile = "b.bat" });
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseWorkshopCommand.Execute(null);

        Assert.Equal(@"D:\picked", vm.WorkshopPath);
        Assert.True(raised);
    }

    [Fact]
    public void BrowseWorkshop_SkipsApply_WhenBatchMissing()
    {
        var dialogs = new FakeDialogs { Folder = @"D:\picked" };
        var vm = new SettingsViewModel(dialogs);
        vm.Load(new Settings { ServerPath = @"D:\server" });
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseWorkshopCommand.Execute(null);

        Assert.Equal(@"D:\picked", vm.WorkshopPath);
        Assert.False(raised);
    }

    [Fact]
    public void BrowseServer_SetsPath_ButSkipsApply_WhenWorkshopPathNotSet()
    {
        var dialogs = new FakeDialogs { Folder = @"D:\server" };
        var vm = new SettingsViewModel(dialogs);
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseServerCommand.Execute(null);

        Assert.Equal(@"D:\server", vm.ServerPath);
        Assert.False(raised);
    }

    [Fact]
    public void BrowseServer_RaisesApplyRequested_WhenWorkshopAndBatchSet()
    {
        var dialogs = new FakeDialogs { Folder = @"D:\server" };
        var vm = new SettingsViewModel(dialogs);
        vm.Load(new Settings { WorkshopPath = @"D:\workshop", BatchFile = "b.bat" });
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseServerCommand.Execute(null);

        Assert.Equal(@"D:\server", vm.ServerPath);
        Assert.True(raised);
    }

    [Fact]
    public void BrowseBatchFile_SetsPath_ButSkipsApply_WhenPathsNotSet()
    {
        var dialogs = new FakeDialogs { File = @"D:\start.bat" };
        var vm = new SettingsViewModel(dialogs);
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseBatchFileCommand.Execute(null);

        Assert.Equal(@"D:\start.bat", vm.BatchFile);
        Assert.False(raised);
    }

    [Fact]
    public void BrowseBatchFile_RaisesApplyRequested_WhenPathsSet()
    {
        var dialogs = new FakeDialogs { File = @"D:\start.bat" };
        var vm = new SettingsViewModel(dialogs);
        vm.Load(new Settings { WorkshopPath = @"D:\workshop", ServerPath = @"D:\server" });
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseBatchFileCommand.Execute(null);

        Assert.Equal(@"D:\start.bat", vm.BatchFile);
        Assert.True(raised);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? Folder { get; set; }

        public string? File { get; set; }

        public void ShowMessage(string message, string title, bool isError = false) { }

        public bool Confirm(string message, string title) => true;

        public bool ConfirmWithWarning(string message, string title, string warning, string note = "") => true;

        public LoadSaveConfirmation ConfirmLoadSave(
            string message, string title, string warning, string note, bool offerTypesRestore) =>
            new(true, false);

        public string? AskText(string title, string prompt, string defaultValue = "") => defaultValue;

        public string? PickFolder(string title = "Select a folder") => Folder;

        public string? PickFile(string title, string filter, string initialDirectory) => File;

        public IReadOnlyList<string>? PickTypeFiles(string modName, IReadOnlyList<string> files, IReadOnlySet<string>? activeFiles = null) => null;
    }
}
