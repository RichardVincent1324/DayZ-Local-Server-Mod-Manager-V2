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
    /// <summary>Mod-name label shown for files in db\ModTypes that are not tracked by the config.</summary>
    private const string UntrackedLabel = "(untracked)";

    private readonly IMapService _mapService;
    private readonly ITypesService _typesService;
    private readonly ISaveGameService _saveGameService;
    private readonly ITypesBackupService _typesBackup;
    private readonly ITypesConfigStore _typesConfigStore;
    private readonly IServerConfigService _serverConfig;
    private readonly IBatchFileService _batchFile;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly LogViewModel _log;
    private readonly TypesConfig _typesConfig;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly IDayZServerProcessState _serverProcess;
    private readonly IProcessLauncher _processLauncher;

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
        ITypesBackupService typesBackup,
        ITypesConfigStore typesConfigStore,
        IServerConfigService serverConfig,
        IBatchFileService batchFile,
        IFileSystem fileSystem,
        IDialogService dialogs,
        LogViewModel log,
        TypesConfig typesConfig,
        IDataDirectoryProvider dataDirectoryProvider,
        IDayZServerProcessState serverProcess,
        IProcessLauncher processLauncher)
    {
        _mapService = mapService;
        _typesService = typesService;
        _saveGameService = saveGameService;
        _typesBackup = typesBackup;
        _typesConfigStore = typesConfigStore;
        _serverConfig = serverConfig;
        _batchFile = batchFile;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _log = log;
        _typesConfig = typesConfig;
        _dataDirectoryProvider = dataDirectoryProvider;
        _serverProcess = serverProcess;
        _processLauncher = processLauncher;

        ConfigXmlCommand = new RelayCommand(ConfigureMod, () => TypesEditingAllowed);
        OpenModTypesFolderCommand = new RelayCommand(OpenModTypesFolder, () => TypesEditingAllowed);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => TypesEditingAllowed && CanRemoveSelected);
        CleanInvalidCommand = new RelayCommand(CleanInvalid, () => TypesEditingAllowed);
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
                NotifyBusyChanged();
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
    /// True when types may be configured: a map is applied and no world exists.
    /// DayZ only reads type files when a new world is created, so once a world
    /// (storage folder) is present editing is locked until a New Game.
    /// </summary>
    public bool TypesEditingAllowed =>
        !string.IsNullOrWhiteSpace(_typesConfig.CurrentMap) && !WorldExists(_typesConfig.CurrentMap);

    /// <summary>True when a world exists and types editing is therefore locked.</summary>
    public bool IsTypesLocked =>
        !string.IsNullOrWhiteSpace(_typesConfig.CurrentMap) && WorldExists(_typesConfig.CurrentMap);

    /// <summary>Banner shown while types editing is locked; empty when editing is allowed.</summary>
    public string TypesLockedMessage =>
        IsTypesLocked
            ? "Types editing is disabled while a world exists. Start a New Game to reconfigure."
            : string.Empty;

    /// <summary>
    /// Tooltip for the ModTypes folder button: an action hint when it is available,
    /// or the lock reason (same as the Config XML button) when editing is disabled.
    /// </summary>
    public string OpenModTypesFolderToolTip =>
        TypesEditingAllowed ? "Open ModTypes folder in File Explorer" : TypesLockedMessage;

    /// <summary>True when the mission's storage folder exists (a world has been created).</summary>
    private bool WorldExists(string mapName)
    {
        if (string.IsNullOrWhiteSpace(_serverPath) || string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        try
        {
            return _fileSystem.DirectoryExists(_saveGameService.GetStorageFolderPath(_serverPath, mapName));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void NotifyTypesEditingChanged()
    {
        OnPropertyChanged(nameof(TypesEditingAllowed));
        OnPropertyChanged(nameof(IsTypesLocked));
        OnPropertyChanged(nameof(TypesLockedMessage));
        OnPropertyChanged(nameof(OpenModTypesFolderToolTip));
    }

    /// <summary>
    /// True while a map switch, a types operation (configure/remove/clean), or a
    /// progress-save operation (load/add/delete/new game) is in flight. Used by the
    /// Start Server action so it never launches the server while the launch batch,
    /// mission files, or the live storage folder are being rewritten or deleted.
    /// </summary>
    public bool IsBusy => _isSwitching || _typesBusy || _isSaveBusy;

    private void NotifyBusyChanged() => OnPropertyChanged(nameof(IsBusy));

    /// <summary>
    /// Invoked before configuring types. Returns true when the current in-memory
    /// state is persisted (either already or just applied), false if Apply failed.
    /// </summary>
    public Func<Task<bool>>? EnsureApplied { get; set; }

    /// <summary>
    /// Invoked when a progress save is loaded to replace the loaded mod list with
    /// the save's list and apply it (batch modList, mod_order.json, junctions).
    /// Returns true on success.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<bool>>? RestoreModList { get; set; }

    /// <summary>The save currently loaded for the active map, if any (session only).</summary>
    private (string MapName, string SaveName)? _activeSave;

    /// <summary>
    /// After a successful Apply, appends any newly loaded mods to the active
    /// save's meta.json (append-only union) so the save keeps track of them.
    /// </summary>
    public void OnApplied(IReadOnlyList<string> appliedMods)
    {
        if (_activeSave is not { } active
            || !string.Equals(active.MapName, _typesConfig.CurrentMap, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(_serverPath))
        {
            return;
        }

        SaveGameResult result = _saveGameService.AppendMetaModList(
            _dataDirectoryProvider.Current, active.MapName, active.SaveName, appliedMods);
        if (!result.Success)
        {
            _log.Warning(result.Message);
        }
    }

    public RelayCommand ConfigXmlCommand { get; }
    public RelayCommand OpenModTypesFolderCommand { get; }
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
        NotifyCommandStates();
    }

    /// <summary>
    /// Called at startup: retains the last-applied map (validating it is still
    /// present on the server) and, for a first-time user whose server is already
    /// configured, applies the first discovered map so the server is provisioned
    /// for it (mission template and batch serverProfile) before its first launch.
    /// Does not rewrite server files for an already-applied map, and never
    /// auto-applies while there is no real mission folder on a configured server.
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

        // Nothing has been applied yet. Auto-apply a default map only when a real
        // mission folder exists on a configured server; before the user sets the
        // server path or selects a launch batch file there is nothing to switch to
        // and no server file to write.
        if (string.IsNullOrWhiteSpace(_serverPath)
            || string.IsNullOrWhiteSpace(_batFileName)
            || _discoveredMaps.Count == 0)
        {
            return;
        }

        string defaultMap = _discoveredMaps[0].Name;
        if (!ApplyMapCore(defaultMap))
        {
            // Refused (server running) or the server files could not be written;
            // leave the current map empty so a later reconcile retries.
            return;
        }

        SetRestoringSelection(defaultMap);
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
        NotifyBusyChanged();
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

            if (!ApplyMapCore(mapName))
            {
                // Refused (server running) or the server files could not be
                // updated; keep the dropdown on the actually-applied map.
                SetRestoringSelection(_typesConfig.CurrentMap);
                return;
            }

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
            NotifyBusyChanged();
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
        NotifyBusyChanged();
        return true;
    }

    private void EndTypesOperation()
    {
        _typesBusy = false;
        NotifyBusyChanged();
    }

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

    /// <summary>
    /// Applies <paramref name="mapName"/> to the server and commits it as the
    /// current map. The server template and batch serverProfile are written and
    /// validated FIRST so a failure never leaves the app believing a map was
    /// applied while the server still runs the previous mission; CurrentMap is
    /// only persisted once those succeed. Returns false when the switch was
    /// refused (server running) or the server files could not be updated.
    /// </summary>
    private bool ApplyMapCore(string mapName)
    {
        if (RefuseWhileServerRunning("switching maps"))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_batFileName))
        {
            _log.Error("No launch batch file is selected. Choose one in the Settings tab before switching maps.");
            return false;
        }

        string previousMap = _typesConfig.CurrentMap;

        if (!_serverConfig.UpdateTemplate(_serverPath, mapName))
        {
            _log.Error("Failed to update the server template (serverDZ.cfg); the map was not switched.");
            return false;
        }

        // The map profile folder mirrors the mpmissions mission folder exactly
        // (e.g. map_profiles\dayzOffline.chernarusplus) so profiles and logs line
        // up 1:1 with the mission the tool runs.
        if (!_batchFile.WriteServerProfile(Path.Combine(_serverPath, _batFileName), $"map_profiles\\{mapName}"))
        {
            // Roll the template back so the server files stay on the previous map.
            RollbackMapFiles(previousMap, mapName);
            _log.Error("Failed to update the batch file serverProfile; the map was not switched.");
            return false;
        }

        // Commit: only after the server files were updated successfully.
        _typesConfig.CurrentMap = mapName;
        try
        {
            _typesConfigStore.Save(_dataDirectoryProvider.Current, _typesConfig);
        }
        catch (Exception ex)
        {
            _typesConfig.CurrentMap = previousMap;
            RollbackMapFiles(previousMap, mapName);
            _log.Error($"Failed to persist the applied map; the map was not switched: {ex.Message}");
            return false;
        }

        // Pre-create the profile folder the batch serverProfile points at. DayZ
        // also creates it on its first boot, so this is only a convenience; a
        // failure is non-fatal.
        try
        {
            _fileSystem.CreateDirectory(Path.Combine(_serverPath, "map_profiles", mapName));
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
        return true;
    }

    /// <summary>
    /// Best-effort revert of the server template and batch serverProfile to
    /// <paramref name="previousMap"/> after a mid-switch failure. A failure to
    /// roll back is only logged; the caller has already reported the switch error.
    /// </summary>
    private void RollbackMapFiles(string previousMap, string currentMap)
    {
        if (string.IsNullOrWhiteSpace(previousMap)
            || string.Equals(previousMap, currentMap, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_serverConfig.UpdateTemplate(_serverPath, previousMap))
        {
            _log.Warning("Failed to restore the previous server template after the map switch was aborted.");
        }

        if (!_batchFile.WriteServerProfile(Path.Combine(_serverPath, _batFileName), $"map_profiles\\{previousMap}"))
        {
            _log.Warning("Failed to restore the previous batch serverProfile after the map switch was aborted.");
        }
    }

    private async void ConfigureMod()
    {
        if (GuardTypesEditing("configuring types files"))
        {
            return;
        }

        if (RefuseWhileServerRunning("configuring types files"))
        {
            return;
        }

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
        if (GuardTypesEditing("removing types files"))
        {
            return;
        }

        if (RefuseWhileServerRunning("removing types files"))
        {
            return;
        }

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

            List<TypesRowViewModel> trackedRows = rows.Where(r => !r.IsUntracked).ToList();
            var untrackedLeaves = new HashSet<string>(
                rows.Where(r => r.IsUntracked).Select(r => r.FileName), StringComparer.Ordinal);

            string confirm = $"Delete the selected {rows.Count} type file(s) from db/ModTypes?";
            if (untrackedLeaves.Count > 0)
            {
                confirm += $"\n\n{untrackedLeaves.Count} of them are untracked (present in db/ModTypes but not managed by this app). Removing them also deletes their entries from cfgeconomycore.xml.";
            }

            confirm += "\n\nThis permanently removes the file(s) from the mission folder. Continue?";
            if (!_dialogs.Confirm(confirm, "Remove type files"))
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
            foreach (IGrouping<string, TypesRowViewModel> group in trackedRows.GroupBy(r => r.ModName, StringComparer.Ordinal))
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

            if (untrackedLeaves.Count > 0)
            {
                TypesOperationResult result = _typesService.RemoveUntrackedFiles(
                    _typesConfig, _typesConfig.CurrentMap, missionPath, untrackedLeaves);
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
                if (trackedRows.Count > 0)
                {
                    _typesConfigStore.Save(_dataDirectoryProvider.Current, _typesConfig);
                }

                CaptureConfiguredTypes();
                _log.Success("Removed selected types files.");
                RebuildRows();
                NotifyCommandStates();
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
        if (GuardTypesEditing("cleaning up invalid types configurations"))
        {
            return;
        }

        if (RefuseWhileServerRunning("cleaning up invalid types configurations"))
        {
            return;
        }

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

        if (RefuseWhileServerRunning("loading a save"))
        {
            return;
        }

        string dataDirectory = _dataDirectoryProvider.Current;
        LoadCompatibilityReport report = BuildLoadCompatibilityReport(mapName, dataDirectory, saveName);

        // A save whose mods are not all present would fail to load its junctions;
        // refuse it rather than silently restoring a different mod set.
        if (report.HasMissingSavedMods)
        {
            string missing =
                "This save was created with mod(s) that are missing from the Workshop and cannot be loaded:\n\n  "
                + string.Join("\n  ", report.MissingSavedMods)
                + "\n\nInstall/subscribe the missing mod(s) in Steam, then load the save again.";
            _log.Error($"Load cancelled: mod(s) missing from the Workshop: {string.Join(", ", report.MissingSavedMods)}");
            _dialogs.ShowWarning(
                $"Load save \"{saveName}\" for map {mapName}?",
                "Load Save",
                missing);
            return;
        }

		string message =
			$"Load save \"{saveName}\" (map: {mapName})?\n\n" +
			$"This will overwrite your current progress in {StorageLabel(mapName)} and replace the mission's type file configuration with the stored copy, " +
			$"and set the loaded mod list to this save's. " +
			$"Your configured type settings are preserved for the next New Game. " +
			$"The current progress will be lost.";
	
        bool confirmed = string.IsNullOrWhiteSpace(report.SnapshotWarning)
            ? _dialogs.Confirm(message, "Load Save")
            : _dialogs.ConfirmWithWarning(message, "Load Save", report.SnapshotWarning);

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
        IsSaveBusy = true;
        try
        {
            // Preserve the configured types before the loaded world replaces them,
            // so a later New Game can restore the user's own configuration.
            _typesConfig.Maps.TryGetValue(mapName, out MapTypesConfig? configured);
            TypesBackupResult backup = await Task.Run(
                () => _typesBackup.EnsureCaptured(serverPath, mapName, dataDirectory, configured));
            if (!backup.Success)
            {
                foreach (string backupMessage in backup.Messages)
                {
                    _log.Error(backupMessage);
                }

                _log.Error("Load cancelled: the configured types could not be preserved.");
                return;
            }

            SaveGameResult result = await Task.Run(
                () => _saveGameService.LoadSave(serverPath, mapName, dataDirectory, saveName));
            LogSaveResult(result);

            if (result.Success)
            {
                ApplyRestoredTypesConfig(mapName, dataDirectory, saveName);

                // Replace the loaded mods with the save's and apply them so the
                // batch file, mod_order.json and junctions match the saved world.
                if (report.HasSnapshot && RestoreModList is not null)
                {
                    _activeSave = (mapName, saveName);
                    bool modsRestored = await RestoreModList(report.SavedModList);
                    if (!modsRestored)
                    {
                        _log.Error("The save loaded, but its mod list could not be applied. Use Apply to retry.");
                    }
                }

                NotifyCommandStates();
                RebuildRows();
            }
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

    /// <summary>
    /// Mirrors the loaded save's types mapping into types_config.json and syncs
    /// cfgeconomycore.xml, so the configuration matches the restored db\ModTypes.
    /// </summary>
    private void ApplyRestoredTypesConfig(string mapName, string dataDirectory, string saveName)
    {
        SaveMetaData? meta;
        try
        {
            ConfigLoadResult<SaveMetaData> loaded = _saveGameService.GetMeta(dataDirectory, mapName, saveName);
            meta = loaded.Status == ConfigLoadStatus.Success ? loaded.Value : null;
        }
        catch (Exception)
        {
            meta = null;
        }

        if (meta?.TypesConfig is null)
        {
            _log.Warning("The save has no types mapping; types_config.json was left unchanged.");
            return;
        }

        // Capture what the manager owned before the save's mapping replaces it:
        // LoadSave has already deleted those files from db\ModTypes, so their
        // cfgeconomycore entries must be removed even though the new mapping no
        // longer records them.
        IReadOnlySet<string> previouslyOwned = _typesConfig.Maps.TryGetValue(mapName, out MapTypesConfig? replaced)
            ? replaced.Mods
                .SelectMany(entry => entry.GeneratedFiles)
                .Select(generated => Path.GetFileName(generated)!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _typesConfig.Maps[mapName] = meta.TypesConfig;
        try
        {
            _typesConfigStore.Save(dataDirectory, _typesConfig);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to persist the restored types configuration: {ex.Message}");
            return;
        }

        string missionPath = Path.Combine(_serverPath, "mpmissions", mapName);
        if (!_typesService.SyncEconomyCore(_typesConfig, mapName, missionPath, LoadedSet(), previouslyOwned))
        {
            _log.Warning("Restored the types mapping, but cfgeconomycore.xml could not be updated.");
        }
        else
        {
            _log.Info("Restored the types files and types configuration for the loaded world.");
        }
    }

    private async void AddSave()
    {
        string? mapName = AppliedMapOrWarn();
        if (mapName is null)
        {
            return;
        }

        if (RefuseWhileServerRunning("saving progress"))
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
            SaveMetaData meta = BuildSaveSnapshot(mapName);
            SaveGameResult result = await Task.Run(
                () => _saveGameService.AddSave(serverPath, mapName, dataDirectory, trimmed, overwrite: exists, meta));
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
                if (_activeSave is { } active
                    && string.Equals(active.MapName, mapName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(active.SaveName, saveName, StringComparison.OrdinalIgnoreCase))
                {
                    _activeSave = null;
                }

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

        if (RefuseWhileServerRunning("starting a new game"))
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
        string dataDirectory = _dataDirectoryProvider.Current;
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(() => _saveGameService.NewGame(serverPath, mapName));
            LogSaveResult(result);
            if (result.Success)
            {
                if (_activeSave is { } active
                    && string.Equals(active.MapName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    _activeSave = null;
                }

                await RestoreConfiguredTypesAsync(mapName, dataDirectory);
                NotifyCommandStates();
                RebuildRows();
            }
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

    /// <summary>
    /// Restores the configured types (files and mapping) saved in the backup
    /// folder, so a new world starts from the configuration the user set up
    /// rather than the previously loaded save's types.
    /// </summary>
    private async Task RestoreConfiguredTypesAsync(string mapName, string dataDirectory)
    {
        string serverPath = _serverPath;
        TypesBackupResult restore = await Task.Run(
            () => _typesBackup.Restore(serverPath, mapName, dataDirectory));

        foreach (string message in restore.Messages)
        {
            if (restore.Success)
            {
                _log.Info(message);
            }
            else
            {
                _log.Warning(message);
            }
        }

        if (!restore.Success || restore.Config is null)
        {
            return;
        }

        _typesConfig.Maps[mapName] = restore.Config;
        try
        {
            _typesConfigStore.Save(dataDirectory, _typesConfig);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to persist the restored types configuration: {ex.Message}");
            return;
        }

        string missionPath = Path.Combine(_serverPath, "mpmissions", mapName);
        if (!_typesService.SyncEconomyCore(_typesConfig, mapName, missionPath, LoadedSet()))
        {
            _log.Warning("Restored the configured types mapping, but cfgeconomycore.xml could not be updated.");
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

    /// <summary>
    /// Captures the current configuration a progress save depends on: the loaded
    /// mod list (order matters) and the map's active generated type files.
    /// </summary>
    private SaveMetaData BuildSaveSnapshot(string mapName)
    {
        string storageFolder;
        try
        {
            storageFolder = Path.GetFileName(_saveGameService.GetStorageFolderPath(_serverPath, mapName));
        }
        catch (Exception)
        {
            storageFolder = string.Empty;
        }

        return new SaveMetaData
        {
            Map = mapName,
            StorageFolder = storageFolder,
            SavedAtUtc = DateTime.UtcNow,
            ModList = _loadedMods.ToList(),
            TypesFiles = _typesService.GetActiveTypeFileNames(_typesConfig, mapName, LoadedSet()).ToList(),
            TypesConfig = CloneMapTypes(_typesConfig.Maps.GetValueOrDefault(mapName)),
        };
    }

    /// <summary>
    /// Deep-copies a map's types configuration so the persisted save snapshot
    /// cannot alias (and later drift with) the live in-memory model. A missing
    /// map config becomes an empty (non-null) mapping so every new save is restorable.
    /// </summary>
    private static MapTypesConfig CloneMapTypes(MapTypesConfig? source) =>
        new()
        {
            Mods = source is null
                ? new List<ModTypesEntry>()
                : source.Mods.Select(entry => new ModTypesEntry
                {
                    ModName = entry.ModName,
                    SourceFiles = entry.SourceFiles.ToList(),
                    GeneratedFiles = entry.GeneratedFiles.ToList(),
                }).ToList(),
        };

    /// <summary>
    /// Reads a stored save's configuration snapshot to decide whether its mod
    /// list and type files can be restored, and whether any saved mod is missing
    /// from the Workshop. The mismatch details are not surfaced: loading applies
    /// the save's configuration automatically.
    /// </summary>
    private LoadCompatibilityReport BuildLoadCompatibilityReport(string mapName, string dataDirectory, string saveName)
    {
        ConfigLoadResult<SaveMetaData> stored;
        try
        {
            stored = _saveGameService.GetMeta(dataDirectory, mapName, saveName);
        }
        catch (Exception)
        {
            return new LoadCompatibilityReport(
                false, Array.Empty<string>(), Array.Empty<string>(),
                "This save's configuration snapshot could not be read, so its mod list and type files will not be restored.");
        }

        if (stored.Status == ConfigLoadStatus.Missing)
        {
            return new LoadCompatibilityReport(
                false, Array.Empty<string>(), Array.Empty<string>(),
                "This save has no configuration snapshot, so its mod list and type files will not be restored.");
        }

        if (stored.Status == ConfigLoadStatus.Corrupt)
        {
            return new LoadCompatibilityReport(
                false, Array.Empty<string>(), Array.Empty<string>(),
                "This save's configuration snapshot is unreadable, so its mod list and type files will not be restored.");
        }

        SaveMetaData saved = stored.Value!;
        List<string> missingFromWorkshop = saved.ModList
            .Where(mod => !_allMods.Contains(mod, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new LoadCompatibilityReport(true, saved.ModList, missingFromWorkshop, string.Empty);
    }

    private void LogSaveResult(SaveGameResult result)
    {
        if (result.Informational)
        {
            _log.Info(result.Message);
        }
        else if (result.Success)
        {
            _log.Success(result.Message);
        }
        else
        {
            _log.Error(result.Message);
        }

        foreach (string warning in result.Warnings)
        {
            _log.Warning(warning);
        }
    }

    /// <summary>
    /// Refuses a progress-save operation while the DayZ server is running, since
    /// it may be writing to the live storage folder. Shows a modal error plus a log
    /// line and returns true when the operation was blocked.
    /// </summary>
    private bool RefuseWhileServerRunning(string purpose)
    {
        if (!_serverProcess.IsDayZServerRunning())
        {
            return false;
        }

        string message = $"The DayZ server is running. Stop it before {purpose} to avoid corrupting the saved progress data.";
        _log.Error(message);
        _dialogs.ShowMessage(message, "DayZ Server running", isError: true);
        return true;
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
            CaptureConfiguredTypes();
            _log.Success(successMessage);
            RebuildRows();
            NotifyCommandStates();
        }
        else
        {
            _log.Error("Operation failed.");
        }
    }

    /// <summary>
    /// Mirrors the live types folder and its mapping into the per-map backup so a
    /// later Load Save preserves the user's configured types for the next New Game.
    /// Best-effort: a failure is logged but never fails the completed operation.
    /// </summary>
    private void CaptureConfiguredTypes()
    {
        string mapName = _typesConfig.CurrentMap;
        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(_serverPath))
        {
            return;
        }

        _typesConfig.Maps.TryGetValue(mapName, out MapTypesConfig? configured);
        TypesBackupResult result = _typesBackup.Capture(
            _serverPath, mapName, _dataDirectoryProvider.Current, configured ?? new MapTypesConfig());
        if (!result.Success)
        {
            foreach (string message in result.Messages)
            {
                _log.Warning(message);
            }
        }
    }

    private void RebuildRows()
    {
        TypesRows.Clear();
        if (string.IsNullOrEmpty(_typesConfig.CurrentMap))
        {
            return;
        }

        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_typesConfig.Maps.TryGetValue(_typesConfig.CurrentMap, out MapTypesConfig? map))
        {
            var valid = new HashSet<string>(_allMods, StringComparer.Ordinal);
            var loaded = LoadedSet();
            foreach (ModTypesEntry entry in map.Mods)
            {
                bool inactive = !valid.Contains(entry.ModName) || !loaded.Contains(entry.ModName);
                foreach (string generated in entry.GeneratedFiles)
                {
                    string leaf = Path.GetFileName(generated);
                    if (string.IsNullOrWhiteSpace(leaf))
                    {
                        continue;
                    }

                    owned.Add(leaf);
                    TypesRows.Add(new TypesRowViewModel(entry.ModName, leaf, inactive));
                }
            }
        }

        AddUntrackedRows(owned);
    }

    /// <summary>
    /// Appends rows for XML files that physically exist in the mission's
    /// <c>db\ModTypes</c> folder but are not tracked by the types configuration,
    /// so orphaned files cannot silently linger. File system failures are ignored
    /// so they never break the grid.
    /// </summary>
    private void AddUntrackedRows(IReadOnlySet<string> ownedLeaves)
    {
        if (string.IsNullOrWhiteSpace(_serverPath) || string.IsNullOrWhiteSpace(_typesConfig.CurrentMap))
        {
            return;
        }

        try
        {
            string missionPath = Path.Combine(_serverPath, "mpmissions", _typesConfig.CurrentMap);
            string typesFolder = Path.Combine(missionPath, "db", "ModTypes");
            foreach (string file in _fileSystem.GetFiles(typesFolder, "*.xml", recursive: false))
            {
                string leaf = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(leaf) || ownedLeaves.Contains(leaf))
                {
                    continue;
                }

                TypesRows.Add(new TypesRowViewModel(UntrackedLabel, leaf, isInactive: false, isUntracked: true));
            }
        }
        catch (Exception)
        {
            // Best effort: a locked/missing folder must not break the row grid.
        }
    }

    private void NotifyCommandStates()
    {
        ConfigXmlCommand.RaiseCanExecuteChanged();
        OpenModTypesFolderCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        CleanInvalidCommand.RaiseCanExecuteChanged();
        NotifyTypesEditingChanged();
    }

    /// <summary>
    /// Blocks a types-mutating action while a world exists (DayZ only reads type
    /// files when a new world is created). Returns true when the action was blocked.
    /// </summary>
    private bool GuardTypesEditing(string action)
    {
        if (TypesEditingAllowed)
        {
            return false;
        }

        _log.Warning($"Type files can only be configured before a world exists. Start a New Game before {action}.");
        return true;
    }

    /// <summary>
    /// Opens the active map's mission <c>db\ModTypes</c> folder in File Explorer.
    /// Gated by the same condition as the Config XML button (a map is applied and
    /// no world exists). The folder is created when missing so Explorer always
    /// lands on it; failures are logged, never thrown.
    /// </summary>
    private void OpenModTypesFolder()
    {
        if (!TypesEditingAllowed)
        {
            return;
        }

        string folder = Path.Combine(_serverPath, "mpmissions", _typesConfig.CurrentMap, "db", "ModTypes");
        try
        {
            _fileSystem.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open the ModTypes folder: {ex.Message}");
            return;
        }

        try
        {
            _processLauncher.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open the ModTypes folder: {ex.Message}");
        }
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

    /// <summary>
    /// What the Load Save flow needs from a stored save: whether it has a
    /// configuration snapshot (so the mod list and type files can be restored),
    /// the mod list it was created with, and any saved mods missing from the
    /// Workshop (which blocks the load).
    /// </summary>
    private sealed record LoadCompatibilityReport(
        bool HasSnapshot,
        IReadOnlyList<string> SavedModList,
        IReadOnlyList<string> MissingSavedMods,
        string SnapshotWarning)
    {
        public bool HasMissingSavedMods => MissingSavedMods.Count > 0;
    }

    private static bool ContainsIgnoreCase(ObservableCollection<string> items, string value) =>
        items.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
}
