using DayZModManager.App.ViewModels;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Tests.ViewModels;

public class ModsViewModelTests
{
    private static ModsViewModel Create(IReadOnlyList<string> workshopMods, IReadOnlyList<string> loadedMods)
    {
        var state = new ModState();
        if (loadedMods.Count > 0)
        {
            state.ReplaceLoadedMods(loadedMods);
        }

        var viewModel = new ModsViewModel(state, new FakeDiscovery(workshopMods), new LogViewModel());
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
    }

    [Fact]
    public void ApplyOrder_ReplacesLoadedOrder_AndMarksDirty()
    {
        ModsViewModel vm = Create(new[] { "@A", "@B", "@C" }, new[] { "@A", "@B", "@C" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.ApplyOrder(new[] { "@C", "@A", "@B" });

        Assert.Equal(new[] { "@C", "@A", "@B" }, Names(vm.LoadedItems));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void RemoveMissing_UnloadsModsNoLongerInWorkshop()
    {
        ModsViewModel vm = Create(new[] { "@A" }, new[] { "@A", "@Ghost" });
        Refresh(vm, @"D:\workshop");
        vm.MarkApplied();

        vm.RemoveMissingCommand.Execute(null);

        Assert.Equal(new[] { "@A" }, Names(vm.LoadedItems));
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

    private sealed class FakeDiscovery : IModDiscoveryService
    {
        private readonly IReadOnlyList<string> _mods;

        public FakeDiscovery(IReadOnlyList<string> mods) => _mods = mods;

        public IReadOnlyList<string> DiscoverWorkshopMods(string workshopPath) => _mods;
    }
}
