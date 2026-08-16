using System.IO;
using DayZModManager.App.Services;
using DayZModManager.App.ViewModels;
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
            BatFileName = "LocalServer.bat",
            DataDirectory = @"D:\custom-data",
        };

        vm.Load(settings);

        Assert.False(vm.IsDirty);
        Assert.Equal(@"D:\workshop", vm.WorkshopPath);
        Assert.Equal(@"D:\server", vm.ServerPath);
        Assert.Equal("LocalServer.bat", vm.BatFileName);
        Assert.Equal(@"D:\custom-data", vm.DataDirectory);
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
    public void ToSettings_RoundTripsValues()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = "ws", ServerPath = "srv", BatFileName = "b.bat" });

        vm.WorkshopPath = "ws2";

        Settings result = vm.ToSettings();

        Assert.Equal("ws2", result.WorkshopPath);
        Assert.Equal("srv", result.ServerPath);
        Assert.Equal("b.bat", result.BatFileName);
    }

    [Fact]
    public void SettingDataDirectory_MarksDirty()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { DataDirectory = @"D:\a" });

        vm.DataDirectory = @"D:\b";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ToSettings_IncludesDataDirectory()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings());
        vm.DataDirectory = @"D:\data";

        Assert.Equal(@"D:\data", vm.ToSettings().DataDirectory);
    }

    [Fact]
    public void EffectiveDataDirectory_WithoutOverride_UsesServerPathDefault()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { ServerPath = @"D:\DayZServer" });

        Assert.Equal(
            Path.Combine(@"D:\DayZServer", "DayZ-Local-Server-Mod-Manager-Data"),
            vm.EffectiveDataDirectory);
    }

    [Fact]
    public void EffectiveDataDirectory_WithOverride_UsesOverride()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { ServerPath = @"D:\DayZServer" });

        vm.DataDirectory = @"D:\custom";

        Assert.Equal(@"D:\custom", vm.EffectiveDataDirectory);
    }

    [Fact]
    public void BrowseDataDirectory_SetsPath_AndRaisesDataDirectoryChanged()
    {
        var dialogs = new FakeDialogs { Folder = @"D:\picked-data" };
        var vm = new SettingsViewModel(dialogs);
        bool raised = false;
        vm.DataDirectoryChanged += () => raised = true;

        vm.BrowseDataDirectoryCommand.Execute(null);

        Assert.Equal(@"D:\picked-data", vm.DataDirectory);
        Assert.True(raised);
    }

    [Fact]
    public void MarkApplied_ClearsDirtyFlag()
    {
        var vm = new SettingsViewModel(new FakeDialogs());
        vm.Load(new Settings { WorkshopPath = "ws", ServerPath = "srv", BatFileName = "b.bat" });

        vm.WorkshopPath = "ws2";
        Assert.True(vm.IsDirty);

        vm.MarkApplied(vm.ToSettings());

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void BrowseWorkshop_SetsPath_AndRaisesApplyRequested()
    {
        var dialogs = new FakeDialogs { Folder = @"D:\picked" };
        var vm = new SettingsViewModel(dialogs);
        bool raised = false;
        vm.ApplyRequested += () => raised = true;

        vm.BrowseWorkshopCommand.Execute(null);

        Assert.Equal(@"D:\picked", vm.WorkshopPath);
        Assert.True(raised);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? Folder { get; set; }

        public void ShowMessage(string message, string title, bool isError = false) { }

        public bool Confirm(string message, string title) => true;

        public string? PickFolder(string title = "Select a folder") => Folder;

        public string? PickFile(string title, string filter, string initialDirectory) => null;

        public IReadOnlyList<string>? PickTypeFiles(string modName, IReadOnlyList<string> files) => null;
    }
}
