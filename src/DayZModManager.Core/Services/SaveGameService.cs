using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a save-game operation.</summary>
public sealed record SaveGameResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Non-fatal issues encountered while an operation still succeeded. Callers log these.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when the operation succeeded but performed no work (e.g. New Game
    /// with no storage folder to delete). Callers log these as neutral notices.
    /// </summary>
    public bool Informational { get; init; }
}

/// <summary>
/// Manages a preset's world-progress saves. The live world lives in
/// <c>mpmissions\&lt;mapName&gt;\storage_&lt;instanceId&gt;</c> (instanceId from the
/// preset's metadata); each stored save lives under
/// <c>&lt;presetFolder&gt;\saves\&lt;saveName&gt;</c> and holds a nested copy of the
/// storage folder plus a <c>save-meta.json</c> identity. A save is only world
/// state: the mod list, types configuration, ModTypes and instance ID belong to
/// the parent preset.
/// </summary>
public interface ISaveGameService
{
    /// <summary>
    /// Returns the live storage folder path for a map and instance ID (whether or
    /// not it exists), e.g. <c>mpmissions\dayzOffline.chernarusplus\storage_1</c>.
    /// </summary>
    string GetStorageFolderPath(string serverPath, string mapName, int instanceId);

    /// <summary>Returns the names of a preset's stored saves, newest first.</summary>
    IReadOnlyList<string> ListSaves(string savesFolder);

    /// <summary>Returns the full folder path of a stored save.</summary>
    string GetSaveFolderPath(string savesFolder, string saveName);

    /// <summary>
    /// Copies the live storage folder into the preset's save library (nested under
    /// the save folder) and writes <c>save-meta.json</c> beside it.
    /// </summary>
    SaveGameResult AddSave(
        string serverPath, string mapName, string savesFolder, int instanceId, string saveName, bool overwrite);

    /// <summary>
    /// Replaces the live storage folder with a stored save. The replacement is
    /// staged to a temporary folder first so the current progress is not lost if
    /// the copy fails.
    /// </summary>
    SaveGameResult LoadSave(string serverPath, string mapName, string savesFolder, int instanceId, string saveName);

    /// <summary>Reads the metadata of a stored save. Missing when it has none.</summary>
    ConfigLoadResult<SaveMetaData> GetMeta(string savesFolder, string saveName);

    /// <summary>Deletes the live storage folder so the map starts fresh.</summary>
    SaveGameResult NewGame(string serverPath, string mapName, int instanceId);

    /// <summary>Deletes a stored save from the preset's save library.</summary>
    SaveGameResult DeleteSave(string savesFolder, string saveName);

    /// <summary>
    /// Renames a stored save (moving its folder) and updates its
    /// <c>save-meta.json</c>. Fails when the source is missing or the target
    /// already exists.
    /// </summary>
    SaveGameResult RenameSave(string savesFolder, string saveName, string newName);
}

public sealed class SaveGameService : ISaveGameService
{
    /// <summary>File name of the metadata stored inside each save folder.</summary>
    internal const string MetaFileName = ConfigFileNames.SaveMeta;

    private const string TemporarySuffix = ".restore";

    /// <summary>
    /// Suffix used for the previous copy of a save or of the live storage while an
    /// overwrite/load is promoted. Leftovers are internal bookkeeping and must
    /// never be surfaced as normal saves.
    /// </summary>
    internal const string OldBackupSuffix = ".old";

    /// <summary>Prefix for the hidden staging folder used while building a save.</summary>
    private const string StagingPrefix = ".save_";

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly IFileSystem _fileSystem;
    private readonly IDayZServerProcessState _processState;

    public SaveGameService(IFileSystem fileSystem, IDayZServerProcessState processState)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _processState = processState ?? throw new ArgumentNullException(nameof(processState));
    }

    public string GetStorageFolderPath(string serverPath, string mapName, int instanceId) =>
        Path.Combine(serverPath, "mpmissions", mapName, $"storage_{instanceId}");

    public IReadOnlyList<string> ListSaves(string savesFolder)
    {
        if (string.IsNullOrWhiteSpace(savesFolder) || !_fileSystem.DirectoryExists(savesFolder))
        {
            return Array.Empty<string>();
        }

        return _fileSystem
            .GetDirectories(savesFolder)
            // Promotion backups (e.g. "Alpha.old" left by an interrupted AddSave)
            // are internal bookkeeping, never loadable saves.
            .Where(name => !name.EndsWith(OldBackupSuffix, StringComparison.OrdinalIgnoreCase))
            .Where(name => !name.StartsWith(StagingPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => (Name: name, SavedAtUtc: TryGetSavedAtUtc(savesFolder, name)))
            .OrderByDescending(save => save.SavedAtUtc.HasValue)
            .ThenBy(save => save.SavedAtUtc.GetValueOrDefault())
            .ThenBy(save => save.Name, StringComparer.OrdinalIgnoreCase)
            .Select(save => save.Name)
            .ToList();
    }

    /// <summary>
    /// Returns the <c>savedAtUtc</c> recorded in a save's <c>save-meta.json</c>, or
    /// null when the save has no metadata, it is unreadable/corrupt, or it records
    /// no usable timestamp. Never throws so a single unreadable save cannot break
    /// listing the others.
    /// </summary>
    private DateTime? TryGetSavedAtUtc(string savesFolder, string saveName)
    {
        try
        {
            ConfigLoadResult<SaveMetaData> meta = GetMeta(savesFolder, saveName);
            if (meta.Status == ConfigLoadStatus.Success)
            {
                DateTime savedAt = meta.Value!.SavedAtUtc;
                if (savedAt != default)
                {
                    return savedAt;
                }
            }
        }
        catch (Exception)
        {
            // Fall back to name ordering for the unreadable save.
        }

        return null;
    }

    public string GetSaveFolderPath(string savesFolder, string saveName) =>
        Path.Combine(savesFolder, saveName);

    public SaveGameResult AddSave(
        string serverPath, string mapName, string savesFolder, int instanceId, string saveName, bool overwrite)
    {
        if (_processState.IsDayZServerRunning())
        {
            return Failure("The DayZ server is running. Stop it before saving progress so the storage folder is not corrupted.");
        }

        string? name = NormalizeSaveName(saveName);
        if (name is null)
        {
            return Failure("Save name cannot be empty or contain invalid characters.");
        }

        string liveStorage = GetStorageFolderPath(serverPath, mapName, instanceId);
        if (!_fileSystem.DirectoryExists(liveStorage))
        {
            return Failure($"No storage folder found at {liveStorage}. Start the server once before saving progress.");
        }

        string target = Path.Combine(savesFolder, name);
        if (_fileSystem.DirectoryExists(target) && !overwrite)
        {
            return Failure($"A save named \"{name}\" already exists.");
        }

        // Build the complete save in a hidden staging folder (outside the saves
        // library so it can never appear in the save list). The previous save is
        // only replaced once the new one is fully written, so a failure never
        // deletes an existing save or leaves a partial one behind.
        string stagingRoot = Path.Combine(Path.GetDirectoryName(savesFolder) ?? savesFolder, StagingPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            _fileSystem.CreateDirectory(savesFolder);
            _fileSystem.CreateDirectory(stagingRoot);
            // Store the storage folder nested so save-meta.json can sit beside it
            // without mixing into the world data.
            _fileSystem.CopyDirectory(liveStorage, Path.Combine(stagingRoot, Path.GetFileName(liveStorage)));

            var meta = new SaveMetaData
            {
                SaveName = name,
                CreatedAtUtc = ResolveCreatedAt(target, overwrite),
                SavedAtUtc = DateTime.UtcNow,
                StorageFolder = Path.GetFileName(liveStorage),
            };
            ConfigJson.Write(_fileSystem, Path.Combine(stagingRoot, MetaFileName), meta);
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(stagingRoot);
            return Failure($"Failed to save progress: {ex.Message}");
        }

        // Promote the staged save into place.
        string backup = target + OldBackupSuffix;
        try
        {
            if (_fileSystem.DirectoryExists(target))
            {
                TryDeleteDirectory(backup);
                _fileSystem.MoveDirectory(target, backup);
            }

            _fileSystem.MoveDirectory(stagingRoot, target);
        }
        catch (Exception ex)
        {
            // Roll the previous save back if promotion fails.
            if (!_fileSystem.DirectoryExists(target) && _fileSystem.DirectoryExists(backup))
            {
                TryMoveDirectory(backup, target);
            }

            TryDeleteDirectory(stagingRoot);
            return Failure($"Failed to finalize the save: {ex.Message}");
        }

        TryDeleteDirectory(backup);
        return Success($"Saved current progress as \"{name}\".");
    }

    /// <summary>Reuses the original creation time when overwriting an existing save.</summary>
    private DateTime ResolveCreatedAt(string target, bool overwrite)
    {
        if (overwrite)
        {
            try
            {
                ConfigLoadResult<SaveMetaData> existing =
                    ConfigJson.Read<SaveMetaData>(_fileSystem, Path.Combine(target, MetaFileName));
                if (existing.Status == ConfigLoadStatus.Success && existing.Value!.CreatedAtUtc != default)
                {
                    return existing.Value.CreatedAtUtc;
                }
            }
            catch (Exception)
            {
                // Fall through to now.
            }
        }

        return DateTime.UtcNow;
    }

    public SaveGameResult LoadSave(
        string serverPath, string mapName, string savesFolder, int instanceId, string saveName)
    {
        if (_processState.IsDayZServerRunning())
        {
            return Failure("The DayZ server is running. Stop it before loading a save so the current progress is not corrupted.");
        }

        string? name = NormalizeSaveName(saveName);
        if (name is null)
        {
            return Failure("Invalid save name.");
        }

        string saveFolder = Path.Combine(savesFolder, name);
        if (!_fileSystem.DirectoryExists(saveFolder))
        {
            // An interrupted AddSave overwrite leaves the previous copy as
            // "<name>.old" with no "<name>" folder. Restore it so the last fully
            // committed save stays loadable instead of the save being reported lost.
            string backupFolder = saveFolder + OldBackupSuffix;
            if (!_fileSystem.DirectoryExists(backupFolder))
            {
                return Failure($"Save \"{name}\" was not found.");
            }

            try
            {
                _fileSystem.MoveDirectory(backupFolder, saveFolder);
            }
            catch (Exception ex)
            {
                return Failure($"Save \"{name}\" is recovering from an interrupted overwrite, but its previous copy could not be restored: {ex.Message}");
            }
        }

        string missionPath = Path.Combine(serverPath, "mpmissions", mapName);
        if (!_fileSystem.DirectoryExists(missionPath))
        {
            return Failure($"Mission folder not found: {missionPath}");
        }

        string? sourceRoot = ResolveSavedContentRoot(saveFolder);
        if (sourceRoot is null)
        {
            return Failure($"Save \"{name}\" contains no storage data.");
        }

        string liveStorage = GetStorageFolderPath(serverPath, mapName, instanceId);
        CleanStaleRestoreFolders(liveStorage);

        string temp = liveStorage + TemporarySuffix + "_" + Guid.NewGuid().ToString("N");
        string backup = liveStorage + OldBackupSuffix;

        try
        {
            // Stage a fresh copy first. Until it succeeds, the live folder (and
            // any .old backup left by an earlier interrupted load) stays intact,
            // so a failed copy can never destroy the current progress.
            _fileSystem.CopyDirectory(sourceRoot, temp);

            if (_fileSystem.DirectoryExists(liveStorage))
            {
                TryDeleteDirectory(backup);
                _fileSystem.MoveDirectory(liveStorage, backup);
            }

            try
            {
                _fileSystem.MoveDirectory(temp, liveStorage);
            }
            catch
            {
                if (!_fileSystem.DirectoryExists(liveStorage) && _fileSystem.DirectoryExists(backup))
                {
                    _fileSystem.MoveDirectory(backup, liveStorage);
                }

                throw;
            }
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(temp);
            return Failure($"Failed to load save \"{name}\": {ex.Message}");
        }

        TryDeleteDirectory(backup);
        TryDeleteDirectory(liveStorage + TemporarySuffix);

        return Success($"Loaded save \"{name}\" into {Path.GetFileName(liveStorage)}.");
    }

    public ConfigLoadResult<SaveMetaData> GetMeta(string savesFolder, string saveName)
    {
        string metaPath = Path.Combine(savesFolder, saveName, MetaFileName);
        return ConfigJson.Read<SaveMetaData>(_fileSystem, metaPath);
    }

    /// <summary>
    /// Resolves the folder whose contents represent a stored save's world data:
    /// the nested storage folder named by the save's <c>save-meta.json</c>. When
    /// that folder is missing (a stale or renamed name), the save's single child
    /// folder is accepted. Returns null when no storage data can be resolved, so
    /// the save is reported as corrupt rather than copying stray files into the
    /// live world.
    /// </summary>
    private string? ResolveSavedContentRoot(string saveFolder)
    {
        string metaPath = Path.Combine(saveFolder, MetaFileName);
        ConfigLoadResult<SaveMetaData> meta = ConfigJson.Read<SaveMetaData>(_fileSystem, metaPath);
        if (meta.Status == ConfigLoadStatus.Success && !string.IsNullOrWhiteSpace(meta.Value!.StorageFolder))
        {
            string nested = Path.Combine(saveFolder, meta.Value!.StorageFolder);
            if (_fileSystem.DirectoryExists(nested))
            {
                return nested;
            }
        }

        // The recorded storage folder is missing or renamed: accept a lone child
        // folder as the world data. save-meta.json itself does not count as a
        // direct file.
        IReadOnlyList<string> directFiles = _fileSystem.GetFiles(saveFolder, "*", recursive: false);
        bool hasNonMetaFiles = directFiles.Any(file =>
            !string.Equals(Path.GetFileName(file), MetaFileName, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<string> children = _fileSystem.GetDirectories(saveFolder);
        if (children.Count == 1
            && !hasNonMetaFiles
            && _fileSystem.DirectoryExists(Path.Combine(saveFolder, children[0])))
        {
            return Path.Combine(saveFolder, children[0]);
        }

        return null;
    }

    public SaveGameResult NewGame(string serverPath, string mapName, int instanceId)
    {
        if (_processState.IsDayZServerRunning())
        {
            return Failure("The DayZ server is running. Stop it before starting a new game so the storage folder is not corrupted.");
        }

        string liveStorage = GetStorageFolderPath(serverPath, mapName, instanceId);
        if (!_fileSystem.DirectoryExists(liveStorage))
        {
            return Notice("No existing storage folder found; nothing to delete.");
        }

        try
        {
            _fileSystem.DeleteDirectory(liveStorage, recursive: true);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to start a new game: {ex.Message}");
        }

        return Success($"Deleted {Path.GetFileName(liveStorage)}. The next server start will create a fresh world.");
    }

    public SaveGameResult DeleteSave(string savesFolder, string saveName)
    {
        string? name = NormalizeSaveName(saveName);
        if (name is null)
        {
            return Failure("Invalid save name.");
        }

        string target = Path.Combine(savesFolder, name);
        if (!_fileSystem.DirectoryExists(target))
        {
            return Failure($"Save \"{name}\" was not found.");
        }

        try
        {
            _fileSystem.DeleteDirectory(target, recursive: true);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to delete save \"{name}\": {ex.Message}");
        }

        return Success($"Deleted stored save \"{name}\".");
    }

    public SaveGameResult RenameSave(string savesFolder, string saveName, string newName)
    {
        string? name = NormalizeSaveName(saveName);
        string? target = NormalizeSaveName(newName);
        if (name is null || target is null)
        {
            return Failure("Save name cannot be empty or contain invalid characters.");
        }

        if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
        {
            return Success($"Save \"{name}\" already has that name.");
        }

        string source = Path.Combine(savesFolder, name);
        if (!_fileSystem.DirectoryExists(source))
        {
            return Failure($"Save \"{name}\" was not found.");
        }

        string destination = Path.Combine(savesFolder, target);
        if (_fileSystem.DirectoryExists(destination))
        {
            return Failure($"A save named \"{target}\" already exists.");
        }

        try
        {
            _fileSystem.MoveDirectory(source, destination);

            // Keep the recorded identity in sync with the folder name.
            string metaPath = Path.Combine(destination, MetaFileName);
            ConfigLoadResult<SaveMetaData> loaded = ConfigJson.Read<SaveMetaData>(_fileSystem, metaPath);
            if (loaded.Status == ConfigLoadStatus.Success && loaded.Value is not null)
            {
                loaded.Value.SaveName = target;
                ConfigJson.Write(_fileSystem, metaPath, loaded.Value);
            }
        }
        catch (Exception ex)
        {
            return Failure($"Failed to rename save \"{name}\": {ex.Message}");
        }

        return Success($"Renamed save \"{name}\" to \"{target}\".");
    }

    private static string? NormalizeSaveName(string? saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName))
        {
            return null;
        }

        string trimmed = saveName.Trim();
        if (trimmed.Length == 0
            || trimmed.Equals(".", StringComparison.Ordinal)
            || trimmed.Equals("..", StringComparison.Ordinal)
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.EndsWith('.')
            || ReservedDeviceNames.Contains(trimmed.Split('.')[0]))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>Deletes a folder, ignoring failures (used for best-effort cleanup).</summary>
    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (_fileSystem.DirectoryExists(path))
            {
                _fileSystem.DeleteDirectory(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort: a locked file must not break the surrounding operation.
        }
    }

    /// <summary>Moves a folder, ignoring failures (used for best-effort rollback).</summary>
    private void TryMoveDirectory(string source, string destination)
    {
        try
        {
            if (_fileSystem.DirectoryExists(source))
            {
                _fileSystem.MoveDirectory(source, destination);
            }
        }
        catch (Exception)
        {
            // Best effort: a failed rollback still leaves the data in the backup.
        }
    }

    /// <summary>
    /// Removes stale <c>storage_&lt;id&gt;.restore*</c> staging folders left behind
    /// in the mission folder by interrupted loads. Never throws.
    /// </summary>
    private void CleanStaleRestoreFolders(string liveStorage)
    {
        string? missionPath = Path.GetDirectoryName(liveStorage);
        string leaf = Path.GetFileName(liveStorage);
        if (string.IsNullOrWhiteSpace(missionPath) || string.IsNullOrWhiteSpace(leaf))
        {
            return;
        }

        string prefix = leaf + TemporarySuffix;
        try
        {
            if (!_fileSystem.DirectoryExists(missionPath))
            {
                return;
            }

            foreach (string name in _fileSystem.GetDirectories(missionPath))
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(Path.Combine(missionPath, name));
                }
            }
        }
        catch (Exception)
        {
            // Best effort cleanup must never fail the load.
        }
    }

    private static SaveGameResult Success(string message) => new() { Success = true, Message = message };

    private static SaveGameResult Notice(string message) => new() { Success = true, Informational = true, Message = message };

    private static SaveGameResult Failure(string message) => new() { Success = false, Message = message };
}
