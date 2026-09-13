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
    private readonly ITypesConfigStore _typesConfigStore;
    private readonly IServerConfigService _serverConfig;
    private readonly IBatchFileService _batchFile;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly LogViewModel _log;
    private readonly TypesConfig _typesConfig;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly IDayZServerProcessState _serverProcess;

    private string _serverPath = string.Empty;
    private string _workshopPath = string.Empty;
    private string _configuredBatchFile = string.Empty;
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
        IDataDirectoryProvider dataDirectoryProvider,
        IDayZServerProcessState serverProcess)
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
    /// True while a map switch or a types operation (configure/remove/clean) is in
    /// flight. Mainly used by the Start Server action so it never launches the
    /// server while the launch batch / mission files are being rewritten.
    /// </summary>
    public bool IsBusy => _isSwitching || _typesBusy;

    private void NotifyBusyChanged() => OnPropertyChanged(nameof(IsBusy));

    /// <summary>
    /// Invoked before configuring types. Returns true when the current in-memory
    /// state is persisted (either already or just applied), false if Apply failed.
    /// </summary>
    public Func<Task<bool>>? EnsureApplied { get; set; }

    /// <summary>
    /// Invoked to replace the loaded-mod order with the given order and persist it
    /// (rewriting the batch file and mod_order.json). Returns true on success.
    /// Used to restore a progress save's load order before loading it.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<bool>>? RestoreModOrder { get; set; }

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
        _configuredBatchFile = settings.BatchFile;
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
        // server path there is nothing to switch to and no server file to write.
        if (string.IsNullOrWhiteSpace(_serverPath) || _discoveredMaps.Count == 0)
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

        string previousMap = _typesConfig.CurrentMap;

        if (!_serverConfig.UpdateTemplate(_serverPath, mapName))
        {
            _log.Error("Failed to update the server template (serverDZ.cfg); the map was not switched.");
            return false;
        }

        // The map profile folder mirrors the mpmissions mission folder exactly
        // (e.g. map_profiles\dayzOffline.chernarusplus) so profiles and logs line
        // up 1:1 with the mission the tool runs.
        if (!_batchFile.WriteServerProfile(Path.Combine(_serverPath, _configuredBatchFile), $"map_profiles\\{mapName}"))
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

        if (!_batchFile.WriteServerProfile(Path.Combine(_serverPath, _configuredBatchFile), $"map_profiles\\{previousMap}"))
        {
            _log.Warning("Failed to restore the previous batch serverProfile after the map switch was aborted.");
        }
    }

    private async void ConfigureMod()
    {
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

        // A valid save always carries a configuration snapshot; without one the
        // stored world data cannot be located, so refuse before any confirmation.
        ConfigLoadResult<SaveMetaData> stored;
        try
        {
            stored = _saveGameService.GetMeta(dataDirectory, mapName, saveName);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to read the save's configuration snapshot: {ex.Message}");
            return;
        }

        if (stored.Status != ConfigLoadStatus.Success)
        {
            string invalidMessage = "This save's configuration snapshot is missing or unreadable, so the save cannot be loaded.";
            _log.Error(invalidMessage);
            _dialogs.ShowMessage(invalidMessage, "Load Save", isError: true);
            return;
        }

        // A save made by this version stores its type configuration alongside the
        // physical ModTypes snapshot. Without that configuration the files cannot
        // be restored consistently, so legacy saves are warned about explicitly.
        bool snapshotExists = _fileSystem.DirectoryExists(
            _saveGameService.GetModTypesSnapshotPath(dataDirectory, mapName, saveName));
        bool legacyTypes = stored.Value!.Types is null;
        bool offerTypesRestore = snapshotExists && !legacyTypes;

        LoadCompatibilityReport report = BuildLoadCompatibilityReport(mapName, stored.Value!);
        if (snapshotExists && legacyTypes)
        {
            string legacy = "This save predates types snapshots; its ModTypes files cannot be restored automatically.";
            report = report with
            {
                Message = string.IsNullOrWhiteSpace(report.Message) ? legacy : report.Message + "\n" + legacy,
            };
        }

        string message =
            $"Load save \"{saveName}\" for map {mapName}?\n\nThis will REPLACE the current progress in {StorageLabel(mapName)} with the stored copy. The current progress will be lost.";

        if (report.OrderWillChange)
        {
            message += "\n\nThe current mod order differs and will be automatically updated to match this save.";
        }

        if (report.ExtraMods.Count > 0)
        {
            message += "\n\nMod(s) loaded now but not in this save:\n";
            message += string.Join(", ", report.ExtraMods);
            message += "\n\nThe above extra mods will be appended to the end of this save's meta.json mod list. They will persist and load automatically for this save going forward.";
        }

        string note = BuildLoadSaveNote(dataDirectory, mapName, saveName, snapshotExists, legacyTypes);
        bool confirmed;
        bool restoreTypes = false;

        if (offerTypesRestore)
        {
            LoadSaveConfirmation confirmation =
                _dialogs.ConfirmLoadSave(message, "Load Save", report.Message, note, offerTypesRestore: true);
            confirmed = confirmation.Confirmed;
            restoreTypes = confirmation.RestoreTypes;
        }
        else if (!report.HasWarning)
        {
            confirmed = _dialogs.Confirm(message, "Load Save");
        }
        else
        {
            confirmed = _dialogs.ConfirmWithWarning(message, "Load Save", report.Message, note);
        }

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
            if (report.OrderWillChange)
            {
                bool restored = RestoreModOrder is not null && await RestoreModOrder(report.RestoredOrder);
                if (!restored)
                {
                    string failure = "Failed to update the mod order to match the save. The load was cancelled.";
                    _log.Error(failure);
                    _dialogs.ShowMessage(failure, "Load Save", isError: true);
                    return;
                }

                // The apply's UI refresh clears the selection; restore it so the
                // load below still targets the chosen save.
                SelectedSave = saveName;
            }

            SaveGameResult result = await Task.Run(() => _saveGameService.LoadSave(serverPath, mapName, dataDirectory, saveName));
            LogSaveResult(result);

            // Restore the types files only after the world data is in place. A
            // failure here is reported but does not undo the loaded world.
            if (result.Success && restoreTypes)
            {
                await RestoreTypesFromSave(serverPath, mapName, dataDirectory, saveName, stored.Value!);
            }

            // Record the accepted extra mods in the save's snapshot so they are not
            // flagged again on the next load.
            if (result.Success && report.ExtraMods.Count > 0)
            {
                stored.Value!.ModList = stored.Value!.ModList.Concat(report.ExtraMods).ToList();
                SaveGameResult meta = _saveGameService.UpdateMeta(dataDirectory, mapName, saveName, stored.Value!);
                if (meta.Success)
                {
                    _log.Info(meta.Message);
                }
                else
                {
                    _log.Warning(meta.Message);
                }
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
    /// Replaces the live <c>db\ModTypes</c> with a save's snapshot and reconciles
    /// the manager's type configuration and cfgeconomycore.xml. Runs after the
    /// world data has loaded, so a failure is reported without undoing the load.
    /// </summary>
    private async Task RestoreTypesFromSave(
        string serverPath, string mapName, string dataDirectory, string saveName, SaveMetaData saved)
    {
        try
        {
            SaveGameResult result = await Task.Run(
                () => _saveGameService.RestoreModTypes(serverPath, mapName, dataDirectory, saveName));
            if (!result.Success)
            {
                _log.Error($"Loaded the world, but failed to restore types files: {result.Message}");
                return;
            }

            if (saved.Types is null)
            {
                _log.Warning(
                    "Restored the types files, but this save has no type configuration snapshot, so they are shown as untracked until reconfigured.");
            }
            else
            {
                _typesConfig.Maps[mapName] = CloneMapConfig(saved.Types);
                _typesConfigStore.Save(dataDirectory, _typesConfig);

                string? missionPath = ResolveAppliedMapPath();
                if (missionPath is null || !_typesService.SyncEconomyCore(_typesConfig, mapName, missionPath, LoadedSet()))
                {
                    _log.Warning("Restored the types files, but failed to update cfgeconomycore.xml.");
                }
            }

            _log.Success(result.Message);
            RebuildRows();
        }
        catch (Exception ex)
        {
            _log.Error($"Loaded the world, but failed to restore types files: {ex.Message}");
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
            Types = CurrentMapConfig() is { } map ? CloneMapConfig(map) : null,
        };
    }

    /// <summary>
    /// Deep-copies a map's types configuration so a stored snapshot cannot be
    /// mutated by later edits to the live configuration (or vice versa).
    /// </summary>
    private static MapTypesConfig CloneMapConfig(MapTypesConfig source) =>
        new()
        {
            Mods = source.Mods
                .Select(entry => new ModTypesEntry
                {
                    ModName = entry.ModName,
                    SourceFiles = new List<string>(entry.SourceFiles),
                    GeneratedFiles = new List<string>(entry.GeneratedFiles),
                })
                .ToList(),
        };

    /// <summary>
    /// Describes how a stored save's configuration snapshot differs from the
    /// current setup, for the Load Save confirmation. <see cref="Message"/> is the
    /// prominent warning text (missing mods / type-file differences); the flags let
    /// a tailored "note" and the automatic mod-order restore be chosen below.
    /// </summary>
    private LoadCompatibilityReport BuildLoadCompatibilityReport(string mapName, SaveMetaData saved)
    {
        SaveMetaData current = BuildSaveSnapshot(mapName);

        var lines = new List<string>();
        List<string> missing = saved.ModList
            .Where(mod => !current.ModList.Contains(mod, StringComparer.OrdinalIgnoreCase))
            .ToList();
        List<string> extra = current.ModList
            .Where(mod => !saved.ModList.Contains(mod, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count > 0)
        {
            lines.Add($"This save was created with a different mod setup ({saved.ModList.Count} mod(s) then vs {current.ModList.Count} now).");
            lines.Add("In save but not loaded now: " + string.Join(", ", missing));
        }

        // With no missing mods the save's order can be restored automatically: the
        // save's mods first, then any extra loaded mods appended in their current
        // relative order.
        IReadOnlyList<string> restoredOrder = Array.Empty<string>();
        bool orderWillChange = false;
        if (missing.Count == 0)
        {
            restoredOrder = saved.ModList.Concat(extra).ToList();
            orderWillChange = !restoredOrder.SequenceEqual(current.ModList, StringComparer.OrdinalIgnoreCase);
        }

        var savedTypes = saved.TypesFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentTypes = current.TypesFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> removed = saved.TypesFiles.Where(file => !currentTypes.Contains(file)).ToList();

        if (!savedTypes.SetEquals(currentTypes))
        {
            List<string> added = current.TypesFiles.Where(file => !savedTypes.Contains(file)).ToList();

            lines.Add($"This save was created with different active type files ({saved.TypesFiles.Count} then vs {current.TypesFiles.Count} now).");
            if (removed.Count > 0)
            {
                lines.Add("Type file(s) in save but not active now: " + string.Join(", ", removed));
            }

            if (added.Count > 0)
            {
                lines.Add("Active type file(s) now but not in save: " + string.Join(", ", added));
            }
        }

        return new LoadCompatibilityReport(string.Join("\n", lines), orderWillChange, restoredOrder, extra);
    }

    /// <summary>
    /// Builds the "note" line shown under the Load Save message. Saves made by this
    /// version describe the automatic types restore; legacy saves (no configuration
    /// snapshot) are told their ModTypes cannot be restored automatically and how
    /// to do it by hand.
    /// </summary>
    private string BuildLoadSaveNote(string dataDirectory, string mapName, string saveName, bool snapshotExists, bool legacyTypes)
    {
        if (!snapshotExists)
        {
            return string.Empty;
        }

        string snapshot = _saveGameService.GetModTypesSnapshotPath(dataDirectory, mapName, saveName);
        if (!legacyTypes)
        {
            return "Tick \"Also restore the saved types files\" to replace the map's db\\ModTypes with the files saved at this time.";
        }

        string note = "ModTypes cannot be restored automatically for this old-format save.";
        note += "\n\nTo restore them manually, copy the files from:\n" + snapshot;
        string liveModTypes = LiveModTypesFolderPath(mapName);
        if (!string.IsNullOrWhiteSpace(liveModTypes))
        {
            note += "\n\ninto the map's db\\ModTypes folder:\n" + liveModTypes;
        }

        return note;
    }

    /// <summary>Returns the live mission's <c>db\ModTypes</c> folder for a map, or empty when it cannot be resolved.</summary>
    private string LiveModTypesFolderPath(string mapName)
    {
        try
        {
            string liveStorage = _saveGameService.GetStorageFolderPath(_serverPath, mapName);
            string? mission = Path.GetDirectoryName(liveStorage);
            return string.IsNullOrWhiteSpace(mission) ? string.Empty : Path.Combine(mission, "db", "ModTypes");
        }
        catch (Exception)
        {
            return string.Empty;
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

    /// <summary>
    /// Result of comparing a stored save's snapshot with the current setup.
    /// <see cref="OrderWillChange"/> is true when the save's load order can be
    /// restored automatically (no missing mods) and differs from the current order;
    /// <see cref="RestoredOrder"/> is that target order and <see cref="ExtraMods"/>
    /// are the loaded mods not in the save (kept after the restored order).
    /// </summary>
    private sealed record LoadCompatibilityReport(
        string Message,
        bool OrderWillChange,
        IReadOnlyList<string> RestoredOrder,
        IReadOnlyList<string> ExtraMods)
    {
        public bool HasWarning => !string.IsNullOrWhiteSpace(Message);
    }

    private static bool ContainsIgnoreCase(ObservableCollection<string> items, string value) =>
        items.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
}
