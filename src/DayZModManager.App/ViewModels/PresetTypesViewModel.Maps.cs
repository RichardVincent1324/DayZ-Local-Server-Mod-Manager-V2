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
        // Load the target preset's own (single-map) types configuration so a map
        // switch shows that preset's settings rather than carrying the previous
        // map's config over.
        MergePresetTypesConfig(mapName, _activePresetName);
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

        // Let the shell persist the now-active map/preset so the next startup can
        // locate this preset's mod order and types configuration.
        PersistActiveSelection?.Invoke(mapName, _activePresetName);
        return true;
    }

    /// <summary>
    /// Loads the types configuration owned by a preset for a map and merges it
    /// into the shared config, replacing only that map's entry. A preset-scoped
    /// file never carries another map, so switching maps or presets cannot leak
    /// or drop other maps' settings.
    /// </summary>
    private void MergePresetTypesConfig(string mapName, string presetName)
    {
        string presetFolder = PresetPaths.PresetFolder(_dataDirectoryProvider.Current, mapName, presetName);
        ConfigLoadResult<TypesConfig> loaded = _typesConfigStore.Load(presetFolder);
        if (loaded.Status == ConfigLoadStatus.Success && loaded.Value is not null)
        {
            _typesConfig.MergeFrom(loaded.Value);
        }
        else
        {
            // No usable config for this preset: this map starts with an empty one.
            _typesConfig.Maps.Remove(mapName);
            _typesConfig.CurrentMap = mapName;
        }
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
}

