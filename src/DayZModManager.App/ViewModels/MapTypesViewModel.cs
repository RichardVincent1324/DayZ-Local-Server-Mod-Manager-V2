using System.Collections.ObjectModel;
using System.IO;
using DayZModManager.Core;
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
    private readonly IPresetService _presetService;
    private readonly IJunctionService _junctions;

    private string _serverPath = string.Empty;
    private string _workshopPath = string.Empty;
    private string _batFileName = string.Empty;
    private string _activePresetName = PresetPaths.DefaultPresetName;
    private List<string> _allMods = new();
    private IReadOnlyList<string> _loadedMods = Array.Empty<string>();
    private IReadOnlyList<MapInfo> _discoveredMaps = Array.Empty<MapInfo>();

    private string? _selectedMap;
    private string? _selectedMod;
    private SaveListEntryViewModel? _selectedSave;
    private bool _isRestoring;
    private bool _isSwitching;
    private bool _typesBusy;
    private bool _isSaveBusy;
    private bool _isSwitchingPreset;
    private bool _isRestoringPreset;
    private bool _isRestoringMod;
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
        IDataDirectoryProvider dataDirectoryProvider,
        IDayZServerProcessState serverProcess,
        IProcessLauncher processLauncher,
        IPresetService presetService,
        IJunctionService junctions)
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
        _serverProcess = serverProcess;
        _processLauncher = processLauncher;
        _presetService = presetService;
        _junctions = junctions;

        ConfigureModCommand = new RelayCommand<string>(ConfigureMod, () => TypesEditingAllowed);
        OpenModTypesFolderCommand = new RelayCommand(OpenModTypesFolder, () => TypesEditingAllowed);
        OpenMapProfilesFolderCommand = new RelayCommand(OpenMapProfilesFolder);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => TypesEditingAllowed && CanRemoveSelected);
        CleanInvalidCommand = new RelayCommand(CleanInvalid, () => TypesEditingAllowed);
        LoadSaveCommand = new RelayCommand(LoadSave, () => SelectedSave is { IsOrphaned: false } && !IsSaveBusy);
        DeleteSaveCommand = new RelayCommand(DeleteSave, () => SelectedSave is not null && !IsSaveBusy);
        RenameSaveCommand = new RelayCommand(RenameSave, () => SelectedSave is { IsOrphaned: false } && !IsSaveBusy);
        AddSaveCommand = new RelayCommand(AddSave, () => !IsSaveBusy);
        WipeWorldCommand = new RelayCommand(WipeWorld, () => !IsSaveBusy);
        AddPresetCommand = new RelayCommand(AddPreset, () => !IsBusy && !string.IsNullOrEmpty(_typesConfig.CurrentMap));
        DuplicatePresetCommand = new RelayCommand(
            DuplicatePreset, () => !IsBusy && !string.IsNullOrEmpty(_typesConfig.CurrentMap) && SelectedPreset is not null);
        RenamePresetCommand = new RelayCommand(RenamePreset, () => SelectedPreset is { IsDefault: false } && !IsBusy);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset is { IsDefault: false } && !IsBusy);

        SelectedTypesRows.CollectionChanged += (_, _) => NotifyCommandStates();
    }

    /// <summary>
    /// The preset whose environment (types, mod order, profiles, instance ID) this
    /// page operates on. Set by the shell when the active preset changes. Defaults
    /// to the map's reserved default preset.
    /// </summary>
    public string ActivePresetName
    {
        get => _activePresetName;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? PresetPaths.DefaultPresetName : value;
            if (SetField(ref _activePresetName, normalized))
            {
                RebuildRows();
                RefreshSaves();
                NotifyCommandStates();
            }
        }
    }

    /// <summary>Folder of the active preset for the given map.</summary>
    private string PresetFolder(string mapName) =>
        PresetPaths.PresetFolder(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>Preset ModTypes folder for the given map.</summary>
    private string PresetModTypesFolder(string mapName) =>
        PresetPaths.ModTypesFolder(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>Preset saves folder for the given map.</summary>
    private string PresetSavesFolder(string mapName) =>
        PresetPaths.SavesFolder(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>The active preset's dedicated instance ID for the given map.</summary>
    private int PresetInstanceId(string mapName) =>
        _presetService.ReadInstanceId(_dataDirectoryProvider.Current, mapName, _activePresetName);

    /// <summary>Where the current types operation writes files and economy references.</summary>
    private TypesTarget ActiveTypesTarget(string missionPath) =>
        new(missionPath, PresetModTypesFolder(_typesConfig.CurrentMap), ActiveCeFolderValue(missionPath));

    /// <summary>
    /// Invoked when the user selects a different preset. Loads the preset's mod
    /// order and types configuration, persists it as the map's active preset, and
    /// applies it. Provided by the shell. Returns false on failure.
    /// </summary>
    public Func<string, string, Task<bool>>? ActivatePreset { get; set; }

    /// <summary>Invoked to resolve the persisted active preset for a map. Provided by the shell.</summary>
    public Func<string, string>? ResolvePresetForMap { get; set; }

    /// <summary>The presets available for the current map.</summary>
    public ObservableCollection<PresetItemViewModel> Presets { get; } = new();

    private PresetItemViewModel? _selectedPreset;

    /// <summary>
    /// The selected preset. Selecting a different preset makes it the map's active
    /// preset (loading its configuration and applying it). Programmatic restoration
    /// is flagged so it never re-activates.
    /// </summary>
    public PresetItemViewModel? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetField(ref _selectedPreset, value))
            {
                return;
            }

            DuplicatePresetCommand.RaiseCanExecuteChanged();

            if (!_isRestoringPreset
                && value is not null
                && !string.Equals(value.Name, _activePresetName, StringComparison.Ordinal))
            {
                _ = SwitchPresetAsync(value.Name);
            }
        }
    }

    public ObservableCollection<string> MapNames { get; } = new();

    public ObservableCollection<string> ModNames { get; } = new();

    public ObservableCollection<TypesRowViewModel> TypesRows { get; } = new();

    public ObservableCollection<TypesRowViewModel> SelectedTypesRows { get; } = new();

    /// <summary>
    /// The currently selected map. Selecting a different map applies it
    /// immediately (server template, the active preset's profiles and config, and economy).
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

    /// <summary>
    /// The mod selected in the Types Config dropdown. Selecting a mod runs the
    /// configure flow and then clears the selection, so the dropdown behaves as an
    /// action (like the Current Map dropdown) and the same mod can be re-selected.
    /// </summary>
    public string? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (!SetField(ref _selectedMod, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsModSelectionEmpty));

            if (_isRestoringMod || string.IsNullOrEmpty(value))
            {
                return;
            }

            HandleModSelection(value);
        }
    }

    /// <summary>True when no mod is selected; drives the Types Config placeholder overlay.</summary>
    public bool IsModSelectionEmpty => string.IsNullOrEmpty(_selectedMod);

    public SaveListEntryViewModel? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (SetField(ref _selectedSave, value))
            {
                LoadSaveCommand.RaiseCanExecuteChanged();
                DeleteSaveCommand.RaiseCanExecuteChanged();
                RenameSaveCommand.RaiseCanExecuteChanged();
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
                WipeWorldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Stored progress saves for the current map, plus any unattached
    /// (orphaned) live storage folders that no preset owns.
    /// </summary>
    public ObservableCollection<SaveListEntryViewModel> SaveNames { get; } = new();

    private bool CanRemoveSelected => SelectedTypesRows.Count > 0;

    /// <summary>
    /// True when types may be configured: a map is applied and no world exists.
    /// DayZ only reads type files when a new world is created, so once a world
    /// (storage folder) is present editing is locked until the world is wiped.
    /// </summary>
    public bool TypesEditingAllowed =>
        !string.IsNullOrWhiteSpace(_typesConfig.CurrentMap) && !WorldExists(_typesConfig.CurrentMap);

    /// <summary>True when a world exists and types editing is therefore locked.</summary>
    public bool IsTypesLocked =>
        !string.IsNullOrWhiteSpace(_typesConfig.CurrentMap) && WorldExists(_typesConfig.CurrentMap);

    /// <summary>Banner shown while types editing is locked; empty when editing is allowed.</summary>
    public string TypesLockedMessage =>
        IsTypesLocked
            ? "Types editing is disabled while a world exists. Wipe the current world to reconfigure."
            : string.Empty;

    /// <summary>
    /// Tooltip for the ModTypes folder button: an action hint when it is available,
    /// or the lock reason (same as the Config XML button) when editing is disabled.
    /// </summary>
    public string OpenModTypesFolderToolTip =>
        TypesEditingAllowed ? "Open ModTypes folder in File Explorer" : TypesLockedMessage;

    /// <summary>Tooltip for the always-available preset profiles folder button.</summary>
    public string MapProfilesFolderToolTip => "Open the preset profiles folder in File Explorer";

    /// <summary>True when the mission's storage folder exists (a world has been created).</summary>
    private bool WorldExists(string mapName)
    {
        if (string.IsNullOrWhiteSpace(_serverPath) || string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        try
        {
            return _fileSystem.DirectoryExists(_saveGameService.GetStorageFolderPath(_serverPath, mapName, PresetInstanceId(mapName)));
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
    /// progress-save operation (load/add/delete/wipe world) is in flight. Used by the
    /// Start Server action so it never launches the server while the launch batch,
    /// mission files, or the live storage folder are being rewritten or deleted.
    /// </summary>
    public bool IsBusy => _isSwitching || _typesBusy || _isSaveBusy || _isSwitchingPreset;

    private void NotifyBusyChanged() => OnPropertyChanged(nameof(IsBusy));

    /// <summary>
    /// Invoked before configuring types. Returns true when the current in-memory
    /// state is persisted (either already or just applied), false if Apply failed.
    /// </summary>
    public Func<Task<bool>>? EnsureApplied { get; set; }

    public RelayCommand<string> ConfigureModCommand { get; }
    public RelayCommand OpenModTypesFolderCommand { get; }
    public RelayCommand OpenMapProfilesFolderCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand CleanInvalidCommand { get; }
    public RelayCommand LoadSaveCommand { get; }
    public RelayCommand AddSaveCommand { get; }
    public RelayCommand DeleteSaveCommand { get; }
    public RelayCommand RenameSaveCommand { get; }
    public RelayCommand WipeWorldCommand { get; }
    public RelayCommand AddPresetCommand { get; }
    public RelayCommand DuplicatePresetCommand { get; }
    public RelayCommand RenamePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }

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
        RefreshPresets();
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
        ModNames.Clear();
        foreach (string mod in loadedMods)
        {
            ModNames.Add(mod);
        }

        // The dropdown is an action, not persistent state: always start with no
        // selection so the placeholder shows and any mod can be selected again.
        SetRestoringModSelection(null);
    }

    /// <summary>Sets <see cref="SelectedMod"/> without triggering the configure action.</summary>
    private void SetRestoringModSelection(string? modName)
    {
        _isRestoringMod = true;
        try
        {
            SelectedMod = modName;
        }
        finally
        {
            _isRestoringMod = false;
        }
    }

    /// <summary>
    /// Runs the configure flow for a mod chosen from the Types Config dropdown and
    /// clears the selection so the action can be repeated. Deferred by one dispatcher
    /// tick so the modal picker does not open while the ComboBox is still updating.
    /// </summary>
    private async void HandleModSelection(string modName)
    {
        await Task.Yield();
        SetRestoringModSelection(null);
        ConfigureModCommand.Execute(modName);
    }

    /// <summary>
    /// Regenerates the manager-owned ModTypes block in cfgeconomycore.xml for the
    /// applied map. The block points at whichever types are active: the configured
    /// <c>db\ModTypes</c>, or the loaded save's own ModTypes folder.
    /// </summary>
    public bool SyncEconomyCore(IReadOnlySet<string>? previouslyOwned = null)
    {
        string? missionPath = ResolveAppliedMapPath();
        if (missionPath is null)
        {
            return false;
        }

        bool updated = _typesService.SyncEconomyCore(
            ActiveMapConfig(), missionPath, ActiveCeFolderValue(missionPath), LoadedSet(), previouslyOwned);
        if (!updated)
        {
            _log.Warning("Failed to update cfgeconomycore.xml.");
        }

        return updated;
    }

    private HashSet<string> LoadedSet() => new(_loadedMods, StringComparer.OrdinalIgnoreCase);

    /// <summary>The active types mapping: the active preset's configured types.</summary>
    private MapTypesConfig? ActiveMapConfig() => CurrentMapConfig();

    /// <summary>Returns the current map's configured types config, if present.</summary>
    private MapTypesConfig? CurrentMapConfig() =>
        string.IsNullOrEmpty(_typesConfig.CurrentMap)
            ? null
            : _typesConfig.Maps.TryGetValue(_typesConfig.CurrentMap, out MapTypesConfig? map) ? map : null;

    /// <summary>The absolute folder holding the active preset's generated types files.</summary>
    private string? ActiveTypesFolderAbsolute()
    {
        if (string.IsNullOrWhiteSpace(_serverPath) || string.IsNullOrEmpty(_typesConfig.CurrentMap))
        {
            return null;
        }

        return PresetModTypesFolder(_typesConfig.CurrentMap);
    }

    /// <summary>
    /// The <c>folder</c> value for the manager-owned cfgeconomycore.xml block: the
    /// active preset's ModTypes folder relative to the mission (forward slashes).
    /// </summary>
    private string ActiveCeFolderValue(string missionPath)
    {
        string? activeFolder = ActiveTypesFolderAbsolute();
        if (activeFolder is null)
        {
            return EconomyCoreService.ConfiguredFolder;
        }

        return Path.GetRelativePath(missionPath, activeFolder).Replace('\\', '/');
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
        string dataDirectory = _dataDirectoryProvider.Current;

        // Switching maps uses that map's active preset (persisted from a previous
        // session), falling back to the reserved default preset. The default
        // preset is always (re)created so the map always has a working environment.
        PresetResult ensured = _presetService.EnsureDefaultPreset(_serverPath, dataDirectory, mapName);
        if (!ensured.Success)
        {
            _log.Error(ensured.Message);
            return false;
        }

        string requestedPreset = ResolvePresetForMap?.Invoke(mapName) ?? PresetPaths.DefaultPresetName;
        if (!_presetService.PresetExists(dataDirectory, mapName, requestedPreset))
        {
            requestedPreset = PresetPaths.DefaultPresetName;
        }

        _activePresetName = requestedPreset;
        string presetConfig = PresetPaths.ServerConfigPath(dataDirectory, mapName, _activePresetName);

        if (!_serverConfig.UpdateTemplate(presetConfig, mapName))
        {
            _log.Error("Failed to update the preset server template (serverDZ.cfg); the map was not switched.");
            return false;
        }

        // Point the launcher at the active preset's environment: its profiles and
        // its serverDZ.cfg, both expressed relative to the server root.
        string batPath = Path.Combine(_serverPath, _batFileName);
        string profileValue = PresetPaths.RelativeToServer(
            _serverPath, PresetPaths.ProfilesFolder(dataDirectory, mapName, _activePresetName));
        string configValue = PresetPaths.RelativeToServer(_serverPath, presetConfig);

        if (!_batchFile.WriteServerProfile(batPath, profileValue))
        {
            // Roll the template back so the server files stay on the previous map.
            RollbackMapFiles(previousMap, mapName);
            _log.Error("Failed to update the batch file serverProfile; the map was not switched.");
            return false;
        }

        if (!_batchFile.WriteServerConfig(batPath, configValue))
        {
            // Templates without a serverConfig line fall back to the server root.
            TryCopyPresetConfigToRoot(mapName);
            _log.Warning("Batch file has no serverConfig line; the preset serverDZ.cfg was copied to the server root instead.");
        }

        // Commit: only after the server files were updated successfully.
        _typesConfig.CurrentMap = mapName;
        try
        {
            _typesConfigStore.Save(PresetFolder(mapName), _typesConfig);
        }
        catch (Exception ex)
        {
            _typesConfig.CurrentMap = previousMap;
            RollbackMapFiles(previousMap, mapName);
            _log.Error($"Failed to persist the applied map; the map was not switched: {ex.Message}");
            return false;
        }

        // Pre-create the preset profiles folder the batch serverProfile points at.
        // DayZ also creates it on its first boot; a failure is non-fatal.
        try
        {
            _fileSystem.CreateDirectory(PresetPaths.ProfilesFolder(dataDirectory, mapName, _activePresetName));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to create the preset profiles directory: {ex.Message}");
        }

        NotifyCommandStates();
        RebuildRows();
        SyncEconomyCore();
        RefreshPresets();
        RefreshSaves();
        _log.Success($"Map switched to: {mapName}");
        return true;
    }

    /// <summary>
    /// Best-effort copy of the active preset's serverDZ.cfg to the server root,
    /// used when the launch template has no serverConfig line.
    /// </summary>
    private void TryCopyPresetConfigToRoot(string mapName)
    {
        string presetConfig = PresetPaths.ServerConfigPath(
            _dataDirectoryProvider.Current, mapName, _activePresetName);
        string rootConfig = Path.Combine(_serverPath, "serverDZ.cfg");
        try
        {
            if (_fileSystem.FileExists(presetConfig))
            {
                _fileSystem.CopyFile(presetConfig, rootConfig);
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to copy the preset serverDZ.cfg to the server root: {ex.Message}");
        }
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

        string dataDirectory = _dataDirectoryProvider.Current;
        string previousConfig = PresetPaths.ServerConfigPath(dataDirectory, previousMap, _activePresetName);
        if (!_serverConfig.UpdateTemplate(previousConfig, previousMap))
        {
            _log.Warning("Failed to restore the previous server template after the map switch was aborted.");
        }

        string profileValue = PresetPaths.RelativeToServer(
            _serverPath, PresetPaths.ProfilesFolder(dataDirectory, previousMap, _activePresetName));
        if (!_batchFile.WriteServerProfile(Path.Combine(_serverPath, _batFileName), profileValue))
        {
            _log.Warning("Failed to restore the previous batch serverProfile after the map switch was aborted.");
        }
    }

    private async void ConfigureMod(string? modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
        {
            _log.Warning("Select a mod to configure first.");
            return;
        }

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
            string? missionPath = ResolveAppliedMapPath();
            if (missionPath is null)
            {
                return;
            }

            IReadOnlyList<string> files = _typesService.DiscoverXmlFiles(_workshopPath, modName);
            _log.Info($"Found {files.Count} XML file(s) in {modName}.");

            // Pre-select only the files that are currently configured for this
            // mod (and restore an unrecognized file's assigned role) so a re-run
            // with no edits does not silently change anything. Matching is by
            // mod-relative source path so a disambiguated (numbered) generated
            // name is handled transparently.
            string modFolderPath = Path.Combine(_workshopPath, modName);
            IReadOnlyList<ConfiguredTypeFile> configuredFiles =
                _typesService.GetConfiguredFiles(_typesConfig, _typesConfig.CurrentMap, modName);
            var configuredBySource = new Dictionary<string, ConfiguredTypeFile>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfiguredTypeFile configured in configuredFiles)
            {
                if (!string.IsNullOrWhiteSpace(configured.SourceRelative))
                {
                    configuredBySource[configured.SourceRelative] = configured;
                }
            }

            var activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var activeRoles = new Dictionary<string, TypesFileRole>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(modFolderPath, file);
                if (configuredBySource.TryGetValue(relative, out ConfiguredTypeFile? configured))
                {
                    activeFiles.Add(file);
                    activeRoles[file] = configured.Role;
                }
            }

            IReadOnlyList<TypeFileSelection>? selected = _dialogs.PickTypeFiles(modName, files, activeFiles, activeRoles, modFolderPath);
            if (selected is null || selected.Count == 0)
            {
                return;
            }

            // Reconfiguring a mod that is already configured overwrites its files
            // in db/ModTypes; never do that silently, even when the same file is
            // selected again.
            if (configuredFiles.Count > 0)
            {
                var selectedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (TypeFileSelection selection in selected)
                {
                    selectedSources.Add(Path.GetRelativePath(modFolderPath, selection.SourceFile));
                }

                List<string> removed = configuredFiles
                    .Where(configured => !selectedSources.Contains(configured.SourceRelative))
                    .Select(configured => string.IsNullOrWhiteSpace(configured.GeneratedLeaf)
                        ? configured.SourceRelative
                        : configured.GeneratedLeaf)
                    .ToList();
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
                _typesConfig, _typesConfig.CurrentMap, ActiveTypesTarget(missionPath), _workshopPath, modName, selected, LoadedSet());

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
                    _typesConfig, _typesConfig.CurrentMap, ActiveTypesTarget(missionPath), group.Key, leaves, LoadedSet());
                messages.AddRange(result.Messages);
                if (!result.Success)
                {
                    success = false;
                }
            }

            if (untrackedLeaves.Count > 0)
            {
                TypesOperationResult result = _typesService.RemoveUntrackedFiles(
                    _typesConfig, _typesConfig.CurrentMap, ActiveTypesTarget(missionPath), untrackedLeaves);
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
                    _typesConfigStore.Save(PresetFolder(_typesConfig.CurrentMap), _typesConfig);
                }

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
                _typesConfig, _typesConfig.CurrentMap, ActiveTypesTarget(missionPath), active, LoadedSet());

            foreach (string message in result.Messages)
            {
                _log.Info(message);
            }

            if (result.Success)
            {
                _typesConfigStore.Save(PresetFolder(_typesConfig.CurrentMap), _typesConfig);
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
        SaveListEntryViewModel? entry = SelectedSave;
        if (mapName is null || entry is null)
        {
            if (entry is null)
            {
                _log.Warning("Select a stored save first.");
            }

            return;
        }

        if (entry.IsOrphaned)
        {
            _log.Warning("Only stored saves can be loaded; unattached storage cannot.");
            return;
        }

        string saveName = entry.Name;

        if (RefuseWhileServerRunning("loading a save"))
        {
            return;
        }

		string message =
			$"Load save \"{saveName}\" (map: {mapName})?\n\n" +
			$"This will overwrite your current progress in {StorageLabel(mapName)} and replace the mission's type file configuration with the stored copy, " +
			$"and set the loaded mod list to this save's. " +
			$"Your configured type settings are preserved until you wipe the world. " +
			$"The current progress will be lost.";
	
        bool confirmed = _dialogs.Confirm(message, "Load Save");

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
        int instanceId = PresetInstanceId(mapName);
        string savesFolder = PresetSavesFolder(mapName);
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(
                () => _saveGameService.LoadSave(serverPath, mapName, savesFolder, instanceId, saveName));
            LogSaveResult(result);

            if (result.Success)
            {
                // A save only restores world state. The preset's configuration
                // (mods, types, profiles, instance ID) is left untouched.
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
        bool exists = SaveNames.Any(entry =>
            !entry.IsOrphaned && string.Equals(entry.Name, trimmed, StringComparison.OrdinalIgnoreCase));
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
        int instanceId = PresetInstanceId(mapName);
        string savesFolder = PresetSavesFolder(mapName);
        bool overwrite = exists;
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(
                () => _saveGameService.AddSave(serverPath, mapName, savesFolder, instanceId, trimmed, overwrite));
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
        SaveListEntryViewModel? entry = SelectedSave;
        if (mapName is null || entry is null)
        {
            if (entry is null)
            {
                _log.Warning("Select a stored save first.");
            }

            return;
        }

        if (entry.IsOrphaned)
        {
            await DeleteOrphanStorageAsync(mapName, entry);
            return;
        }

        string saveName = entry.Name;
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

        string savesFolder = PresetSavesFolder(mapName);
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(() => _saveGameService.DeleteSave(savesFolder, saveName));
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

    /// <summary>
    /// Deletes a live storage folder that no preset owns. Refuses while the
    /// server is running and always confirms, since it destroys world data that
    /// this tool did not create.
    /// </summary>
    private async Task DeleteOrphanStorageAsync(string mapName, SaveListEntryViewModel entry)
    {
        if (entry.InstanceId is not int instanceId)
        {
            return;
        }

        if (RefuseWhileServerRunning("deleting unattached storage"))
        {
            return;
        }

        bool confirmed = _dialogs.Confirm(
            $"Delete unattached storage \"{entry.Name}\" (map: {mapName})?\n\n" +
            "This folder is not owned by any preset. It may be world data left behind by a deleted preset or created before this tool managed the server. " +
            "This cannot be undone.",
            "Delete Unattached Storage");
        if (!confirmed)
        {
            _log.Info("Deletion cancelled.");
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
            SaveGameResult result = await Task.Run(
                () => _saveGameService.DeleteStorage(serverPath, mapName, instanceId));
            LogSaveResult(result);
            if (result.Success)
            {
                RefreshSaves();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to delete unattached storage: {ex.Message}");
        }
        finally
        {
            IsSaveBusy = false;
        }
    }

    private async void WipeWorld()
    {
        string? mapName = AppliedMapOrWarn();
        if (mapName is null)
        {
            return;
        }

        if (RefuseWhileServerRunning("wiping the world"))
        {
            return;
        }

        bool confirmed = _dialogs.Confirm(
            $"Wipe the current world on map {mapName}?\n\nThis will DELETE {StorageLabel(mapName)} so the map starts fresh on the next server launch. Continue?",
            "Wipe World");
        if (!confirmed)
        {
            _log.Info("Wipe cancelled.");
            return;
        }

        if (IsSaveBusy)
        {
            return;
        }

        string serverPath = _serverPath;
        int instanceId = PresetInstanceId(mapName);
        IsSaveBusy = true;
        try
        {
            SaveGameResult result = await Task.Run(() => _saveGameService.WipeWorld(serverPath, mapName, instanceId));
            LogSaveResult(result);
            if (result.Success)
            {
                NotifyCommandStates();
                RebuildRows();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to wipe the world: {ex.Message}");
        }
        finally
        {
            IsSaveBusy = false;
        }
    }

    /// <summary>Rebuilds the preset list for the current map.</summary>
    private void RefreshPresets()
    {
        string mapName = _typesConfig.CurrentMap;
        Presets.Clear();

        if (string.IsNullOrWhiteSpace(mapName))
        {
            SetRestoringPresetSelection(null);
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in _presetService.ListPresetNames(_dataDirectoryProvider.Current, mapName))
        {
            if (seen.Add(name))
            {
                Presets.Add(new PresetItemViewModel(name));
            }
        }

        // The reserved default always exists as a selection target even before the
        // preset has been materialised on disk.
        if (!Presets.Any(p => p.IsDefault))
        {
            Presets.Insert(0, new PresetItemViewModel(PresetPaths.DefaultPresetName));
        }

        PresetItemViewModel? current =
            Presets.FirstOrDefault(p => string.Equals(p.Name, _activePresetName, StringComparison.Ordinal))
            ?? Presets.First();
        SetRestoringPresetSelection(current);
        NotifyCommandStates();
    }

    private void SetRestoringPresetSelection(PresetItemViewModel? preset)
    {
        _isRestoringPreset = true;
        try
        {
            SelectedPreset = preset;
        }
        finally
        {
            _isRestoringPreset = false;
        }
    }

    private async Task SwitchPresetAsync(string presetName)
    {
        if (string.Equals(presetName, _activePresetName, StringComparison.Ordinal))
        {
            return;
        }

        string mapName = _typesConfig.CurrentMap;
        if (string.IsNullOrWhiteSpace(mapName) || _isSwitchingPreset)
        {
            return;
        }

        if (RefuseWhileServerRunning("switching presets"))
        {
            SetRestoringPresetSelection(
                Presets.FirstOrDefault(p => string.Equals(p.Name, _activePresetName, StringComparison.Ordinal)));
            return;
        }

        _isSwitchingPreset = true;
        NotifyBusyChanged();
        try
        {
            bool ok = ActivatePreset is not null && await ActivatePreset(mapName, presetName);
            if (!ok)
            {
                SetRestoringPresetSelection(
                    Presets.FirstOrDefault(p => string.Equals(p.Name, _activePresetName, StringComparison.Ordinal)));
                return;
            }

            RebuildRows();
            RefreshSaves();
            NotifyCommandStates();
            _log.Success($"Preset switched to: {presetName}");
        }
        catch (Exception ex)
        {
            _log.Error($"Preset switch failed: {ex.Message}");
        }
        finally
        {
            _isSwitchingPreset = false;
            NotifyBusyChanged();
        }
    }

    private async void AddPreset()
    {
        string? mapName = AppliedMapOrWarn();
        if (mapName is null)
        {
            return;
        }

        AddPresetRequest? request = _dialogs.AskAddPreset(mapName);
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return;
        }

        PresetResult result = _presetService.CreatePreset(
            _serverPath, _dataDirectoryProvider.Current, mapName, request.Name,
            request.CopyProfilesFromDefault);
        if (!result.Success)
        {
            _log.Error(result.Message);
            _dialogs.ShowMessage(result.Message, "Add Preset", isError: true);
            return;
        }

        _log.Success(result.Message);
        RefreshPresets();

        PresetItemViewModel? created = Presets.FirstOrDefault(
            p => string.Equals(p.Name, request.Name.Trim(), StringComparison.Ordinal));
        if (created is not null)
        {
            SetRestoringPresetSelection(created);
            await SwitchPresetAsync(created.Name);
        }
    }

    /// <summary>
    /// Creates a copy of the currently selected preset so the user can experiment
    /// with mods and types without touching the original. The copy always inherits
    /// the source's server config, mod order, types config and ModTypes; profiles
    /// are copied only when the user opts in.
    /// </summary>
    private async void DuplicatePreset()
    {
        string? mapName = AppliedMapOrWarn();
        PresetItemViewModel? source = SelectedPreset;
        if (mapName is null || source is null)
        {
            return;
        }

        DuplicatePresetRequest? request = _dialogs.AskDuplicatePreset(source.Name);
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return;
        }

        PresetResult result = _presetService.DuplicatePreset(
            _serverPath, _dataDirectoryProvider.Current, mapName, source.Name,
            request.Name, request.CopyProfiles);
        if (!result.Success)
        {
            _log.Error(result.Message);
            _dialogs.ShowMessage(result.Message, "Duplicate Preset", isError: true);
            return;
        }

        _log.Success(result.Message);
        RefreshPresets();

        PresetItemViewModel? created = Presets.FirstOrDefault(
            p => string.Equals(p.Name, request.Name.Trim(), StringComparison.Ordinal));
        if (created is not null)
        {
            SetRestoringPresetSelection(created);
            await SwitchPresetAsync(created.Name);
        }
    }

    private void RenamePreset()
    {
        string mapName = _typesConfig.CurrentMap;
        PresetItemViewModel? preset = SelectedPreset;
        if (string.IsNullOrWhiteSpace(mapName) || preset is null || preset.IsDefault)
        {
            return;
        }

        string? newName = _dialogs.AskText("Rename Preset", $"Rename preset \"{preset.Name}\" to:", preset.Name);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        string trimmed = newName.Trim();
        if (string.Equals(trimmed, preset.Name, StringComparison.Ordinal))
        {
            return;
        }

        PresetResult result = _presetService.RenamePreset(_dataDirectoryProvider.Current, mapName, preset.Name, trimmed);
        if (!result.Success)
        {
            _log.Error(result.Message);
            return;
        }

        _log.Success(result.Message);
        bool wasActive = string.Equals(_activePresetName, preset.Name, StringComparison.Ordinal);
        RefreshPresets();

        if (wasActive && ActivatePreset is not null)
        {
            _ = ActivatePreset(mapName, trimmed);
        }
    }

    private void DeletePreset()
    {
        string mapName = _typesConfig.CurrentMap;
        PresetItemViewModel? preset = SelectedPreset;
        if (string.IsNullOrWhiteSpace(mapName) || preset is null || preset.IsDefault)
        {
            return;
        }

        if (!_dialogs.Confirm(
                $"Delete preset \"{preset.Name}\"? Its configuration, profiles and saves will be removed. This cannot be undone.",
                "Delete Preset"))
        {
            return;
        }

        // Read the instance ID before deleting: it is the preset's ModList subfolder.
        int instanceId = _presetService.ReadInstanceId(_dataDirectoryProvider.Current, mapName, preset.Name);

        PresetResult result = _presetService.DeletePreset(_dataDirectoryProvider.Current, mapName, preset.Name);
        if (!result.Success)
        {
            _log.Error(result.Message);
            return;
        }

        // Remove the preset's junction folder so no orphaned links are left behind.
        if (!string.IsNullOrWhiteSpace(_serverPath))
        {
            _junctions.DeleteJunctionFolder(_serverPath, ModListFolder.PresetKey(instanceId));
        }

        _log.Success(result.Message);
        bool wasActive = string.Equals(_activePresetName, preset.Name, StringComparison.Ordinal);
        RefreshPresets();

        if (wasActive && ActivatePreset is not null)
        {
            _ = ActivatePreset(mapName, PresetPaths.DefaultPresetName);
        }
    }

    private void RenameSave()
    {
        string? mapName = AppliedMapOrWarn();
        SaveListEntryViewModel? entry = SelectedSave;
        if (mapName is null || entry is null)
        {
            if (entry is null)
            {
                _log.Warning("Select a stored save first.");
            }

            return;
        }

        if (entry.IsOrphaned)
        {
            _log.Warning("Unattached storage cannot be renamed; only stored saves can.");
            return;
        }

        string saveName = entry.Name;

        if (RefuseWhileServerRunning("renaming a save"))
        {
            return;
        }

        string? newName = _dialogs.AskText("Rename Save", $"Rename save \"{saveName}\" to:", saveName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        SaveGameResult result = _saveGameService.RenameSave(PresetSavesFolder(mapName), saveName, newName.Trim());
        LogSaveResult(result);
        if (result.Success)
        {
            RefreshSaves();
        }
    }

    /// <summary>Returns the applied map name, warning when none is applied yet.</summary>
    private string? AppliedMapOrWarn()
    {
        if (string.IsNullOrWhiteSpace(_typesConfig.CurrentMap))
        {            _log.Warning("Apply a map first before managing progress saves.");
            return null;
        }

        return _typesConfig.CurrentMap;
    }

    private string StorageLabel(string mapName)
    {
        try
        {
            return Path.GetFileName(_saveGameService.GetStorageFolderPath(_serverPath, mapName, PresetInstanceId(mapName)));
        }
        catch (Exception)
        {
            return "storage folder";
        }
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

    /// <summary>
    /// Rebuilds the stored-save list for the current map, then appends any
    /// unattached live storage folders that no preset owns.
    /// </summary>
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
            foreach (string save in _saveGameService.ListSaves(PresetSavesFolder(mapName)))
            {
                SaveNames.Add(new SaveListEntryViewModel(save, isOrphaned: false));
            }

            foreach (int instanceId in FindOrphanStorageInstanceIds(mapName))
            {
                SaveNames.Add(new SaveListEntryViewModel($"storage_{instanceId}", isOrphaned: true, instanceId));
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to list progress saves: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the instance IDs of live storage folders in the map's mission
    /// folder that are not owned by any preset of that map (unattached/orphan
    /// storage). Never throws.
    /// </summary>
    private IReadOnlyList<int> FindOrphanStorageInstanceIds(string mapName)
    {
        try
        {
            string dataDirectory = _dataDirectoryProvider.Current;
            var managed = new HashSet<int>(
                _presetService.ListPresetNames(dataDirectory, mapName)
                    .Select(preset => _presetService.ReadInstanceId(dataDirectory, mapName, preset)));

            return _saveGameService.ListStorageInstanceIds(_serverPath, mapName)
                .Where(id => !managed.Contains(id))
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to scan for unattached storage: {ex.Message}");
            return Array.Empty<int>();
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
            _typesConfigStore.Save(PresetFolder(_typesConfig.CurrentMap), _typesConfig);
            _log.Success(successMessage);
            RebuildRows();
            NotifyCommandStates();
        }
        else
        {
            _log.Error("Operation failed.");
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
        MapTypesConfig? map = ActiveMapConfig();
        if (map is not null)
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
                    TypesRows.Add(new TypesRowViewModel(entry.ModName, leaf, inactive, fileType: FileTypeLabel(entry, leaf)));
                }
            }
        }

        AddUntrackedRows(owned);
    }

    /// <summary>Returns the display label ("type"/"spawnable") for a tracked generated file.</summary>
    private static string FileTypeLabel(ModTypesEntry entry, string leaf)
    {
        TypesFileRole role = entry.FileRoles.TryGetValue(leaf, out string? stored)
            ? TypesFileRoles.ToRole(stored)
            : TypesFileRole.Types;
        return role == TypesFileRole.SpawnableTypes ? "spawnable" : "type";
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
            string? typesFolder = ActiveTypesFolderAbsolute();
            if (typesFolder is null)
            {
                return;
            }

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
        ConfigureModCommand.RaiseCanExecuteChanged();
        OpenModTypesFolderCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        CleanInvalidCommand.RaiseCanExecuteChanged();
        RenameSaveCommand.RaiseCanExecuteChanged();
        AddPresetCommand.RaiseCanExecuteChanged();
        DuplicatePresetCommand.RaiseCanExecuteChanged();
        RenamePresetCommand.RaiseCanExecuteChanged();
        DeletePresetCommand.RaiseCanExecuteChanged();
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

        _log.Warning($"Type files can only be configured before a world exists. Wipe the current world before {action}.");
        return true;
    }

    /// <summary>
    /// Opens the active preset's <c>ModTypes</c> folder in File Explorer. Gated by
    /// the same condition as the Config XML button (a map is applied and no world
    /// exists). The folder is created when missing; failures are logged, never thrown.
    /// </summary>
    private void OpenModTypesFolder()
    {
        if (!TypesEditingAllowed)
        {
            return;
        }

        string folder = PresetModTypesFolder(_typesConfig.CurrentMap);
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

    /// <summary>
    /// Opens the active preset's <c>profiles</c> folder in File Explorer, falling
    /// back to the presets root when no map is applied yet. Always available (it
    /// does not depend on world state). The folder is created when missing;
    /// failures are logged, never thrown.
    /// </summary>
    private void OpenMapProfilesFolder()
    {
        if (string.IsNullOrWhiteSpace(_serverPath))
        {
            _log.Warning("Set the server path before opening the preset profiles folder.");
            return;
        }

        string dataDirectory = _dataDirectoryProvider.Current;
        string folder = string.IsNullOrWhiteSpace(_typesConfig.CurrentMap)
            ? PresetPaths.PresetsRoot(dataDirectory)
            : PresetPaths.ProfilesFolder(dataDirectory, _typesConfig.CurrentMap, _activePresetName);

        try
        {
            _fileSystem.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open the preset profiles folder: {ex.Message}");
            return;
        }

        try
        {
            _processLauncher.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open the preset profiles folder: {ex.Message}");
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

    private static bool ContainsIgnoreCase(ObservableCollection<string> items, string value) =>
        items.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
}
