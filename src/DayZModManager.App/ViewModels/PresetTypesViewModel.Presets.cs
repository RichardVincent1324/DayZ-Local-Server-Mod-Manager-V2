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
        if (string.IsNullOrWhiteSpace(mapName) || _isSwitchingPreset || _isSwitching || _typesBusy)
        {
            // A map switch or a types operation is in flight; keep the dropdown on
            // the actually-active preset so selection never diverges from it.
            RestorePresetSelection();
            return;
        }

        _isSwitchingPreset = true;
        NotifyBusyChanged();

        // Defer past the Preset ComboBox's selection event so a reverting write to
        // SelectedPreset is honored (see SwitchMapAsync for the same WPF pitfall).
        await Task.Yield();

        try
        {
            if (RefuseWhileServerRunning("switching presets"))
            {
                RestorePresetSelection();
                return;
            }

            string previousActive = _activePresetName;
            bool ok = ActivatePreset is not null && await ActivatePreset(mapName, presetName);
            if (!ok)
            {
                // Activation may have moved the active preset before its apply
                // failed; restore it so the dropdown (and later refreshes) show the
                // actually-active preset instead of the failed one.
                if (!string.Equals(_activePresetName, previousActive, StringComparison.Ordinal))
                {
                    ActivePresetName = previousActive;
                }

                RestorePresetSelection();
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
            DrainPendingMapSwitch();
        }
    }

    /// <summary>Restores the preset dropdown to the actually-active preset.</summary>
    private void RestorePresetSelection() =>
        SetRestoringPresetSelection(
            Presets.FirstOrDefault(p => string.Equals(p.Name, _activePresetName, StringComparison.Ordinal)));

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
    /// Creates a copy of the currently selected preset under a chosen destination
    /// map so the user can experiment with mods and types without touching the
    /// original. The copy always inherits the source's server config, mod order,
    /// types config and type_files; profiles are copied only when the user opts in.
    /// When the destination differs from the current map, the active map is switched
    /// to the destination so the copy can be activated.
    /// </summary>
    private async void DuplicatePreset()
    {
        string? sourceMap = AppliedMapOrWarn();
        PresetItemViewModel? source = SelectedPreset;
        if (sourceMap is null || source is null)
        {
            return;
        }

        DuplicatePresetRequest? request = _dialogs.AskDuplicatePreset(
            source.Name, MapNames.ToList(), _typesConfig.CurrentMap);
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return;
        }

        string targetMap = request.DestinationMap;
        if (string.IsNullOrWhiteSpace(targetMap) || !ContainsIgnoreCase(MapNames, targetMap))
        {
            _log.Error("The selected destination map is not available.");
            return;
        }

        PresetResult result = _presetService.DuplicatePreset(
            _serverPath, _dataDirectoryProvider.Current, sourceMap, source.Name,
            targetMap, request.Name, request.CopyProfiles);
        if (!result.Success)
        {
            _log.Error(result.Message);
            _dialogs.ShowMessage(result.Message, "Duplicate Preset", isError: true);
            return;
        }

        _log.Success(result.Message);

        // A cross-map copy is not in the current map's preset list; switch the active
        // map to the destination first so the copy can be selected and activated.
        if (!string.Equals(targetMap, sourceMap, StringComparison.Ordinal))
        {
            await SwitchMapAsync(targetMap);
            if (!string.Equals(_typesConfig.CurrentMap, targetMap, StringComparison.OrdinalIgnoreCase))
            {
                _log.Warning($"The copy was created for {targetMap}, but the active map could not be switched to it.");
                return;
            }
        }

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
            _dialogs.ShowMessage(result.Message, "Rename Preset", isError: true);
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
}

