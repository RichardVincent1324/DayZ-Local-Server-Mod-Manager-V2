using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DayZModManager.App.Services;
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
    private readonly IDialogService _dialogs;

    private List<string> _savedLoaded = new();
    private string _workshopPath = string.Empty;
    private string _searchText = string.Empty;
    private SearchResultViewModel? _selectedSearchResult;
    private bool _isDirty;

    public ModsViewModel(
        ModState state,
        IModDiscoveryService discovery,
        LogViewModel log,
        IDialogService dialogs)
    {
        _state = state;
        _discovery = discovery;
        _log = log;
        _dialogs = dialogs;

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
    public AsyncRelayCommand RefreshCommand { get; private set; } = null!;
    public RelayCommand RemoveMissingCommand { get; private set; } = null!;
    public RelayCommand<ReorderRequest> ReorderCommand { get; private set; } = null!;
    public RelayCommand ToggleSelectedCommand { get; private set; } = null!;

    /// <summary>Wires up commands (called once after construction).</summary>
    public void Initialize()
    {
        LoadSelectedCommand = new RelayCommand(LoadSelected);
        UnloadSelectedCommand = new RelayCommand(UnloadSelected);

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(_workshopPath));
        RemoveMissingCommand = new RelayCommand(RemoveMissing, () => _state.MissingMods.Count > 0);
        ReorderCommand = new RelayCommand<ReorderRequest>(Reorder);
        ToggleSelectedCommand = new RelayCommand(ToggleSelected, () => SelectedSearchResult is not null);
    }

    /// <summary>
    /// Re-discovers workshop mods on a background thread to keep the UI responsive.
    /// An unusable workshop path leaves the current lists untouched: treating it as
    /// an empty folder would mark every loaded mod as missing.
    /// </summary>
    /// <param name="workshopPath">The workshop folder to scan.</param>
    public async Task RefreshAsync(string workshopPath)
    {
        _workshopPath = workshopPath;

        ModDiscoveryResult result;
        try
        {
            result = await Task.Run(() => _discovery.DiscoverWorkshopMods(workshopPath));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to discover workshop mods: {ex.Message}");
            return;
        }

        if (!result.PathAvailable)
        {
            // Keep the previous lists. A blank path is the normal state before the
            // app is configured, so it is a warning; a configured-but-unreadable
            // folder is an error worth surfacing.
            if (string.IsNullOrWhiteSpace(workshopPath))
            {
                _log.Warning("The workshop path is not set; set it in the Settings tab to manage mods.");
            }
            else
            {
                _log.Error($"The workshop folder could not be read, so the mod list was not refreshed: {workshopPath}");
            }

            return;
        }

        ApplyDiscovery(result.Mods);
    }

    /// <summary>Marks the current state as applied (clears the dirty flag).</summary>
    public void MarkApplied() => MarkApplied(_state.LoadedMods);

    /// <summary>
    /// Records <paramref name="appliedMods"/> (the snapshot that was actually
    /// persisted) as the applied baseline and recomputes dirty from the current
    /// in-memory list. Edits made after the snapshot — e.g. while an Apply was in
    /// flight on a background thread — correctly remain pending.
    /// </summary>
    public void MarkApplied(IReadOnlyList<string> appliedMods)
    {
        _savedLoaded = appliedMods.ToList();
        UpdateDirtyFlag();
    }

    /// <summary>
    /// Replaces the loaded-mod list wholesale (e.g. when restoring the mod list a
    /// progress save was created with) and refreshes the UI. The new list becomes
    /// pending until the next Apply.
    /// </summary>
    public void ReplaceLoadedMods(IReadOnlyList<string> mods)
    {
        _state.ReplaceLoadedMods(mods);
        Rebuild();
        UpdateDirtyFlag();
    }

    /// <summary>
    /// Applies a completed discovery: updates the workshop snapshot, reports missing
    /// mods, then rebuilds the lists exactly once.
    /// </summary>
    private void ApplyDiscovery(IReadOnlyList<string> workshopMods)
    {
        _state.SetWorkshopMods(workshopMods);
        _log.Info($"Found {workshopMods.Count} mod(s) in workshop.");

        if (workshopMods.Count == 0 && !string.IsNullOrWhiteSpace(_workshopPath))
        {
            _log.Warning(
                "No mods were found in the workshop path. Make sure it points at the DayZ \"!Workshop\" folder " +
                "(e.g. ...\\steamapps\\common\\DayZ\\!Workshop).");
        }

        IReadOnlyList<string> missing = _state.MissingMods;
        if (missing.Count > 0)
        {
            _log.Warning($"{missing.Count} loaded mod(s) are missing from the workshop: {string.Join(", ", missing)}");
        }

        Rebuild();
    }

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

        Rebuild();
    }

    /// <summary>
    /// Drops every loaded mod that is no longer present in the workshop, in one pass.
    /// The removal is in-memory until the next Apply, so the explicit button plus one
    /// confirmation is what makes acting on the detected state safe.
    /// </summary>
    private void RemoveMissing()
    {
        IReadOnlyList<string> missing = _state.MissingMods;
        if (missing.Count == 0)
        {
            _log.Warning("No missing mods to remove.");
            return;
        }

        string message =
            $"Remove {missing.Count} missing mod(s) from the load order?\n\n{string.Join(", ", missing)}\n\n" +
            "This takes effect the next time you click Apply.";

        if (!_dialogs.Confirm(message, "Remove missing mods"))
        {
            _log.Info("Removal cancelled.");
            return;
        }

        int removed = _state.UnloadMany(missing);
        Rebuild();
        _log.Success($"Removed {removed} missing mod(s) from the load order.");
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

            // The search dropdown shows load positions, so a reorder must refresh
            // it too or its "#Index" column would go stale.
            RefreshSearch();
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
        }
        else if (!_state.Load(result.Name))
        {
            _log.Warning($"Cannot load {result.Name}: not found in workshop.");
            return;
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

        for (int i = 0; i < LoadedItems.Count; i++)
        {
            LoadedItems[i].Position = i + 1;
        }

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

        // The search dropdown shows each result's load position so it can be matched
        // visually against the Loaded Mods list. Positions come from the same source
        // as the list, so both always agree.
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _state.LoadedMods.Count; i++)
        {
            positions[_state.LoadedMods[i]] = i + 1;
        }

        var all = new HashSet<string>(_state.WorkshopMods, StringComparer.OrdinalIgnoreCase);
        foreach (string mod in _state.LoadedMods)
        {
            all.Add(mod);
        }

        var names = all
            .Where(mod => string.IsNullOrWhiteSpace(_searchText) || mod.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(mod => mod, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReconcileSearch(names, loaded, missing, positions);
        OnPropertyChanged(nameof(HasSearch));
    }

    private void ReconcileSearch(
        IReadOnlyList<string> names,
        HashSet<string> loaded,
        HashSet<string> missing,
        IReadOnlyDictionary<string, int> positions)
    {
        var existing = new Dictionary<string, SearchResultViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (SearchResultViewModel item in SearchResults)
        {
            existing[item.Name] = item;
        }

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            int? position = positions.TryGetValue(name, out int loadPosition) ? loadPosition : null;

            if (i < SearchResults.Count && string.Equals(SearchResults[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                SearchResults[i].IsLoaded = loaded.Contains(name);
                SearchResults[i].IsMissing = missing.Contains(name);
                SearchResults[i].Position = position;
                continue;
            }

            if (!existing.TryGetValue(name, out SearchResultViewModel? vm))
            {
                vm = new SearchResultViewModel(name, loaded.Contains(name), missing.Contains(name), position);
                existing[name] = vm;
            }
            else
            {
                vm.IsLoaded = loaded.Contains(name);
                vm.IsMissing = missing.Contains(name);
                vm.Position = position;
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
        RemoveMissingCommand?.RaiseCanExecuteChanged();
    }
}
