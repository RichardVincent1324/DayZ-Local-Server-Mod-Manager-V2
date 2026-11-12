using DayZModManager.App.Services;
using DayZModManager.App.ViewModels;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Tests.ViewModels;

public class ModsViewModelTests
{
    private static ModsViewModel Create(
        IReadOnlyList<string> workshopMods,
        IReadOnlyList<string> loadedMods,
        FakeDialogs? dialogs = null,
        bool pathAvailable = true)
    {
        var state = new ModState();
        if (loadedMods.Count > 0)
        {
            state.ReplaceLoadedMods(loadedMods);
        }

        var viewModel = new ModsViewModel(
            state,
            new FakeDiscovery(workshopMods) { PathAvailable = pathAvailable },
            new LogViewModel(),
            dialogs ?? new FakeDialogs());
        viewModel.Initialize();
        return viewModel;
    }

    private static string[] Names(IEnumerable<ModItemViewModel> items) => items.Select(i => i.Name).ToArray();

    private static string[] SearchNames(ModsViewModel vm) => vm.SearchResults.Select(i => i.Name).ToArray();

    private static void Refresh(ModsViewModel vm, string workshopPath) =>
        vm.RefreshAsync(workshopPath).GetAwaiter().GetResult();

    [Fact]
    public void Refresh_PopulatesLoadedAndAvailableLists()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B", "@C" }, new[] { "@B" });

        Refresh(vm, @"D:\workshop");

        Assert.Equal(new[] { "@B" }, Names(vm.LoadedItems));
        Assert.Equal(new[] { "@A", "@C" }, Names(vm.AvailableItems));
    }

    [Fact]
    public void Refresh_WithNoMods_WarnsAboutWorkshopPath()
    {
        var state = new ModState();
        var log = new LogViewModel();
        var vm = new ModsViewModel(
            state, new FakeDiscovery(Array.Empty<string>()), log, new FakeDialogs());
        vm.Initialize();

        Refresh(vm, @"D:\SteamLibrary\steamapps\common\DayZ");

        Assert.Contains(
            log.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("!Workshop"));
    }

    [Fact]
    public void LoadSelected_MovesModsToLoaded_AndMarksDirty()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B" }, Array.Empty<string>());
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.SelectedAvailableItems.Add(vm.AvailableItems[0]);
        vm.LoadSelectedCommand.Execute(null);

        Assert.Contains(vm.LoadedItems, i => i.Name == "@A");
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ReplaceLoadedMods_ReplacesListAndMarksDirty()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B", "@C" }, new[] { "@A" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.ReplaceLoadedMods(new[] { "@B", "@C" });

        Assert.Equal(new[] { "@B", "@C" }, Names(vm.LoadedItems));
        Assert.Equal(new[] { "@A" }, Names(vm.AvailableItems));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void UnloadSelected_RemovesModsFromLoaded()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B" }, new[] { "@A", "@B" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.SelectedLoadedItems.Add(vm.LoadedItems[0]);
        vm.UnloadSelectedCommand.Execute(null);

        Assert.Equal(new[] { "@B" }, Names(vm.LoadedItems));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Reorder_MovesModToTargetIndex()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B", "@C", "@D" }, new[] { "@A", "@B", "@C", "@D" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.ReorderCommand.Execute(new ReorderRequest("@B", 2));

        Assert.Equal(new[] { "@A", "@C", "@B", "@D" }, Names(vm.LoadedItems));
        Assert.Equal(new[] { 1, 2, 3, 4 }, vm.LoadedItems.Select(i => i.Position));
    }

    [Fact]
    public void Refresh_NeverPrompts_AndMarksMissingMods()
    {
        var dialogs = new FakeDialogs();
        ModsViewModel vm = Create(new[] { "@A" }, new[] { "@A", "@Ghost" }, dialogs);

        Refresh(vm, @"D:\workshop");

        Assert.Equal(0, dialogs.ConfirmWithWarningCalls);
        Assert.Equal(0, dialogs.ConfirmCalls);
        Assert.Contains(vm.LoadedItems, i => i.Name == "@Ghost" && i.IsMissing);
    }

    [Fact]
    public void Refresh_UnavailableWorkshopPath_KeepsPreviousStateAndMarksNothingMissing()
    {
        var state = new ModState();
        state.ReplaceLoadedMods(new[] { "@A" });
        var discovery = new FakeDiscovery(new[] { "@A", "@B" });
        var dialogs = new FakeDialogs();
        var vm = new ModsViewModel(state, discovery, new LogViewModel(), dialogs);
        vm.Initialize();
        Refresh(vm, @"D:\workshop");

        discovery.PathAvailable = false;
        Refresh(vm, @"D:\workshop");

        Assert.Equal(new[] { "@A" }, Names(vm.LoadedItems));
        Assert.Equal(new[] { "@B" }, Names(vm.AvailableItems));
        Assert.DoesNotContain(vm.LoadedItems, i => i.IsMissing);
        Assert.Equal(0, dialogs.ConfirmCalls);
    }

    [Fact]
    public void RemoveMissing_RemovesEveryMissingMod_WhenConfirmed()
    {
        var dialogs = new FakeDialogs { ConfirmResult = true };
        ModsViewModel vm = Create(new[] { "@A" }, new[] { "@A", "@GhostB", "@GhostA" }, dialogs);
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.RemoveMissingCommand.Execute(null);

        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Equal(new[] { "@A" }, Names(vm.LoadedItems));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void RemoveMissing_KeepsMods_WhenDeclined()
    {
        var dialogs = new FakeDialogs { ConfirmResult = false };
        ModsViewModel vm = Create(new[] { "@A" }, new[] { "@A", "@Ghost" }, dialogs);
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.RemoveMissingCommand.Execute(null);

        Assert.Equal(1, dialogs.ConfirmCalls);
        Assert.Equal(new[] { "@A", "@Ghost" }, Names(vm.LoadedItems));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void RemoveMissingCommand_DisabledWhenNothingIsMissing()
    {
        ModsViewModel vm = Create(new[] { "@A" }, new[] { "@A" });
        Refresh(vm, @"D:\workshop");

        Assert.False(vm.RemoveMissingCommand.CanExecute(null));
    }

    [Fact]
    public void RemoveMissingCommand_EnabledWhenModsAreMissing()
    {
        ModsViewModel vm = Create(new[] { "@A" }, new[] { "@A", "@Ghost" });
        Refresh(vm, @"D:\workshop");

        Assert.True(vm.RemoveMissingCommand.CanExecute(null));
    }

    [Fact]
    public void SearchText_MatchesLoadedAndAvailableMods()
    {
        ModsViewModel vm = Create(new[] { "@Alpha", "@Bravo" }, new[] { "@Bravo" });
        Refresh(vm, @"D:\workshop");

        vm.SearchText = "a";

        Assert.Equal(new[] { "@Alpha", "@Bravo" }, SearchNames(vm));
    }

    [Fact]
    public void SearchResults_MarkMissingMods()
    {
        ModsViewModel vm = Create(new[] { "@Alpha" }, new[] { "@Alpha", "@Ghost" });
        Refresh(vm, @"D:\workshop");

        vm.SearchText = "ghost";

        SearchResultViewModel result = Assert.Single(vm.SearchResults);
        Assert.Equal("@Ghost", result.Name);
        Assert.True(result.IsLoaded);
        Assert.True(result.IsMissing);
    }

    [Fact]
    public void ToggleSelectedCommand_LoadsAvailableMod()
    {
        ModsViewModel vm = Create(new[] { "@Alpha", "@Bravo" }, new[] { "@Bravo" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.SearchText = "alpha";
        vm.SelectedSearchResult = Assert.Single(vm.SearchResults);

        vm.ToggleSelectedCommand.Execute(null);

        Assert.Equal(new[] { "@Bravo", "@Alpha" }, Names(vm.LoadedItems));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ToggleSelectedCommand_UnloadsLoadedMod()
    {
        ModsViewModel vm = Create(new[] { "@Alpha", "@Bravo" }, new[] { "@Bravo" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.SearchText = "bravo";
        vm.SelectedSearchResult = Assert.Single(vm.SearchResults);
        Assert.True(vm.SelectedSearchResult.IsLoaded);

        vm.ToggleSelectedCommand.Execute(null);

        Assert.Empty(Names(vm.LoadedItems));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ToggleSelectedCommand_DisabledWithoutSelection()
    {
        ModsViewModel vm = Create(new[] { "@Alpha" }, Array.Empty<string>());
        Refresh(vm, @"D:\workshop");

        vm.SearchText = "alpha";

        Assert.False(vm.ToggleSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void ToggleButtonText_DefaultsToToggle()
    {
        ModsViewModel vm = Create(new[] { "@Alpha" }, Array.Empty<string>());
        Refresh(vm, @"D:\workshop");

        Assert.Equal("Toggle", vm.ToggleButtonText);
    }

    [Fact]
    public void ToggleButtonText_ShowsLoadForAvailable()
    {
        ModsViewModel vm = Create(new[] { "@Alpha", "@Bravo" }, new[] { "@Bravo" });
        Refresh(vm, @"D:\workshop");

        vm.SearchText = "alpha";
        vm.SelectedSearchResult = Assert.Single(vm.SearchResults);

        Assert.Equal("Load", vm.ToggleButtonText);
    }

    [Fact]
    public void ToggleButtonText_ShowsUnloadForLoaded()
    {
        ModsViewModel vm = Create(new[] { "@Alpha", "@Bravo" }, new[] { "@Bravo" });
        Refresh(vm, @"D:\workshop");

        vm.SearchText = "bravo";
        vm.SelectedSearchResult = Assert.Single(vm.SearchResults);

        Assert.Equal("Unload", vm.ToggleButtonText);
    }

    [Fact]
    public void ToggleButtonText_FlipsAfterToggle()
    {
        ModsViewModel vm = Create(new[] { "@Alpha", "@Bravo" }, new[] { "@Bravo" });
        Refresh(vm, @"D:\workshop");

        vm.SearchText = "alpha";
        vm.SelectedSearchResult = Assert.Single(vm.SearchResults);
        Assert.Equal("Load", vm.ToggleButtonText);

        vm.ToggleSelectedCommand.Execute(null);

        Assert.Equal("Unload", vm.ToggleButtonText);
    }

    [Fact]
    public void MarkApplied_ClearsDirtyFlag()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B" }, new[] { "@A" });
        Refresh(vm, @"D:\workshop");

        vm.SelectedAvailableItems.Add(vm.AvailableItems[0]);
        vm.LoadSelectedCommand.Execute(null);
        Assert.True(vm.IsDirty);

        vm.MarkApplied();

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void MarkApplied_WithSnapshot_KeepsEditsMadeAfterSnapshotDirty()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B" }, new[] { "@A" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        // Apply snapshot is ["@A"], but the user loads @B while Apply is in flight.
        vm.SelectedAvailableItems.Add(vm.AvailableItems[0]);
        vm.LoadSelectedCommand.Execute(null);

        vm.MarkApplied(new[] { "@A" });

        Assert.Equal(new[] { "@A", "@B" }, Names(vm.LoadedItems));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void MarkApplied_WithSnapshot_MatchingCurrentList_ClearsDirty()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B" }, new[] { "@A" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.MarkApplied(new[] { "@A" });

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void MarkApplied_WithUpdatedSnapshot_ClearsDirty()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B" }, new[] { "@A" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.SelectedAvailableItems.Add(vm.AvailableItems[0]);
        vm.LoadSelectedCommand.Execute(null);

        vm.MarkApplied(new[] { "@A", "@B" });

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Refresh_MarksMissingMods()
    {
        ModsViewModel vm = Create(new[] { "@A" }, new[] { "@A", "@Ghost" });

        Refresh(vm, @"D:\workshop");

        Assert.Contains(vm.LoadedItems, i => i.Name == "@Ghost" && i.IsMissing);
    }

    [Fact]
    public void LoadSelected_PreservesUnchangedItemInstances()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B" }, Array.Empty<string>());
        Refresh(vm, @"D:\workshop");

        ModItemViewModel remaining = vm.AvailableItems[1];

        vm.SelectedAvailableItems.Add(vm.AvailableItems[0]);
        vm.LoadSelectedCommand.Execute(null);

        Assert.Equal(new[] { "@B" }, Names(vm.AvailableItems));
        Assert.Same(remaining, vm.AvailableItems[0]);
    }

    [Fact]
    public void SearchResults_ShowLoadPositions_AndNullForAvailableMods()
    {
        ModsViewModel vm = Create(new[] { "@Alpha", "@Bravo" }, new[] { "@Bravo" });
        Refresh(vm, @"D:\workshop");

        vm.SearchText = "a";

        Assert.Equal(1, vm.SearchResults.Single(r => r.Name == "@Bravo").Position);
        Assert.Null(vm.SearchResults.Single(r => r.Name == "@Alpha").Position);
    }

    [Fact]
    public void SearchResults_RefreshPositions_AfterReorder()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B", "@C" }, new[] { "@A", "@B", "@C" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.ReorderCommand.Execute(new ReorderRequest("@C", 0));

        Assert.Equal(1, vm.SearchResults.Single(r => r.Name == "@C").Position);
        Assert.Equal(2, vm.SearchResults.Single(r => r.Name == "@A").Position);
        Assert.Equal(3, vm.SearchResults.Single(r => r.Name == "@B").Position);
    }

    private sealed class FakeDiscovery : IModDiscoveryService
    {
        public FakeDiscovery(IReadOnlyList<string> mods) => Mods = mods;

        public IReadOnlyList<string> Mods { get; set; }

        public bool PathAvailable { get; set; } = true;

        public ModDiscoveryResult DiscoverWorkshopMods(string workshopPath) =>
            PathAvailable
                ? new ModDiscoveryResult(true, Mods)
                : ModDiscoveryResult.Unavailable;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public int ConfirmWithWarningCalls { get; private set; }

        public string? LastMessage { get; private set; }

        public string? LastWarning { get; private set; }

        public int ConfirmCalls { get; private set; }

        public string? LastConfirmMessage { get; private set; }

        public bool ConfirmWithWarning(string message, string title, string warning, string note = "")
        {
            ConfirmWithWarningCalls++;
            LastMessage = message;
            LastWarning = warning;
            return ConfirmResult;
        }

        public void ShowMessage(string message, string title, bool isError = false)
        {
        }

        public bool Confirm(string message, string title)
        {
            ConfirmCalls++;
            LastConfirmMessage = message;
            return ConfirmResult;
        }

        public void ShowWarning(string message, string title, string warning, string note = "")
        {
        }

        public void ShowAlert(string message, string title, string warning, string note = "")
        {
        }

        public string? PickFolder(string title = "Select a folder") => null;

        public string? PickFile(string title, string filter, string initialDirectory) => null;

        public IReadOnlyList<string>? PickTypeFiles(
            string modName, IReadOnlyList<string> files, string? modFolderPath = null) => null;

        public string? AskText(string title, string prompt, string defaultValue = "") => null;

        public AddPresetRequest? AskAddPreset(string mapName) => null;

        public DuplicatePresetRequest? AskDuplicatePreset(
            string sourcePresetName, IReadOnlyList<string> maps, string defaultMap) => null;
    }
}
