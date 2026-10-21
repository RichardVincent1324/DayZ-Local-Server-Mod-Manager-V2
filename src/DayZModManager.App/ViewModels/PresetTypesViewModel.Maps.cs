using System.Collections.ObjectModel;
using System.IO;
using DayZModManager.Core;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.App.Services;

namespace DayZModManager.App.ViewModels;

public sealed partial class PresetTypesViewModel : ViewModelBase
{
    /// <summary>Corrupt types files already reported, so the modal warning is shown once per file.</summary>
    private readonly HashSet<string> _reportedCorruptTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Called at startup: retains the last-applied map (validating it is still
    /// present on the server) and, for a first-time user whose server is already
    /// configured, applies the first discovered map so the server is provisioned
    /// for it (mission template and batch serverProfile) before its first launch.
    /// Does not rewrite server files for an already-applied map, and never
    /// auto-applies while there is no real mission folder on a configured server.
    /// Returns the map it auto-applied, or null when it applied nothing, so the
    /// shell can provision the mod list and junctions for a freshly applied map.
    /// </summary>
    public string? ReconcileAppliedMap()
    {
        if (MapNames.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(_typesConfig.CurrentMap))
        {
            if (!_discoveredMaps.Any(m => string.Equals(m.Name, _typesConfig.CurrentMap, StringComparison.OrdinalIgnoreCase)))
            {
                _log.Warning($"Previously applied map \"{_typesConfig.CurrentMap}\" was not found on this server. Select a valid map.");
            }

            return null;
        }

        // Nothing has been applied yet. Auto-apply a default map only when a real
        // mission folder exists on a configured server; before the user sets the
        // server path or selects a launch batch file there is nothing to switch to
        // and no server file to write.
        if (string.IsNullOrWhiteSpace(_serverPath)
            || string.IsNullOrWhiteSpace(_batFileName)
            || _discoveredMaps.Count == 0)
        {
            return null;
        }

        string defaultMap = _discoveredMaps[0].Name;
        ServerEnvironment? environment = PrepareMapSwitch(defaultMap);
        if (environment is null)
        {
            // Refused (server running) or the server files could not be written;
            // leave the current map empty so a later reconcile retries.
            return null;
        }

        if (!CommitAppliedMap(defaultMap, environment.PresetName, environment))
        {
            return null;
        }

        SetRestoringSelection(defaultMap);
        return defaultMap;
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

        // Add maps that have preset folders but are no longer discovered, so the
        // user can see and clean up stale configurations.
        string presetsRoot = PresetPaths.PresetsRoot(_dataDirectoryProvider.Current);
        foreach (string key in _fileSystem.GetDirectories(presetsRoot).OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!ContainsIgnoreCase(MapNames, key))
            {
                MapNames.Add(key);
            }
        }

        // Keep the applied map selectable even when its mission folder disappeared,
        // so the dropdown never silently jumps to a different map.
        if (!string.IsNullOrEmpty(_typesConfig.CurrentMap) && !ContainsIgnoreCase(MapNames, _typesConfig.CurrentMap))
        {
            MapNames.Add(_typesConfig.CurrentMap);
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

        if (_isSwitching || _typesBusy || _isSwitchingPreset)
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

        // Defer past the Current Map ComboBox's selection event. A synchronous
        // reverting write to SelectedMap is ignored by WPF while the ComboBox is
        // still processing the change, leaving the dropdown stuck on a map whose
        // switch failed.
        await Task.Yield();

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

            // Prepare the target map/preset (resolve environment, write the mission
            // template) without committing anything. Only after the environment apply
            // succeeds is the switch committed, so a failed switch leaves the active
            // map, persisted selection and launch batch untouched.
            ServerEnvironment? environment = PrepareMapSwitch(mapName);
            if (environment is null)
            {
                // Refused (server running) or the server files could not be
                // updated; keep the dropdown on the actually-applied map.
                SetRestoringSelection(_typesConfig.CurrentMap);
                return;
            }

            // A successful map switch selects that map's active preset, so load its
            // mod order/types and apply its environment. Without this the in-memory
            // mod list would still belong to the previous preset, and the next Apply
            // would persist it into the wrong preset's mod_order.json.
            if (ActivatePreset is not null && !await ActivatePreset(mapName, environment.PresetName))
            {
                _log.Error($"Map switched to {mapName}, but its preset environment could not be applied.");
                SetRestoringSelection(_typesConfig.CurrentMap);
                return;
            }

            if (!CommitAppliedMap(mapName, environment.PresetName, environment))
            {
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
        if (_typesBusy || _isSwitching || _isSwitchingPreset)
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
        if (_typesBusy || _isSwitching || _isSwitchingPreset)
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
    /// Prepares a map switch: resolves the target map's active preset and writes
    /// that preset's mission template, but commits nothing (no CurrentMap, no types
    /// save, no launch batch, no persisted selection). Returns the resolved
    /// environment, or null when the switch was refused (server running, no batch
    /// file) or the server files could not be updated. The caller runs the
    /// environment apply and then <see cref="CommitAppliedMap"/>.
    /// </summary>
    private ServerEnvironment? PrepareMapSwitch(string mapName)
    {
        if (RefuseWhileServerRunning("switching maps"))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_batFileName))
        {
            _log.Error("No launch batch file is selected. Choose one in the Settings tab before switching maps.");
            return null;
        }

        string dataDirectory = _dataDirectoryProvider.Current;
        var settings = new Settings { ServerPath = _serverPath, BatFileName = _batFileName };

        // Switching maps uses that map's active preset (persisted from a previous
        // session), falling back to the reserved default preset. The default
        // preset is always (re)created so the map always has a working environment.
        PresetResult ensured = _presetService.EnsureDefaultPreset(_serverPath, dataDirectory, mapName);
        if (!ensured.Success)
        {
            _log.Error(ensured.Message);
            return null;
        }

        foreach (string warning in ensured.Warnings)
        {
            _log.Warning(warning);
        }

        string requestedPreset = ResolvePresetForMap?.Invoke(mapName) ?? PresetPaths.DefaultPresetName;
        EnvironmentResolution resolution = _presetService.ResolveEnvironment(
            settings, dataDirectory, mapName, requestedPreset);
        if (resolution.Environment is null)
        {
            // The persisted preset is unavailable; fall back to the default preset,
            // which was just ensured above.
            resolution = _presetService.ResolveEnvironment(
                settings, dataDirectory, mapName, PresetPaths.DefaultPresetName);
            if (resolution.Environment is null)
            {
                _log.Error(resolution.Error ?? $"No usable preset is available for {mapName}.");
                return null;
            }
        }

        foreach (string warning in resolution.Warnings)
        {
            _log.Warning(warning);
        }

        ServerEnvironment environment = resolution.Environment;
        string presetConfig = Path.Combine(environment.PresetFolder, ConfigFileNames.ServerConfig);

        if (!_serverConfig.UpdateTemplate(presetConfig, mapName))
        {
            _log.Error($"Failed to set the mission template in {presetConfig}; the map was not switched.");
            return null;
        }

        return environment;
    }

    /// <summary>
    /// Commits a prepared map switch after the environment apply has succeeded:
    /// makes the map/preset active, merges and persists its types configuration,
    /// creates its profiles folder, points the launch batch at it (idempotent when
    /// an Apply already did), refreshes the UI and persists the selection.
    /// </summary>
    private bool CommitAppliedMap(string mapName, string presetName, ServerEnvironment environment)
    {
        // Point the launcher at the environment even when no separate Apply ran
        // (e.g. the startup auto-apply). Idempotent when an Apply already wrote it.
        // A non-conforming batch is rejected before any state is committed.
        var settings = new Settings { ServerPath = _serverPath, BatFileName = _batFileName };
        LaunchWriteResult location = _launchEnvironment.WriteServerLocation(settings, environment);
        if (!location.Success)
        {
            foreach (string message in location.Messages)
            {
                _log.Error(message);
            }

            _log.Error($"Could not point the launch batch at preset \"{presetName}\"; the map was not switched.");
            return false;
        }

        foreach (string message in location.Messages)
        {
            _log.Info(message);
        }

        _activePresetName = presetName;

        // Load the target preset's own (single-map) types configuration so a map
        // switch shows that preset's settings rather than carrying the previous
        // map's config over.
        bool typesUsable = MergePresetTypesConfig(mapName, presetName);
        _typesConfig.CurrentMap = mapName;
        if (typesUsable)
        {
            try
            {
                _typesConfigStore.Save(PresetFolder(mapName), _typesConfig);
            }
            catch (Exception ex)
            {
                // The environment is already applied; a failed types save is reported
                // but does not tear it down.
                _log.Error($"Failed to persist the applied map's types configuration: {ex.Message}");
            }
        }

        // Pre-create the preset profiles folder the batch serverProfile points at.
        // DayZ also creates it on its first boot; a failure is non-fatal.
        try
        {
            _fileSystem.CreateDirectory(PresetPaths.ProfilesFolder(
                _dataDirectoryProvider.Current, mapName, presetName));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to create the preset profiles directory: {ex.Message}");
        }

        NotifyCommandStates();
        RebuildRows();
        if (typesUsable)
        {
            if (!SyncEconomyCore())
            {
                _log.Error(
                    "Map switched, but cfgeconomycore.xml could not be synchronized. " +
                    "Start Server is blocked until the configuration is re-applied successfully.");
            }
        }
        else
        {
            _log.Warning("cfgeconomycore.xml was not updated because types_config.json is corrupt.");
        }

        RefreshPresets();
        RefreshSaves();
        _log.Success($"Map switched to: {mapName}");

        // Let the shell persist the now-active map/preset so the next startup can
        // locate this preset's mod order and types configuration.
        PersistActiveSelection?.Invoke(mapName, presetName);
        return true;
    }

    /// <summary>
    /// Loads the target preset's types configuration into the shared config.
    /// Returns false when the file is corrupt: the in-memory mods are cleared but
    /// the file is preserved (and backed up) so an automatic save cannot replace
    /// it with an empty config.
    /// </summary>
    private bool MergePresetTypesConfig(string mapName, string presetName)
    {
        string presetFolder = PresetPaths.PresetFolder(_dataDirectoryProvider.Current, mapName, presetName);
        ConfigLoadResult<TypesConfig> loaded = _typesConfigStore.Load(presetFolder);
        if (loaded.Status == ConfigLoadStatus.Success && loaded.Value is not null)
        {
            _typesConfig.Mods = loaded.Value.Mods.ToList();
            _typesConfig.IsCorrupt = false;
            return true;
        }

        // No usable config for this preset: this map starts with an empty one.
        _typesConfig.Mods.Clear();
        _typesConfig.CurrentMap = mapName;
        _typesConfig.IsCorrupt = loaded.Status == ConfigLoadStatus.Corrupt;
        if (!_typesConfig.IsCorrupt)
        {
            return true;
        }

        string path = PresetPaths.TypesConfigPath(_dataDirectoryProvider.Current, mapName, presetName);
        bool backedUp = _typesConfigStore.BackupCorrupt(presetFolder);
        _log.Error(
            $"types_config.json is corrupt ({path}). " +
            (backedUp ? $"A backup was saved as \"{path}.corrupt\". " : string.Empty) +
            "The file was not overwritten and cfgeconomycore.xml was not updated. " +
            "Fix or delete the corrupt file, then re-select the map.");

        if (_reportedCorruptTypes.Add(path))
        {
            _dialogs.ShowWarning(
                "The preset's types configuration could not be read and was left untouched.",
                "Types configuration corrupt",
                "The file was not overwritten and cfgeconomycore.xml was not updated. " +
                "Fix or delete the corrupt types_config.json, then re-select the map.",
                path);
        }

        return false;
    }
}

