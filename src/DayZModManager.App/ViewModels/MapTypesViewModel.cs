using System.Collections.ObjectModel;
using System.IO;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.App.Services;

namespace DayZModManager.App.ViewModels;

/// <summary>
/// Backs the "Map & Types" page. Map selection and types configuration are
/// applied immediately, not deferred to the main Apply.
/// </summary>
public sealed class MapTypesViewModel : ViewModelBase
{
    private readonly IMapService _mapService;
    private readonly ITypesService _typesService;
    private readonly ISaveGameService _saveGameService;
    private readonly ITypesConfigStore _typesConfigStore;
    private readonly IServerConfigService _serverConfig;
    private readonly IBatchFileService _batchFile;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly LogViewModel _log;
    private readonly TypesConfig _typesConfig;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;

    private string _serverPath = string.Empty;
    private string _workshopPath = string.Empty;
    private string _batFileName = string.Empty;
    private List<string> _allMods = new();
    private IReadOnlyList<string> _loadedMods = Array.Empty<string>();
    private IReadOnlyList<MapInfo> _discoveredMaps = Array.Empty<MapInfo>();

    private string? _selectedMap;
    private string? _selectedMod;
    private string? _selectedSave;
    private bool _isRestoring;
    private bool _isSwitching;
    private bool _typesBusy;
    private bool _isSaveBusy;
    private string? _pendingMap;

    public MapTypesViewModel(
        IMapService mapService,
        ITypesService typesService,
        ISaveGameService saveGameService,
        ITypesConfigStore typesConfigStore,
        IServerConfigService serverConfig,
        IBatchFileService batchFile,
        IFileSystem fileSystem,
        IDialogService dialogs,
        LogViewModel log,
        TypesConfig typesConfig,
        IDataDirectoryProvider dataDirectoryProvider)
    {
        _mapService = mapService;
        _typesService = typesService;
        _saveGameService = saveGameService;
        _typesConfigStore = typesConfigStore;
        _serverConfig = serverConfig;
        _batchFile = batchFile;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _log = log;
        _typesConfig = typesConfig;
        _dataDirectoryProvider = dataDirectoryProvider;

        ConfigXmlCommand = new RelayCommand(ConfigureMod);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => CanRemoveSelected);
        CleanInvalidCommand = new RelayCommand(CleanInvalid);
        LoadSaveCommand = new RelayCommand(LoadSave, () => SelectedSave is not null && !IsSaveBusy);
        DeleteSaveCommand = new RelayCommand(DeleteSave, () => SelectedSave is not null && !IsSaveBusy);
        AddSaveCommand = new RelayCommand(AddSave, () => !IsSaveBusy);
        NewGameCommand = new RelayCommand(NewGame, () => !IsSaveBusy);

        SelectedTypesRows.CollectionChanged += (_, _) => NotifyCommandStates();
    }

    public ObservableCollection<string> MapNames { get; } = new();

    public ObservableCollection<string> ModNames { get; } = new();

    public ObservableCollection<TypesRowViewModel> TypesRows { get; } = new();

    public ObservableCollection<TypesRowViewModel> SelectedTypesRows { get; } = new();

    /// <summary>
    /// The currently selected map. Selecting a different map applies it
    /// immediately (server template, batch file, map_profiles and economy).
    /// Programmatic restoration during refresh is flagged so it never re-applies.
    /// </summary>
    public string? SelectedMap
    {
        get => _selectedMap;
        set
        {
            if (SetField(ref _selectedMap, value) && !_isRestoring)
            {
                RequestMapSwitch(value);
            }
        }
    }

    public string? SelectedMod
    {
        get => _selectedMod;
        set => SetField(ref _selectedMod, value);
    }

    public string? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (SetField(ref _selectedSave, value))
            {
                LoadSaveCommand.RaiseCanExecuteChanged();
                DeleteSaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True while a save operation is running in the background.</summary>
    public bool IsSaveBusy
    {
        get => _isSaveBusy;
        private set
        {
            if (SetField(ref _isSaveBusy, value))
            {
                LoadSaveCommand.RaiseCanExecuteChanged();
                DeleteSaveCommand.RaiseCanExecuteChanged();
                AddSaveCommand.RaiseCanExecuteChanged();
                NewGameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Names of stored progress saves for the current map.</summary>
    public ObservableCollection<string> SaveNames { get; } = new();

    private bool CanRemoveSelected => SelectedTypesRows.Count > 0;

    /// <summary>
    /// Invoked before configuring types. Returns true when the current in-memory
    /// state is persisted (either already or just applied), false if Apply failed.
    /// </summary>
    public Func<Task<bool>>? EnsureApplied { get; set; }

    public RelayCommand ConfigXmlCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand CleanInvalidCommand { get; }
    public RelayCommand LoadSaveCommand { get; }
    public RelayCommand AddSaveCommand { get; }
    public RelayCommand DeleteSaveCommand { get; }
    public RelayCommand NewGameCommand { get; }

    public void Refresh(Settings settings, IReadOnlyList<string> workshopMods, IReadOnlyList<string> loadedMods)
    {
        _serverPath = settings.ServerPath;
        _workshopPath = settings.WorkshopPath;
        _batFileName = settings.BatFileName;
        Sync(workshopMods, loadedMods);
    }

    /// <summary>Refreshes maps, the mod dropdown and the grid (used on tab activation).</summary>
    public void Sync(IReadOnlyList<string> workshopMods, IReadOnlyList<string> loadedMods)
    {
        _allMods = workshopMods.ToList();
        _loadedMods = loadedMods.ToList();

        RebuildMapNames();
        RefreshModNames(loadedMods);
        RebuildRows();
        RefreshSaves();
    }

    /// <summary>
    /// Called at startup: retains the last-applied map (validating it is still
    /// present on the server) and applies a default map for a first-time user so
    /// the map_profiles folder is created. Does not rewrite server files for an
    /// already-applied map.
    /// </summary>
    public void ReconcileAppliedMap()
    {
        if (MapNames.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_typesConfig.CurrentMap))
        {
            if (!_discoveredMaps.Any(m => string.Equals(m.Name, _typesConfig.CurrentMap, StringComparison.OrdinalIgnoreCase)))
            {
                _log.Warning($"Previously applied map \"{_typesConfig.CurrentMap}\" was not found on this server. Select a valid map.");
            }

            return;
        }

        string defaultMap = MapNames[0];
        SetRestoringSelection(defaultMap);
        ApplyMapCore(defaultMap);
    }

    /// <summary>Rebuilds the map dropdown from discovered maps plus any maps already configured.</summary>
    private void RebuildMapNames()
    {
        string? previousMap = SelectedMap;

        _discoveredMaps = _mapService.DiscoverMaps(_serverPath);

        MapNames.Clear();
        foreach (MapInfo map in _discoveredMaps)
        {
            MapNames.Add(map.Name);
        }

        foreach (string key in _typesConfig.Maps.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!ContainsIgnoreCase(MapNames, key))
            {
                MapNames.Add(key);
            }
        }

        _isRestoring = true;
        try
        {
            if (!string.IsNullOrEmpty(_typesConfig.CurrentMap) && ContainsIgnoreCase(MapNames, _typesConfig.CurrentMap))
            {
                SelectedMap = _typesConfig.CurrentMap;
            }
            else if (previousMap is not null && ContainsIgnoreCase(MapNames, previousMap))
            {
                SelectedMap = previousMap;
            }
            else
            {
                SelectedMap = MapNames.Count > 0 ? MapNames[0] : null;
            }
        }
        finally
        {
            _isRestoring = false;
        }
    }

    /// <summary>Rebuilds the types-config mod dropdown from the currently loaded mods.</summary>
    public void RefreshModNames(IReadOnlyList<string> loadedMods)
    {
        string? previous = SelectedMod;

        ModNames.Clear();
        foreach (string mod in loadedMods)
        {
            ModNames.Add(mod);
        }

        if (previous is not null && ModNames.Contains(previous))
        {
            SelectedMod = previous;
        }
        else
        {
            SelectedMod = ModNames.Count > 0 ? ModNames[0] : null;
        }
    }

    /// <summary>Regenerates cfgeconomycore.xml for the applied map, referencing only loaded mods' types.</summary>
    public bool SyncEconomyCore()
    {
        string? missionPath = ResolveAppliedMapPath();
        if (missionPath is null)
        {
            return false;
        }

        bool updated = _typesService.SyncEconomyCore(_typesConfig, _typesConfig.CurrentMap, missionPath, LoadedSet());
        if (!updated)
        {
            _log.Warning("Failed to update cfgeconomycore.xml.");
        }

        return updated;
    }

    private HashSet<string> LoadedSet() => new(_loadedMods, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the current map's types config, if present.</summary>
    private MapTypesConfig? CurrentMapConfig() =>
        string.IsNullOrEmpty(_typesConfig.CurrentMap)
            ? null
            : _typesConfig.Maps.TryGetValue(_typesConfig.CurrentMap, out MapTypesConfig? map) ? map : null;

    /// <summary>
    /// Returns the leaf names of the generated files currently configured for a mod.
    /// </summary>
    private HashSet<string> GetActiveLeafNames(string modName)
    {
        var leaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ModTypesEntry? entry = CurrentMapConfig()?.Mods.FirstOrDefault(m =>
            string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return leaves;
        }

        foreach (string generated in entry.GeneratedFiles)
        {
            leaves.Add(Path.GetFileName(generated));
        }

        return leaves;
    }

    /// <summary>
    /// Returns the active generated files (by leaf name) that would be deleted if
    /// <paramref name="selectedFiles"/> became the mod's configuration.
    /// </summary>
    private List<string> FilesRemovedBySelection(string modName, HashSet<string> activeLeaves, IReadOnlyList<string> selectedFiles)
    {
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in selectedFiles)
        {
            if (_typesService.GetGeneratedFileName(_workshopPath, modName, file) is { } leaf)
            {
                kept.Add(leaf);
            }
        }

        return activeLeaves.Where(leaf => !kept.Contains(leaf)).ToList();
    }

    private string? ResolveAppliedMapPath()
    {
        if (string.IsNullOrEmpty(_typesConfig.CurrentMap))
        {
            _log.Warning("Apply a map first.");
            return null;
        }

        return ResolveMapPathForName(_typesConfig.CurrentMap);
    }

    private string? ResolveMapPathForName(string? mapName)
    {
        if (mapName is null)
        {
            _log.Warning("Select a map first.");
            return null;
        }

        string? path = _discoveredMaps
            .FirstOrDefault(m => string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase))
            ?.Path;

        if (path is null)
        {
            _log.Error($"Map not found: {mapName}");
        }

        return path;
    }

    /// <summary>
    /// Applies a map selected by the user. If a switch is already in flight the
    /// request is queued so the latest selection always wins.
    /// </summary>
    private void RequestMapSwitch(string? mapName)
    {
        if (mapName is null)
        {
            return;
        }

        if (_isSwitching || _typesBusy)
        {
            // A switch or a types operation is in flight; queue the latest
            // selection so it is applied once the current operation finishes.
            _pendingMap = mapName;
            return;
        }

        _ = SwitchMapAsync(mapName);
    }

    private async Task SwitchMapAsync(string mapName)
    {
        _isSwitching = true;
        _typesBusy = true;
        try
        {
            string? missionPath = ResolveMapPathForName(mapName);
            if (missionPath is null)
            {
                // The selected map cannot be applied; keep the dropdown on the
                // actually-applied map so selection never diverges from it.
                SetRestoringSelection(_typesConfig.CurrentMap);
                return;
            }

            if (!await EnsureAppliedBeforeAsync("map switch"))
            {
                SetRestoringSelection(_typesConfig.CurrentMap);
                return;
            }

            ApplyMapCore(mapName);
            SetRestoringSelection(mapName);
        }
        catch (Exception ex)
        {
            _log.Error($"Map switch failed: {ex.Message}");
        }
        finally
        {
            _isSwitching = false;
            _typesBusy = false;
            string? next = _pendingMap;
            _pendingMap = null;

            if (next is not null && !string.Equals(next, _typesConfig.CurrentMap, StringComparison.Ordinal))
            {
                _ = SwitchMapAsync(next);
            }
        }
    }

    /// <summary>
    /// Claims the single-user mutation slot shared by map switches and the types
    /// operations (configure/remove/clean). Returns false when one is in flight.
    /// </summary>
    private bool TryBeginTypesOperation()
    {
        if (_typesBusy || _isSwitching)
        {
            return false;
        }

        _typesBusy = true;
        return true;
    }

    private void EndTypesOperation() => _typesBusy = false;

    /// <summary>
    /// Applies a map selection that was queued while a types operation held the
    /// mutation slot.
    /// </summary>
    private void DrainPendingMapSwitch()
    {
        if (_typesBusy || _isSwitching)
        {
            return;
        }

        string? next = _pendingMap;
        _pendingMap = null;
        if (next is not null && !string.Equals(next, _typesConfig.CurrentMap, StringComparison.Ordinal))
        {
            _ = SwitchMapAsync(next);
        }
    }

    /// <summary>Sets the selected map without triggering another switch.</summary>
    private void SetRestoringSelection(string? mapName)
    {
        if (string.Equals(_selectedMap, mapName, StringComparison.Ordinal))
        {
            return;
        }

        _isRestoring = true;
        try
        {
            SelectedMap = mapName;
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void ApplyMapCore(string mapName)
    {
        _typesConfig.CurrentMap = mapName;
        _typesConfigStore.Save(_dataDirectoryProvider.Current, _typesConfig);

        string mapId = GetMapId(mapName);

        if (!_serverConfig.UpdateTemplate(_serverPath, mapName))
        {
            _log.Warning("Failed to update the server template (serverDZ.cfg).");
        }

        if (!_batchFile.WriteServerProfile(Path.Combine(_serverPath, _batFileName), $"map_profiles\\{mapId}"))
        {
            _log.Warning("Failed to update the batch file serverProfile.");
        }

        try
        {
            _fileSystem.CreateDirectory(Path.Combine(_serverPath, "map_profiles", mapId));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to create map_profiles directory: {ex.Message}");
        }

        NotifyCommandStates();
        RebuildRows();
        SyncEconomyCore();
        RefreshSaves();
        _log.Success($"Map switched to: {mapName}");
    }

    private async void ConfigureMod()
    {
        if (!TryBeginTypesOperation())
        {
            _log.Warning("Another types or map operation is already in progress; please retry.");
            return;
        }

        try
        {
            string? modName = SelectedMod;
            if (modName is null)
            {
                _log.Warning("Select a mod to configure first.");
                return;
            }

            string? missionPath = ResolveAppliedMapPath();
            if (missionPath is null)
            {
                return;
            }

            IReadOnlyList<string> files = _typesService.DiscoverTypeFiles(_workshopPath, modName);
            _log.Info($"Found {files.Count} candidate type file(s) in {modName}.");

            // Pre-select only the files that are currently configured for this
            // mod so a re-run with no edits does not silently change anything.
            HashSet<string> activeLeaves = GetActiveLeafNames(modName);
            var activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (_typesService.GetGeneratedFileName(_workshopPath, modName, file) is { } leaf && activeLeaves.Contains(leaf))
                {
                    activeFiles.Add(file);
                }
            }

            IReadOnlyList<string>? selected = _dialogs.PickTypeFiles(modName, files, activeFiles);
            if (selected is null || selected.Count == 0)
            {
                return;
            }

            // Reconfiguring a mod that is already configured overwrites its files
            // in db/ModTypes; never do that silently, even when the same file is
            // selected again.
            if (activeLeaves.Count > 0)
            {
                List<string> removed = FilesRemovedBySelection(modName, activeLeaves, selected);
                string message = $"Mod {modName} already has configured type file(s). Reconfiguring will overwrite its current configuration in db/ModTypes with the newly selected file(s).";
                if (removed.Count > 0)
                {
                    string list = string.Join("\n", removed.Select(name => "  \u2022 " + name));
                    message += $"\n\nThe following currently configured file(s) will be deleted because they are no longer selected:\n{list}";
                }

                message += "\n\nContinue?";
                bool replace = _dialogs.Confirm(message, "Replace types configuration?");
                if (!replace)
                {
                    _log.Info("Types configuration cancelled.");
                    return;
                }
            }

            if (!await EnsureAppliedBeforeAsync("types configuration"))
            {
                return;
            }

            TypesOperationResult result = _typesService.ConfigureMod(
                _typesConfig, _typesConfig.CurrentMap, missionPath, _workshopPath, modName, selected, LoadedSet());

            LogOperation(result, "Types configuration completed.");
        }
        catch (Exception ex)
        {
            _log.Error($"Types configuration failed: {ex.Message}");
        }
        finally
        {
            EndTypesOperation();
            DrainPendingMapSwitch();
        }
    }

    private async void RemoveSelected()
    {
        if (!TryBeginTypesOperation())
        {
            _log.Warning("Another types or map operation is already in progress; please retry.");
            return;
        }

        try
        {
            List<TypesRowViewModel> rows = SelectedTypesRows.ToList();
            if (rows.Count == 0)
            {
                _log.Warning("Select rows in the table first.");
                return;
            }

            string? missionPath = ResolveAppliedMapPath();
            if (missionPath is null)
            {
                return;
            }

            if (!_dialogs.Confirm(
                $"Delete the selected {rows.Count} type file(s) from db/ModTypes?\n\nThis permanently removes the file(s) from the mission folder. Continue?",
                "Remove type files"))
            {
                _log.Info("Removal cancelled.");
                return;
            }

            if (!await EnsureAppliedBeforeAsync("removal"))
            {
                return;
            }

            bool success = true;
            var messages = new List<string>();
            foreach (IGrouping<string, TypesRowViewModel> group in rows.GroupBy(r => r.ModName, StringComparer.Ordinal))
            {
                var leaves = new HashSet<string>(group.Select(r => r.FileName), StringComparer.Ordinal);
                TypesOperationResult result = _typesService.RemoveFiles(
                    _typesConfig, _typesConfig.CurrentMap, missionPath, group.Key, leaves, LoadedSet());
                messages.AddRange(result.Messages);
                if (!result.Success)
                {
                    success = false;
                }
            }

            foreach (string message in messages)
            {
                _log.Info(message);
            }

            if (success)
            {
                _typesConfigStore.Save(_dataDirectoryProvider.Current, _typesConfig);
                _log.Success("Removed selected types files.");
                RebuildRows();
            }
            else
            {
                _log.Error("Operation failed.");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Removal failed: {ex.Message}");
        }
        finally
        {
            EndTypesOperation();
            DrainPendingMapSwitch();
        }
    }

    private async void CleanInvalid()
    {
        if (!TryBeginTypesOperation())
        {
            _log.Warning("Another types or map operation is already in progress; please retry.");
            return;
        }

        try
        {
            string? missionPath = ResolveAppliedMapPath();
            if (missionPath is null)
            {
                return;
            }

            var workshop = new HashSet<string>(_allMods, StringComparer.Ordinal);
            var active = new HashSet<string>(_loadedMods.Where(workshop.Contains), StringComparer.Ordinal);

            List<string> invalidMods = CurrentMapConfig() is { } mapConfig
                ? mapConfig.Mods
                    .Where(entry => !active.Contains(entry.ModName))
                    .Select(entry => entry.ModName)
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
                : new List<string>();

            if (invalidMods.Count > 0)
            {
                string list = string.Join("\n", invalidMods.Select(name => "  \u2022 " + name));
                bool clean = _dialogs.Confirm(
                    $"The following mod(s) are no longer active (not loaded or missing from the workshop) and their generated type file(s) will be DELETED from db/ModTypes:\n\n{list}\n\nContinue?",
                    "Clean invalid types configurations?");
                if (!clean)
                {
                    _log.Info("Cleanup cancelled.");
                    return;
                }
            }

            if (!await EnsureAppliedBeforeAsync("cleanup"))
            {
                return;
            }

            TypesOperationResult result = _typesService.CleanInvalid(
                _typesConfig, _typesConfig.CurrentMap, missionPath, active, LoadedSet());

            foreach (string message in result.Messages)
            {
                _log.Info(message);
            }

            if (result.Success)
            {
                _typesConfigStore.Save(_dataDirectoryProvider.Current, _typesConfig);
                RebuildRows();
            }
            else
            {
                _log.Error("Operation failed.");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Cleanup failed: {ex.Message}");
        }
        finally
        {
            EndTypesOperation();
            DrainPendingMapSwitch();
        }
    }

    private async void LoadSave()
    {
        string? mapName = AppliedMapOrWarn();
        string? saveName = SelectedSave;
        if (mapName is null || saveName is null)
        {
            if (saveName is null)
            {
                _log.Warning("Select a stored save first.");
            }

            return;
        }

        bool confirmed = _dialogs.Confirm(
            $"Load save \"{saveName}\" for map {mapName}?\n\nThis will REPLACE the current progress in {StorageLabel(mapName)} with the stored copy. The current progress will be lost. Continue?",
            "Load Save");
        if (!confirmed)
        {
            _log.Info("Load cancelled.");
            return;
        }

        if (IsSaveBusy)
        {
            return;
        }

        string serverPath = _serverPath;
        string dataDirectory = _dataDirectoryProvider.Current;
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(() => _saveGameService.LoadSave(serverPath, mapName, dataDirectory, saveName));
            LogSaveResult(result);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load save: {ex.Message}");
        }
        finally
        {
            IsSaveBusy = false;
        }
    }

    private async void AddSave()
    {
        string? mapName = AppliedMapOrWarn();
        if (mapName is null)
        {
            return;
        }

        string? name = _dialogs.AskText(
            "Add Save",
            $"Save the current progress of {mapName} ({StorageLabel(mapName)}) as:", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string trimmed = name.Trim();
        bool exists = SaveNames.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
        if (exists && !_dialogs.Confirm($"A save named \"{trimmed}\" already exists. Overwrite it?", "Overwrite save?"))
        {
            _log.Info("Save cancelled.");
            return;
        }

        if (IsSaveBusy)
        {
            return;
        }

        string serverPath = _serverPath;
        string dataDirectory = _dataDirectoryProvider.Current;
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(() => _saveGameService.AddSave(serverPath, mapName, dataDirectory, trimmed, overwrite: exists));
            LogSaveResult(result);
            if (result.Success)
            {
                RefreshSaves();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to save progress: {ex.Message}");
        }
        finally
        {
            IsSaveBusy = false;
        }
    }

    private async void DeleteSave()
    {
        string? mapName = AppliedMapOrWarn();
        string? saveName = SelectedSave;
        if (mapName is null || saveName is null)
        {
            if (saveName is null)
            {
                _log.Warning("Select a stored save first.");
            }

            return;
        }

        bool confirmed = _dialogs.Confirm(
            $"Delete the stored save \"{saveName}\"? This cannot be undone.", "Delete Save");
        if (!confirmed)
        {
            _log.Info("Deletion cancelled.");
            return;
        }

        if (IsSaveBusy)
        {
            return;
        }

        string dataDirectory = _dataDirectoryProvider.Current;
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(() => _saveGameService.DeleteSave(dataDirectory, mapName, saveName));
            LogSaveResult(result);
            if (result.Success)
            {
                RefreshSaves();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to delete save: {ex.Message}");
        }
        finally
        {
            IsSaveBusy = false;
        }
    }

    private async void NewGame()
    {
        string? mapName = AppliedMapOrWarn();
        if (mapName is null)
        {
            return;
        }

        bool confirmed = _dialogs.Confirm(
            $"Start a NEW GAME on map {mapName}?\n\nThis will DELETE {StorageLabel(mapName)} so the map starts fresh on the next server launch. Continue?",
            "New Game");
        if (!confirmed)
        {
            _log.Info("New game cancelled.");
            return;
        }

        if (IsSaveBusy)
        {
            return;
        }

        string serverPath = _serverPath;
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(() => _saveGameService.NewGame(serverPath, mapName));
            LogSaveResult(result);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to start a new game: {ex.Message}");
        }
        finally
        {
            IsSaveBusy = false;
        }
    }

    /// <summary>Returns the applied map name, warning when none is applied yet.</summary>
    private string? AppliedMapOrWarn()
    {
        if (string.IsNullOrWhiteSpace(_typesConfig.CurrentMap))
        {
            _log.Warning("Apply a map first before managing progress saves.");
            return null;
        }

        return _typesConfig.CurrentMap;
    }

    private string StorageLabel(string mapName)
    {
        try
        {
            return Path.GetFileName(_saveGameService.GetStorageFolderPath(_serverPath, mapName));
        }
        catch (Exception)
        {
            return "storage folder";
        }
    }

    private void LogSaveResult(SaveGameResult result)
    {
        if (result.Success)
        {
            _log.Success(result.Message);
        }
        else
        {
            _log.Error(result.Message);
        }
    }

    /// <summary>Rebuilds the stored-save list for the current map.</summary>
    private void RefreshSaves()
    {
        SaveNames.Clear();
        SelectedSave = null;

        string mapName = _typesConfig.CurrentMap;
        if (string.IsNullOrWhiteSpace(_serverPath) || string.IsNullOrWhiteSpace(mapName))
        {
            return;
        }

        try
        {
            foreach (string save in _saveGameService.ListSaves(_dataDirectoryProvider.Current, mapName))
            {
                SaveNames.Add(save);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to list progress saves: {ex.Message}");
        }
    }

    private void LogOperation(TypesOperationResult result, string successMessage)
    {
        foreach (string message in result.Messages)
        {
            _log.Info(message);
        }

        if (result.Success)
        {
            _typesConfigStore.Save(_dataDirectoryProvider.Current, _typesConfig);
            _log.Success(successMessage);
            RebuildRows();
        }
        else
        {
            _log.Error("Operation failed.");
        }
    }

    private void RebuildRows()
    {
        TypesRows.Clear();
        if (string.IsNullOrEmpty(_typesConfig.CurrentMap) ||
            !_typesConfig.Maps.TryGetValue(_typesConfig.CurrentMap, out MapTypesConfig? map))
        {
            return;
        }

        var valid = new HashSet<string>(_allMods, StringComparer.Ordinal);
        var loaded = LoadedSet();
        foreach (ModTypesEntry entry in map.Mods)
        {
            bool inactive = !valid.Contains(entry.ModName) || !loaded.Contains(entry.ModName);
            foreach (string generated in entry.GeneratedFiles)
            {
                TypesRows.Add(new TypesRowViewModel(entry.ModName, Path.GetFileName(generated), inactive));
            }
        }
    }

    private void NotifyCommandStates()
    {
        RemoveSelectedCommand.RaiseCanExecuteChanged();
    }

    private async Task<bool> EnsureAppliedBeforeAsync(string action)
    {
        if (EnsureApplied is not null && !await EnsureApplied())
        {
            _log.Error($"Apply failed; {action} aborted.");
            return false;
        }

        return true;
    }

    private static bool ContainsIgnoreCase(ObservableCollection<string> items, string value) =>
        items.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

    private static string GetMapId(string mapName)
    {
        int dot = mapName.LastIndexOf('.');
        return dot >= 0 ? mapName[(dot + 1)..] : mapName;
    }
}
