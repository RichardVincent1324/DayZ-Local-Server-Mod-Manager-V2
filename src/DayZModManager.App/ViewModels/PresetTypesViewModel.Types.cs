using System.Collections.ObjectModel;
using System.IO;
using DayZModManager.Core;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.App.Services;

namespace DayZModManager.App.ViewModels;

public sealed partial class PresetTypesViewModel : ViewModelBase
{
    /// <summary>
    /// Warning shown before a types edit when the preset already holds world data:
    /// the change only affects future spawns, not entities already in the world.
    /// </summary>
    private const string SaveWarningText =
        "\u26A0\uFE0F WARNING: This preset has existing saves. Types edits will NOT modify items already spawned in the world. Changes only affect future spawns and newly created saves.";

    /// <summary>
    /// Returns the save warning when the active preset already has stored saves or
    /// a live world storage folder; otherwise null.
    /// </summary>
    private string? SaveWarning()
    {
        string? mapName = ActiveMap;
        return PresetHasWorldData(mapName) ? SaveWarningText : null;
    }

    /// <summary>
    /// True when the active preset has at least one stored save or a live world
    /// storage folder. Never throws.
    /// </summary>
    private bool PresetHasWorldData(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(_serverPath) || string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        try
        {
            if (_saveGameService.ListSaves(PresetSavesFolder(mapName)).Count > 0)
            {
                return true;
            }

            return _fileSystem.DirectoryExists(
                _saveGameService.GetStorageFolderPath(_serverPath, mapName, PresetInstanceId(mapName)));
        }
        catch (Exception)
        {
            return false;
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

    /// <summary>Sets <see cref="SelectedMod"/> without triggering the import action.</summary>
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
    /// Runs the import flow for a mod chosen from the Types Config dropdown and
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
    /// Reloads the active preset's <c>type_files.json</c> into memory. Called when
    /// the active preset changes (or on initial setup), never for a map-only change.
    /// </summary>
    public void ReloadTypeFilesConfig()
    {
        _assignments.Clear();
        TypeFilesConfigCorrupt = false;

        if (string.IsNullOrWhiteSpace(ActiveMap))
        {
            RebuildRows();
            return;
        }

        string presetFolder = PresetFolder(ActiveMap);
        ConfigLoadResult<Dictionary<string, string>> loaded = _typeFilesConfigStore.Load(presetFolder);
        if (loaded.Status == ConfigLoadStatus.Success && loaded.Value is not null)
        {
            foreach ((string fileName, string role) in loaded.Value)
            {
                _assignments[fileName] = role;
            }
        }
        else if (loaded.Status == ConfigLoadStatus.Corrupt)
        {
            TypeFilesConfigCorrupt = true;
            bool backedUp = _typeFilesConfigStore.BackupCorrupt(presetFolder);
            string path = PresetPaths.TypeFilesConfigPath(
                _dataDirectoryProvider.Current, ActiveMap, _activePresetName);
            _log.Error(
                $"type_files.json is corrupt ({path}). " +
                (backedUp ? $"A backup was saved as \"{path}.corrupt\". " : string.Empty) +
                "The file was not overwritten and cfgeconomycore.xml was not updated. " +
                "Fix or delete the corrupt file, then re-select the preset.");

            if (_reportedCorruptTypes.Add(path))
            {
                _dialogs.ShowWarning(
                    "The preset's type-file configuration could not be read and was left untouched.",
                    "Type files configuration corrupt",
                    "The file was not overwritten and cfgeconomycore.xml was not updated. " +
                    "Fix or delete the corrupt type_files.json, then re-select the preset.",
                    path);
            }
        }

        RebuildRows();
    }

    /// <summary>
    /// Regenerates the manager-owned (marked) type_files block in cfgeconomycore.xml
    /// from a fresh scan of the active preset's <c>type_files</c> folder and the
    /// current role assignments. The block always points at the stable mission-local
    /// <c>db/DZMMV2_Types</c> junction, which Apply re-targets to the active preset.
    /// </summary>
    public bool SyncEconomyCore()
    {
        if (TypeFilesConfigCorrupt)
        {
            _log.Warning("cfgeconomycore.xml was not updated because type_files.json is corrupt.");
            return false;
        }

        string? mapName = ActiveMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        string? missionPath = ResolveAppliedMapPath();
        if (missionPath is null)
        {
            return false;
        }

        TypesOperationResult result = _typesService.SyncEconomyCore(
            missionPath,
            EconomyCoreService.ManagedFolder,
            PresetTypeFilesFolder(mapName),
            _assignments);

        foreach (string message in result.Messages)
        {
            _log.Warning(message);
        }

        ReportUnmanagedV2CeDetection(result.UnmanagedV2CeBlockDetected, missionPath);

        if (!result.Success)
        {
            _log.Warning("Failed to update cfgeconomycore.xml.");
        }

        return result.Success;
    }

    /// <summary>
    /// cfgeconomycore.xml files whose unmarked V2 CE block was already logged in
    /// this session.
    /// </summary>
    private readonly HashSet<string> _reportedUnmanagedV2CePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the session-scoped unmanaged V2 CE block alert was shown.</summary>
    private bool _unmanagedV2CeAlertShown;

    /// <summary>Title of the session-scoped unmanaged V2 CE block alert.</summary>
    private const string UnmanagedV2CeAlertTitle = "Unmanaged V2 CE block detected";

    /// <summary>Red-banner body of the session-scoped unmanaged V2 CE block alert.</summary>
    private const string UnmanagedV2CeAlertWarning =
        "DayZ loads all <ce> blocks simultaneously. Please manually remove the unmarked V2-related <ce> " +
        "from cfgeconomycore.xml to avoid duplicate type loading / server boot issues.";

    /// <summary>
    /// Logs the unified warning once per affected cfgeconomycore.xml per session,
    /// and raises the modal alert once per application session. A later detection
    /// after the block disappears warns again for that file.
    /// </summary>
    private void ReportUnmanagedV2CeDetection(bool detected, string missionPath)
    {
        string cfgXmlPath = Path.Combine(missionPath, "cfgeconomycore.xml");

        if (!detected)
        {
            _reportedUnmanagedV2CePaths.Remove(cfgXmlPath);
            return;
        }

        if (_reportedUnmanagedV2CePaths.Add(cfgXmlPath))
        {
            _log.Warning(EconomyCoreService.UnmanagedV2CeBlockWarning);
        }

        if (_unmanagedV2CeAlertShown)
        {
            return;
        }

        _unmanagedV2CeAlertShown = true;

        string alertMessage = "Detected an unmarked V2-related <ce> block in cfgeconomycore.xml. The manager will not modify it.";
        string alertNote = $"To edit and remove the old entry, open:\n{cfgXmlPath}";

        _dialogs.ShowAlert(
            alertMessage,
            UnmanagedV2CeAlertTitle,
            UnmanagedV2CeAlertWarning,
            alertNote);
    }

    /// <summary>
    /// Imports XML files from the selected mod into the active preset's
    /// <c>type_files</c> folder. The files are copied verbatim and remain
    /// unassigned until the user selects a role.
    /// </summary>
    /// <returns>
    /// True only when the user completed a successful import. Every other outcome
    /// (no mod, no XML files, cancelled, a failed apply, a failed copy) returns
    /// false so a caller can avoid acting on an import that did not happen.
    /// </returns>
    public async Task<bool> ImportTypesAsync(string? modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
        {
            _log.Warning("Select a mod to import first.");
            return false;
        }

        if (RefuseWhileServerRunning("importing types files"))
        {
            return false;
        }

        if (!TryBeginTypesOperation())
        {
            _log.Warning("Another types or map operation is already in progress; please retry.");
            return false;
        }

        try
        {
            string? mapName = ActiveMap;
            if (string.IsNullOrWhiteSpace(mapName))
            {
                _log.Warning("Apply a map first.");
                return false;
            }

            // Scanning a large mod walks its whole folder tree; keep it off the UI
            // thread so the window stays responsive while the button is clicked.
            IReadOnlyList<string> files =
                await Task.Run(() => _typesService.DiscoverXmlFiles(_workshopPath, modName));

            if (files.Count == 0)
            {
                string message = $"No XML files were found in {modName}.";
                _log.Warning(message);
                _dialogs.ShowMessage(message, "Configure types");
                return false;
            }

            _log.Info($"Found {files.Count} XML file(s) in {modName}.");

            string modFolderPath = Path.Combine(_workshopPath, modName);
            IReadOnlyList<string>? selected = _dialogs.PickTypeFiles(modName, files, modFolderPath);
            if (selected is null || selected.Count == 0)
            {
                return false;
            }

            string typeFilesFolder = PresetTypeFilesFolder(mapName);
            List<string> collisions = selected
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => _fileSystem.FileExists(Path.Combine(typeFilesFolder, name)))
                .OrderBy(name => name, NaturalStringComparer.Instance)
                .ToList();

            bool overwrite = false;
            if (collisions.Count > 0)
            {
                string list = string.Join("\n", collisions.Select(name => "  • " + name));
                
                string message = $"""
                    The following file(s) already exist in type_files:

                    {list}

                    Do you want to overwrite them?

                      • Yes: Overwrite the existing files.
                      • No: Skip these files and import the rest.
                    """;
                    
                overwrite = _dialogs.Confirm(message, "File Collision Detected");
            }

            if (!await EnsureAppliedBeforeAsync("types import"))
            {
                return false;
            }

            TypesOperationResult result = _typesService.ImportFiles(selected, typeFilesFolder, overwrite);
            LogMessages(result.Messages);

            if (!result.Success)
            {
                _log.Error("Import failed.");
                return false;
            }

            _log.Success($"Types files imported into {ImportTargetDescription(mapName)}.");
            RebuildRows();
            NotifyCommandStates();
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Types import failed: {ex.Message}");
            return false;
        }
        finally
        {
            EndTypesOperation();
            DrainPendingMapSwitch();
        }
    }

    /// <summary>
    /// Describes the preset an import wrote into, for the success log line.
    /// </summary>
    private string ImportTargetDescription(string mapName) => $"{mapName} / {_activePresetName}";

    /// <summary>
    /// Deletes the selected physical files from type_files, removes their role
    /// assignments, and regenerates the CE block.
    /// </summary>
    private void RemoveSelected()
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

            string? mapName = ActiveMap;
            if (string.IsNullOrWhiteSpace(mapName))
            {
                return;
            }

            string confirm = $"Delete the selected {rows.Count} type file(s) from type_files?";
            confirm += "\n\nThis permanently removes the file(s) from the preset.";
            string? saveWarning = SaveWarning();
            if (saveWarning is not null)
            {
                confirm = saveWarning + "\n\n" + confirm;
            }

            confirm += "\n\nContinue?";
            if (!_dialogs.Confirm(confirm, "Remove type files"))
            {
                _log.Info("Removal cancelled.");
                return;
            }

            string typeFilesFolder = PresetTypeFilesFolder(mapName);
            var removedAssignments = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (TypesRowViewModel row in rows)
            {
                removedAssignments[row.FileName] = _assignments.TryGetValue(row.FileName, out string? role)
                    ? role
                    : null;
                _assignments.Remove(row.FileName);
            }

            // Save the assignments first. If the save cannot verify the directory
            // (locked/missing), abort before any file is deleted.
            try
            {
                _typeFilesConfigStore.Save(PresetFolder(mapName), _assignments);
            }
            catch (Exception ex)
            {
                foreach ((string fileName, string? role) in removedAssignments)
                {
                    if (role is not null)
                    {
                        _assignments[fileName] = role;
                    }
                }

                _log.Error($"Failed to save type_files.json: {ex.Message}");
                _dialogs.ShowMessage(
                    $"Failed to save the file-type assignments: {ex.Message}",
                    "Remove type files",
                    isError: true);
                return;
            }

            TypeFilesConfigCorrupt = false;

            foreach (TypesRowViewModel row in rows)
            {
                string path = Path.Combine(typeFilesFolder, row.FileName);
                try
                {
                    if (_fileSystem.FileExists(path))
                    {
                        _fileSystem.DeleteFile(path);
                        _log.Info($"Deleted {row.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning($"Failed to delete {row.FileName}: {ex.Message}");
                }
            }

            if (SyncEconomyCore())
            {
                _log.Success("Removed selected types files.");
            }
            else
            {
                ShowCeSyncFailure(
                    "The files were removed and saved, but cfgeconomycore.xml could not be updated. " +
                    "Click Apply to repair the CE block.");
            }

            RebuildRows();
            NotifyCommandStates();
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

    private void LogMessages(IEnumerable<string> messages)
    {
        foreach (string message in messages)
        {
            _log.Info(message);
        }
    }

    /// <summary>
    /// Rebuilds the grid from the filesystem. Assignments are not reloaded here;
    /// they change only when the active preset changes or the user edits a role.
    /// </summary>
    private void RebuildRows()
    {
        TypesRows.Clear();

        string? mapName = ActiveMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return;
        }

        try
        {
            string folder = PresetTypeFilesFolder(mapName);
            IReadOnlyList<string> files = _fileSystem.GetFiles(folder, "*.xml", recursive: false);
            foreach (string name in files
                         .Select(Path.GetFileName)
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Select(name => name!)
                         .OrderBy(name => name, NaturalStringComparer.Instance))
            {
                string? role = _assignments.TryGetValue(name, out string? stored) ? stored : null;
                TypesRows.Add(new TypesRowViewModel(name, role, TryRenameFile, TrySetFileType));
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to read the type_files folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Persists a File Type selection immediately. Returns false (and restores the
    /// previous value) when the save fails.
    /// </summary>
    private bool TrySetFileType(TypesRowViewModel row, string? previous, string? selected)
    {
        string? mapName = ActiveMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        if (string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (selected is null)
        {
            _assignments.Remove(row.FileName);
        }
        else
        {
            _assignments[row.FileName] = selected;
        }

        try
        {
            _typeFilesConfigStore.Save(PresetFolder(mapName), _assignments);
        }
        catch (Exception ex)
        {
            if (previous is null)
            {
                _assignments.Remove(row.FileName);
            }
            else
            {
                _assignments[row.FileName] = previous;
            }

            _log.Error($"Failed to save type_files.json: {ex.Message}");
            _dialogs.ShowMessage(
                $"Failed to save the file type: {ex.Message}",
                "File type",
                isError: true);
            return false;
        }

        TypeFilesConfigCorrupt = false;

        if (!SyncEconomyCore())
        {
            ShowCeSyncFailure(
                "The change was saved, but cfgeconomycore.xml could not be updated. " +
                "Click Apply to repair the CE block.");
            return true;
        }

        _log.Info(
            $"Assigned {row.FileName} to {selected ?? PresetTypesViewModel.UnassignedFileTypeLabel}, CE synced.");
        return true;
    }

    /// <summary>
    /// Renames a file in type_files on edit commit, moving its role assignment to
    /// the new filename. Returns false (and keeps the old name) on any failure.
    /// </summary>
    private bool TryRenameFile(TypesRowViewModel row, string oldName, string newName)
    {
        string? mapName = ActiveMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        newName = newName.Trim();
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IsValidFileName(newName, out string? error))
        {
            ShowRenameError(error!);
            return false;
        }

        string folder = PresetTypeFilesFolder(mapName);
        string source = Path.Combine(folder, oldName);
        string destination = Path.Combine(folder, newName);

        if (!_fileSystem.FileExists(source))
        {
            ShowRenameError($"The file \"{oldName}\" no longer exists in type_files.");
            return false;
        }

        if (_fileSystem.FileExists(destination))
        {
            ShowRenameError($"A file named \"{newName}\" already exists in type_files.");
            return false;
        }

        try
        {
            _fileSystem.MoveFile(source, destination);
        }
        catch (Exception ex)
        {
            ShowRenameError($"Failed to rename \"{oldName}\" to \"{newName}\": {ex.Message}");
            return false;
        }

        bool hadAssignment = _assignments.TryGetValue(oldName, out string? role);
        if (hadAssignment)
        {
            _assignments.Remove(oldName);
            _assignments[newName] = role!;
        }

        try
        {
            _typeFilesConfigStore.Save(PresetFolder(mapName), _assignments);
        }
        catch (Exception ex)
        {
            try
            {
                _fileSystem.MoveFile(destination, source);
            }
            catch (Exception)
            {
                // Best effort rollback; the original error below is reported.
            }

            if (hadAssignment)
            {
                _assignments.Remove(newName);
                _assignments[oldName] = role!;
            }

            ShowRenameError($"Failed to save the renamed file's type assignment: {ex.Message}");
            return false;
        }

        TypeFilesConfigCorrupt = false;

        if (!SyncEconomyCore())
        {
            ShowCeSyncFailure(
                "The file was renamed and saved, but cfgeconomycore.xml could not be updated. " +
                "Click Apply to repair the CE block.");
        }

        _ = RebuildRowsDeferredAsync();
        return true;
    }

    private static bool IsValidFileName(string name, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "The file name cannot be empty.";
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal)
            || name.Contains('\\')
            || name.Contains('/')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "The file name contains invalid characters or path separators.";
            return false;
        }

        if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            error = "Type files must keep the .xml extension.";
            return false;
        }

        return true;
    }

    private async Task RebuildRowsDeferredAsync()
    {
        await Task.Yield();
        RebuildRows();
    }

    private void ShowRenameError(string message)
    {
        _log.Error(message);
        _dialogs.ShowMessage(message, "Rename type file", isError: true);
    }

    /// <summary>
    /// Reports a CE sync failure after the authoritative type_files.json write
    /// succeeded. The saved change is kept; Apply repairs the derived CE block.
    /// </summary>
    private void ShowCeSyncFailure(string message)
    {
        _log.Error(message);
        _dialogs.ShowMessage(message, "cfgeconomycore.xml", isError: true);
    }

    /// <summary>
    /// Opens the active preset's <c>type_files</c> folder in File Explorer. The
    /// folder is created when missing; failures are logged, never thrown.
    /// </summary>
    private void OpenTypeFilesFolder()
    {
        string? mapName = ActiveMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return;
        }

        string folder = PresetTypeFilesFolder(mapName);
        try
        {
            _fileSystem.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open the type_files folder: {ex.Message}");
            return;
        }

        try
        {
            _processLauncher.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open the type_files folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the launch-blocking problems with the active preset's type files:
    /// a corrupt assignment file, a CE block that does not match the fresh
    /// filesystem/assignment state, or a mission-local junction that is missing or
    /// points at another preset.
    /// </summary>
    public IReadOnlyList<string> PreflightTypesIssues()
    {
        string? mapName = ActiveMap;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return Array.Empty<string>();
        }

        if (TypeFilesConfigCorrupt)
        {
            return new[] { "type_files.json is corrupt for the active preset. Fix or delete it before starting the server." };
        }

        var messages = new List<string>();
        string typeFilesFolder = PresetTypeFilesFolder(mapName);

        try
        {
            string? missionPath = ResolveAppliedMapPath();
            if (missionPath is not null)
            {
                IReadOnlyList<ManagedTypeFileEntry> expected =
                    _typesService.GetActiveTypeFiles(typeFilesFolder, _assignments);
                IReadOnlyList<ManagedTypeFileEntry> actual = _typesService.GetManagedTypeFiles(missionPath);
                string expectedFolder = EconomyCoreService.ManagedFolder;
                string? actualFolder = _typesService.GetActiveTypesFolder(missionPath);

                if ((expected.Count > 0 || actual.Count > 0)
                    && (!string.Equals(actualFolder, expectedFolder, StringComparison.OrdinalIgnoreCase)
                        || !TypeFileEntriesMatch(expected, actual)))
                {
                    messages.Add(
                        "cfgeconomycore.xml is not synchronized with the active preset's types. " +
                        "Re-apply the configuration to restore it.");
                }
            }
        }
        catch (Exception ex)
        {
            messages.Add($"The preset's type_files folder could not be read: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(_serverPath))
        {
            string missionPath = MissionTypeFiles.MissionPath(_serverPath, mapName);
            if (!_typeFileJunctions.Verify(missionPath, typeFilesFolder))
            {
                messages.Add(
                    $"The managed type files junction ({MissionTypeFiles.CeFolder}) is missing or does not point at the active preset. " +
                    "Re-apply the configuration before starting the server.");
            }
        }

        return messages;
    }

    /// <summary>
    /// True when both managed-entry lists reference the same files, in the same
    /// order, with the same economy type.
    /// </summary>
    private static bool TypeFileEntriesMatch(
        IReadOnlyList<ManagedTypeFileEntry> expected, IReadOnlyList<ManagedTypeFileEntry> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(expected[i].Name, actual[i].Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(expected[i].Type, actual[i].Type, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
