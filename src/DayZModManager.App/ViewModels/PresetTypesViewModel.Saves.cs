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
			$"This will overwrite the preset's current world in {StorageLabel(mapName)} with the stored world data. " +
			$"The preset's mod list, types configuration and profiles are left unchanged. " +
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
}

