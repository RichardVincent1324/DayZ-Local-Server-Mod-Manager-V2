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
    /// <summary>Where the current types operation writes files and economy references.</summary>
    private TypesTarget ActiveTypesTarget(string missionPath) =>
        new(missionPath, PresetTypeFilesFolder(_typesConfig.CurrentMap), ActiveCeFolderValue(missionPath));

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
        string mapName = _typesConfig.CurrentMap;
        return PresetHasWorldData(mapName) ? SaveWarningText : null;
    }

    /// <summary>
    /// True when the active preset has at least one stored save or a live world
    /// storage folder. Never throws.
    /// </summary>
    private bool PresetHasWorldData(string mapName)
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
    /// Regenerates the manager-owned type_files block in cfgeconomycore.xml for the
    /// applied map. The block points at whichever types are active: the configured
    /// <c>type_files</c>, or the loaded save's own type_files folder.
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

        return PresetTypeFilesFolder(_typesConfig.CurrentMap);
    }

    /// <summary>
    /// The <c>folder</c> value for the manager-owned cfgeconomycore.xml block: the
    /// active preset's type_files folder relative to the mission (forward slashes).
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

    private async void ConfigureMod(string? modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
        {
            _log.Warning("Select a mod to configure first.");
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
            // in type_files; never do that silently, even when the same file is
            // selected again. When the preset already holds world data, the save
            // warning is stacked on top of the overwrite warning in one dialog.
            string? overwriteWarning = null;
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
                overwriteWarning = $"Mod {modName} already has configured type file(s). Reconfiguring will overwrite its current configuration in type_files with the newly selected file(s).";
                if (removed.Count > 0)
                {
                    string list = string.Join("\n", removed.Select(name => "  \u2022 " + name));
                    overwriteWarning += $"\n\nThe following currently configured file(s) will be deleted because they are no longer selected:\n{list}";
                }
            }

            string? saveWarning = SaveWarning();
            if (saveWarning is not null || overwriteWarning is not null)
            {
                string message = string.Join(
                    "\n\n",
                    new[] { saveWarning, overwriteWarning }.Where(part => !string.IsNullOrEmpty(part)));
                message += "\n\nContinue?";
                if (!_dialogs.Confirm(message, "Configure types files?"))
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

            string confirm = $"Delete the selected {rows.Count} type file(s) from type_files?";
            if (untrackedLeaves.Count > 0)
            {
                confirm += $"\n\n{untrackedLeaves.Count} of them are untracked (present in type_files but not managed by this app). Removing them also deletes their entries from cfgeconomycore.xml.";
            }

            confirm += "\n\nThis permanently removes the file(s) from the mission folder.";
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
                string message = $"The following mod(s) are no longer active (not loaded or missing from the workshop) and their generated type file(s) will be DELETED from type_files:\n\n{list}";
                string? saveWarning = SaveWarning();
                if (saveWarning is not null)
                {
                    message = saveWarning + "\n\n" + message;
                }

                bool clean = _dialogs.Confirm(message + "\n\nContinue?", "Clean invalid types configurations?");
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
    /// <c>type_files</c> folder but are not tracked by the types configuration,
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

    /// <summary>
    /// Opens the active preset's <c>type_files</c> folder in File Explorer. The
    /// folder is created when missing; failures are logged, never thrown.
    /// </summary>
    private void OpenTypeFilesFolder()
    {
        string folder = PresetTypeFilesFolder(_typesConfig.CurrentMap);
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
}

