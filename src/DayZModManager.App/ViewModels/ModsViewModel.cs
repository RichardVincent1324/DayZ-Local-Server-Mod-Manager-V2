using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>
/// Backs the "Mods Manage" page. Maintains the desired loaded-mod set in a
/// <see cref="ModState"/> and exposes the two lists (loaded / available) plus a
/// unified search over every known mod. All edits are in-memory only; the Apply
/// operation persists them.
/// </summary>
public sealed class ModsViewModel : ViewModelBase
{
    private readonly ModState _state;
    private readonly IModDiscoveryService _discovery;
    private readonly LogViewModel _log;

    private List<string> _savedLoaded = new();
    private string _workshopPath = string.Empty;
    private string _searchText = string.Empty;
    private SearchResultViewModel? _selectedSearchResult;
    private bool _isDirty;

    public ModsViewModel(ModState state, IModDiscoveryService discovery, LogViewModel log)
    {
        _state = state;
        _discovery = discovery;
        _log = log;

        SelectedLoadedItems.CollectionChanged += OnSelectionChanged;
        SelectedAvailableItems.CollectionChanged += OnSelectionChanged;
    }

    public ObservableCollection<ModItemViewModel> LoadedItems { get; } = new();

    public ObservableCollection<ModItemViewModel> AvailableItems { get; } = new();

    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = new();

    /// <summary>Selected entries in the Loaded list (kept in sync by the UI).</summary>
    public ObservableCollection<ModItemViewModel> SelectedLoadedItems { get; } = new();

    /// <summary>Selected entries in the Available list (kept in sync by the UI).</summary>
    public ObservableCollection<ModItemViewModel> SelectedAvailableItems { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                if (string.IsNullOrWhiteSpace(_searchText))
                {
                    SelectedSearchResult = null;
                }

                RefreshSearch();
            }
        }
    }

    public bool HasSearch => !string.IsNullOrWhiteSpace(_searchText);

    public SearchResultViewModel? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (SetField(ref _selectedSearchResult, value))
            {
                ToggleSelectedCommand?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ToggleButtonText));
            }
        }
    }

    public string ToggleButtonText =>
        SelectedSearchResult is null ? "Toggle"
        : SelectedSearchResult.IsLoaded ? "Unload"
        : "Load";

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    public bool CanSelectAll => SelectedLoadedItems.Count > 0 || SelectedAvailableItems.Count > 0;

    public string LoadedCountText => $"Loaded Mods ({LoadedItems.Count})";

    public string AvailableCountText => $"Available Mods ({AvailableItems.Count})";

    public RelayCommand LoadSelectedCommand { get; private set; } = null!;
    public RelayCommand UnloadSelectedCommand { get; private set; } = null!;
    public RelayCommand MoveUpCommand { get; private set; } = null!;
    public RelayCommand MoveDownCommand { get; private set; } = null!;
    public AsyncRelayCommand RefreshCommand { get; private set; } = null!;
    public RelayCommand RemoveMissingCommand { get; private set; } = null!;
    public RelayCommand<ReorderRequest> ReorderCommand { get; private set; } = null!;
    public RelayCommand ToggleSelectedCommand { get; private set; } = null!;

    /// <summary>Wires up commands (called once after construction).</summary>
    public void Initialize()
    {
        LoadSelectedCommand = new RelayCommand(LoadSelected);
        UnloadSelectedCommand = new RelayCommand(UnloadSelected);
        MoveUpCommand = new RelayCommand(MoveUp, () => CanMoveSelected);
        MoveDownCommand = new RelayCommand(MoveDown, () => CanMoveSelected);
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(_workshopPath));
        RemoveMissingCommand = new RelayCommand(RemoveMissing);
        ReorderCommand = new RelayCommand<ReorderRequest>(Reorder);
        ToggleSelectedCommand = new RelayCommand(ToggleSelected, () => SelectedSearchResult is not null);
    }

    /// <summary>Re-discovers workshop mods on a background thread to keep the UI responsive.</summary>
    public async Task RefreshAsync(string workshopPath)
    {
        _workshopPath = workshopPath;

        IReadOnlyList<string> workshopMods;
        try
        {
            workshopMods = await Task.Run(() => _discovery.DiscoverWorkshopMods(workshopPath));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to discover workshop mods: {ex.Message}");
            return;
        }

        ApplyDiscovery(workshopMods);
    }

    /// <summary>Marks the current state as applied (clears the dirty flag).</summary>
    public void MarkApplied()
    {
        _savedLoaded = _state.LoadedMods.ToList();
        IsDirty = false;
    }

    private void ApplyDiscovery(IReadOnlyList<string> workshopMods)
    {
        _state.SetWorkshopMods(workshopMods);
        _log.Info($"Found {workshopMods.Count} mod(s) in workshop.");

        int missing = _state.MissingMods.Count;
        if (missing > 0)
        {
            _log.Warning($"{missing} loaded mod(s) are missing from the workshop: {string.Join(", ", _state.MissingMods)}");
        }

        Rebuild();
    }

    private bool CanMoveSelected => SelectedLoadedItems.Count == 0 || SelectedLoadedItems.Count < LoadedItems.Count;

    private void LoadSelected()
    {
        List<ModItemViewModel> items = SelectedAvailableItems.ToList();
        if (items.Count == 0)
        {
            _log.Warning("Select one or more mods from Available Mods first.");
            return;
        }

        foreach (ModItemViewModel item in items)
        {
            _state.Load(item.Name);
        }

        _log.Info($"Loaded {items.Count} mod(s).");
        Rebuild();
    }

    private void UnloadSelected()
    {
        List<ModItemViewModel> items = SelectedLoadedItems.ToList();
        if (items.Count == 0)
        {
            _log.Warning("Select one or more mods from Loaded Mods first.");
            return;
        }

        foreach (ModItemViewModel item in items)
        {
            _state.Unload(item.Name);
        }

        _log.Info($"Unloaded {items.Count} mod(s).");
        Rebuild();
    }

    private void RemoveMissing()
    {
        IReadOnlyList<string> missing = _state.MissingMods;
        if (missing.Count == 0)
        {
            _log.Warning("No missing mods to remove.");
            return;
        }

        foreach (string name in missing)
        {
            _state.Unload(name);
        }

        _log.Info($"Removed {missing.Count} missing mod(s).");
        Rebuild();
    }

    private void MoveUp()
    {
        if (SelectedLoadedItems.Count == 0)
        {
            _log.Warning("Select one or more loaded mods to move.");
            return;
        }

        var selected = new HashSet<string>(SelectedLoadedItems.Select(i => i.Name), StringComparer.Ordinal);

        // No-op when the selected block already touches the top of the load order.
        if (FindFirstSelectedIndex(selected) == 0)
        {
            return;
        }

        foreach (string name in _state.LoadedMods.ToList())
        {
            if (selected.Contains(name))
            {
                _state.MoveUp(name);
            }
        }

        RefreshLoaded();
        UpdateDirtyFlag();
    }

    private void MoveDown()
    {
        if (SelectedLoadedItems.Count == 0)
        {
            _log.Warning("Select one or more loaded mods to move.");
            return;
        }

        var selected = new HashSet<string>(SelectedLoadedItems.Select(i => i.Name), StringComparer.Ordinal);

        // No-op when the selected block already touches the bottom of the load order.
        if (FindLastSelectedIndex(selected) == _state.LoadedMods.Count - 1)
        {
            return;
        }

        List<string> snapshot = _state.LoadedMods.ToList();
        for (int i = snapshot.Count - 1; i >= 0; i--)
        {
            if (selected.Contains(snapshot[i]))
            {
                _state.MoveDown(snapshot[i]);
            }
        }

        RefreshLoaded();
        UpdateDirtyFlag();
    }

    private int FindFirstSelectedIndex(HashSet<string> selected)
    {
        for (int i = 0; i < _state.LoadedMods.Count; i++)
        {
            if (selected.Contains(_state.LoadedMods[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindLastSelectedIndex(HashSet<string> selected)
    {
        for (int i = _state.LoadedMods.Count - 1; i >= 0; i--)
        {
            if (selected.Contains(_state.LoadedMods[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private void Reorder(ReorderRequest? request)
    {
        if (request is null)
        {
            return;
        }

        if (_state.Move(request.SourceName, request.TargetIndex))
        {
            RefreshLoaded();
            UpdateDirtyFlag();
        }
    }

    private void ToggleSelected()
    {
        if (SelectedSearchResult is not { } result)
        {
            return;
        }

        if (result.IsLoaded)
        {
            _state.Unload(result.Name);
            _log.Info($"Unloaded {result.Name}.");
        }
        else if (!_state.Load(result.Name))
        {
            _log.Warning($"Cannot load {result.Name}: not found in workshop.");
            return;
        }
        else
        {
            _log.Info($"Loaded {result.Name}.");
        }

        Rebuild();
        OnPropertyChanged(nameof(ToggleButtonText));
    }

    private void Rebuild()
    {
        RefreshLoaded();
        RefreshAvailable();
        RefreshSearch();

        UpdateDirtyFlag();
        NotifyCommandStates();
    }

    /// <summary>
    /// Reconciles <see cref="LoadedItems"/> against the loaded-mod order,
    /// reusing existing item instances so the UI selection and scroll position
    /// are preserved.
    /// </summary>
    private void RefreshLoaded()
    {
        var missing = new HashSet<string>(_state.MissingMods, StringComparer.Ordinal);
        Reconcile(LoadedItems, _state.LoadedMods, missing);
        OnPropertyChanged(nameof(LoadedCountText));
    }

    private void RefreshAvailable()
    {
        Reconcile(AvailableItems, _state.AvailableMods, new HashSet<string>(StringComparer.Ordinal));
        OnPropertyChanged(nameof(AvailableCountText));
    }

    /// <summary>Rebuilds the unified search results from every known mod (workshop + loaded).</summary>
    private void RefreshSearch()
    {
        var loaded = new HashSet<string>(_state.LoadedMods, StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(_state.MissingMods, StringComparer.OrdinalIgnoreCase);

        var all = new HashSet<string>(_state.WorkshopMods, StringComparer.OrdinalIgnoreCase);
        foreach (string mod in _state.LoadedMods)
        {
            all.Add(mod);
        }

        var names = all
            .Where(mod => string.IsNullOrWhiteSpace(_searchText) || mod.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(mod => mod, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReconcileSearch(names, loaded, missing);
        OnPropertyChanged(nameof(HasSearch));
    }

    private void ReconcileSearch(IReadOnlyList<string> names, HashSet<string> loaded, HashSet<string> missing)
    {
        var existing = new Dictionary<string, SearchResultViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (SearchResultViewModel item in SearchResults)
        {
            existing[item.Name] = item;
        }

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (i < SearchResults.Count && string.Equals(SearchResults[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                SearchResults[i].IsLoaded = loaded.Contains(name);
                SearchResults[i].IsMissing = missing.Contains(name);
                continue;
            }

            if (!existing.TryGetValue(name, out SearchResultViewModel? vm))
            {
                vm = new SearchResultViewModel(name, loaded.Contains(name), missing.Contains(name));
                existing[name] = vm;
            }
            else
            {
                vm.IsLoaded = loaded.Contains(name);
                vm.IsMissing = missing.Contains(name);
            }

            int currentIndex = SearchResults.IndexOf(vm);
            if (currentIndex >= 0)
            {
                SearchResults.Move(currentIndex, i);
            }
            else
            {
                SearchResults.Insert(i, vm);
            }
        }

        while (SearchResults.Count > names.Count)
        {
            SearchResults.RemoveAt(SearchResults.Count - 1);
        }
    }

    /// <summary>
    /// Makes <paramref name="items"/> match <paramref name="names"/> in order,
    /// reusing existing instances (by name) so selections survive.
    /// </summary>
    private static void Reconcile(
        ObservableCollection<ModItemViewModel> items,
        IReadOnlyList<string> names,
        HashSet<string> missing)
    {
        var existing = new Dictionary<string, ModItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (ModItemViewModel item in items)
        {
            existing[item.Name] = item;
        }

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (i < items.Count && string.Equals(items[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                items[i].IsMissing = missing.Contains(name);
                continue;
            }

            if (!existing.TryGetValue(name, out ModItemViewModel? vm))
            {
                vm = new ModItemViewModel(name, missing.Contains(name));
                existing[name] = vm;
            }

            vm.IsMissing = missing.Contains(name);

            int currentIndex = items.IndexOf(vm);
            if (currentIndex >= 0)
            {
                items.Move(currentIndex, i);
            }
            else
            {
                items.Insert(i, vm);
            }
        }

        while (items.Count > names.Count)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private void UpdateDirtyFlag() => IsDirty = !_state.LoadedMods.SequenceEqual(_savedLoaded);

    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyCommandStates();

    private void NotifyCommandStates()
    {
        OnPropertyChanged(nameof(CanSelectAll));
        MoveUpCommand?.RaiseCanExecuteChanged();
        MoveDownCommand?.RaiseCanExecuteChanged();
    }
}
