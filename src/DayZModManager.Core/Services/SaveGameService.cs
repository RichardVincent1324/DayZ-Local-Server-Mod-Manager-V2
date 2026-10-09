using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a save-game operation.</summary>
public sealed record SaveGameResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// True when the operation succeeded but performed no work (e.g. wiping a
    /// world that has no storage folder to delete). Callers log these as neutral notices.
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

    /// <summary>
    /// Returns the instance IDs of the live <c>storage_&lt;id&gt;</c> folders present in
    /// the map's mission folder, ascending. Non-storage folders are excluded. Never throws.
    /// </summary>
    IReadOnlyList<int> ListStorageInstanceIds(string serverPath, string mapName);

    /// <summary>
    /// Deletes the live storage folder for the given instance ID. Used to clean up
    /// unattached (orphan) storage that no preset owns.
    /// </summary>
    SaveGameResult DeleteStorage(string serverPath, string mapName, int instanceId);

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

    /// <summary>Replaces the live storage folder with a stored save.</summary>
    SaveGameResult LoadSave(string serverPath, string mapName, string savesFolder, int instanceId, string saveName);

    /// <summary>Reads the metadata of a stored save. Missing when it has none.</summary>
    ConfigLoadResult<SaveMetaData> GetMeta(string savesFolder, string saveName);

    /// <summary>Wipes (deletes) the live storage folder so the map starts fresh on the next launch.</summary>
    SaveGameResult WipeWorld(string serverPath, string mapName, int instanceId);

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

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Matches a live storage folder leaf exactly, e.g. "storage_1".</summary>
    private static readonly Regex StorageFolderNameRegex = new(@"^storage_(\d+)$", RegexOptions.Compiled);

    private readonly IFileSystem _fileSystem;
    private readonly IDayZServerProcessState _processState;

    public SaveGameService(IFileSystem fileSystem, IDayZServerProcessState processState)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _processState = processState ?? throw new ArgumentNullException(nameof(processState));
    }

    public string GetStorageFolderPath(string serverPath, string mapName, int instanceId) =>
        Path.Combine(serverPath, "mpmissions", mapName, $"storage_{instanceId}");

    public IReadOnlyList<int> ListStorageInstanceIds(string serverPath, string mapName)
    {
        if (string.IsNullOrWhiteSpace(serverPath) || string.IsNullOrWhiteSpace(mapName))
        {
            return Array.Empty<int>();
        }

        string missionPath = Path.Combine(serverPath, "mpmissions", mapName);
        if (!_fileSystem.DirectoryExists(missionPath))
        {
            return Array.Empty<int>();
        }

        var ids = new List<int>();
        try
        {
            foreach (string name in _fileSystem.GetDirectories(missionPath))
            {
                Match match = StorageFolderNameRegex.Match(name);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                {
                    ids.Add(id);
                }
            }
        }
        catch (Exception)
        {
            return Array.Empty<int>();
        }

        ids.Sort();
        return ids;
    }

    public SaveGameResult DeleteStorage(string serverPath, string mapName, int instanceId)
    {
        if (_processState.IsDayZServerRunning())
        {
            return Failure("The DayZ server is running. Stop it before deleting storage so it is not corrupted.");
        }

        string liveStorage = GetStorageFolderPath(serverPath, mapName, instanceId);
        if (!_fileSystem.DirectoryExists(liveStorage))
        {
            return Notice($"No storage folder found at {liveStorage}.");
        }

        try
        {
            _fileSystem.DeleteDirectory(liveStorage, recursive: true);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to delete storage \"{Path.GetFileName(liveStorage)}\": {ex.Message}");
        }

        return Success($"Deleted unattached storage \"{Path.GetFileName(liveStorage)}\".");
    }

    public IReadOnlyList<string> ListSaves(string savesFolder)
    {
        if (string.IsNullOrWhiteSpace(savesFolder) || !_fileSystem.DirectoryExists(savesFolder))
        {
            return Array.Empty<string>();
        }

        return _fileSystem
            .GetDirectories(savesFolder)
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

        // Capture the original creation time before the target is replaced.
        DateTime createdAt = ResolveCreatedAt(target, overwrite);
        try
        {
            if (_fileSystem.DirectoryExists(target))
            {
                _fileSystem.DeleteDirectory(target, recursive: true);
            }

            _fileSystem.CreateDirectory(savesFolder);
            // Store the storage folder nested so save-meta.json can sit beside it
            // without mixing into the world data.
            _fileSystem.CopyDirectory(liveStorage, Path.Combine(target, Path.GetFileName(liveStorage)));

            var meta = new SaveMetaData
            {
                SaveName = name,
                CreatedAtUtc = createdAt,
                SavedAtUtc = DateTime.UtcNow,
                StorageFolder = Path.GetFileName(liveStorage),
            };
            ConfigJson.Write(_fileSystem, Path.Combine(target, MetaFileName), meta);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to save progress: {ex.Message}");
        }

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
            return Failure($"Save \"{name}\" was not found.");
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
        try
        {
            if (_fileSystem.DirectoryExists(liveStorage))
            {
                _fileSystem.DeleteDirectory(liveStorage, recursive: true);
            }

            _fileSystem.CopyDirectory(sourceRoot, liveStorage);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to load save \"{name}\": {ex.Message}");
        }

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

    public SaveGameResult WipeWorld(string serverPath, string mapName, int instanceId)
    {
        if (_processState.IsDayZServerRunning())
        {
            return Failure("The DayZ server is running. Stop it before wiping the world so the storage folder is not corrupted.");
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
            return Failure($"Failed to wipe the world: {ex.Message}");
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

    private static SaveGameResult Success(string message) => new() { Success = true, Message = message };

    private static SaveGameResult Notice(string message) => new() { Success = true, Informational = true, Message = message };

    private static SaveGameResult Failure(string message) => new() { Success = false, Message = message };
}
