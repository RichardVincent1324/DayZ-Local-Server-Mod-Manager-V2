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

