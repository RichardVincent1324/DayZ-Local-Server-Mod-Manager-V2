using System.IO;
using DayZModManager.App.ViewModels;

namespace DayZModManager.App.Tests.ViewModels;

public partial class PresetTypesViewModelTests
{
    private static string LiveStoragePath(int instanceId) =>
        Path.Combine(ServerPath, "mpmissions", MapName, $"storage_{instanceId}");

    [Fact]
    public void LiveWorldStatus_IsGreen_WhenActivePresetStorageExists()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddDirectory(LiveStoragePath(1));
        fileSystem.AddDirectory(LiveStoragePath(5));
        var saves = new FakeSaveGameService { StorageInstanceIds = new[] { 5 } };

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            saveGameService: saves, fileSystem: fileSystem);

        Assert.True(vm.LiveWorldExists);
        Assert.Contains("Live-world indicator: Green = Active live world exists", vm.LiveWorldStatusToolTip);
        Assert.Contains("Associated storage folder: storage_1", vm.LiveWorldStatusToolTip);
        Assert.Contains("Add-Save and Wipe Current World are available for this preset.", vm.LiveWorldStatusToolTip);
        Assert.DoesNotContain("storage_5", vm.LiveWorldStatusToolTip);
    }

    [Fact]
    public void LiveWorldStatus_IsGray_WhenOnlyOrphanStorageExists()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddDirectory(LiveStoragePath(5));
        var saves = new FakeSaveGameService { StorageInstanceIds = new[] { 5 } };

        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(),
            saveGameService: saves, fileSystem: fileSystem);

        Assert.False(vm.LiveWorldExists);
        Assert.Contains("Live-world indicator: Gray = No active live-world exists", vm.LiveWorldStatusToolTip);
        Assert.Contains("Expected storage folder: storage_1", vm.LiveWorldStatusToolTip);
        Assert.Contains("Start server at least once to generate this world folder.", vm.LiveWorldStatusToolTip);
        Assert.Contains("Add-Save and Wipe Current World are unavailable until storage exists.", vm.LiveWorldStatusToolTip);
        Assert.DoesNotContain("storage_5", vm.LiveWorldStatusToolTip);
    }

    [Fact]
    public void RefreshLiveWorldStatus_TracksActiveStorageFolderChanges()
    {
        var fileSystem = new FakeFileSystem();
        (PresetTypesViewModel vm, _, _) = CreateTypesVm(
            AppliedConfig(), new FakeTypesService(), new FakeDialogs(), fileSystem: fileSystem);

        Assert.False(vm.LiveWorldExists);

        fileSystem.AddDirectory(LiveStoragePath(1));
        vm.RefreshLiveWorldStatus();
        Assert.True(vm.LiveWorldExists);

        fileSystem.DeleteDirectory(LiveStoragePath(1), recursive: true);
        vm.RefreshLiveWorldStatus();
        Assert.False(vm.LiveWorldExists);
    }
}
