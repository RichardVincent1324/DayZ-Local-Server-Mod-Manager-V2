using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a save-game operation.</summary>
public sealed record SaveGameResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Manages world-progress saves for a DayZ mission. The live data lives in
/// <c>mpmissions\&lt;mapName&gt;\storage_&lt;instanceId&gt;</c> (instanceId from
/// serverDZ.cfg); each stored save lives under
/// <c>&lt;dataDirectory&gt;\Progress_Saves\&lt;mapName&gt;\&lt;saveName&gt;</c>
/// and holds a nested copy of the storage folder, a snapshot of the mission's
/// <c>db\ModTypes</c> folder, and a <c>meta.json</c> configuration snapshot.
/// Saves created before meta.json existed (flat folder layout) are still loadable.
/// </summary>
public interface ISaveGameService
{
    /// <summary>Reads <c>instanceId</c> from serverDZ.cfg, defaulting to 1.</summary>
    int ReadInstanceId(string serverPath);

    /// <summary>
    /// Returns the live storage folder path for a map (whether or not it exists),
    /// e.g. <c>mpmissions\dayzOffline.chernarusplus\storage_1</c>.
    /// </summary>
    string GetStorageFolderPath(string serverPath, string mapName);

    /// <summary>Returns the names of saved progress folders for a map.</summary>
    IReadOnlyList<string> ListSaves(string dataDirectory, string mapName);

    /// <summary>
    /// Returns the full folder path of a stored save (where its storage folder and
    /// <c>meta.json</c> live).
    /// </summary>
    string GetSaveFolderPath(string dataDirectory, string mapName, string saveName);

    /// <summary>
    /// Returns the folder inside a stored save where the mission's <c>db\ModTypes</c>
    /// contents were snapshot at save time. The folder may not exist for saves that
    /// predate this feature or had no types configured.
    /// </summary>
    string GetModTypesSnapshotPath(string dataDirectory, string mapName, string saveName);

    /// <summary>
    /// Copies the live storage folder into the save library (nested under the
    /// save folder). When <paramref name="meta"/> is supplied it is written to
    /// <c>meta.json</c> next to the copied folder, and the mission's
    /// <c>db\ModTypes</c> folder is snapshot into the save as well.
    /// </summary>
    SaveGameResult AddSave(
        string serverPath, string mapName, string dataDirectory, string saveName, bool overwrite,
        SaveMetaData? meta = null);

    /// <summary>
    /// Replaces the live storage folder with a stored save. The replacement is
    /// staged to a temporary folder first so the current progress is not lost if
    /// the copy fails. Supports the current nested layout as well as the legacy
    /// flat layout used before configuration snapshots existed.
    /// </summary>
    SaveGameResult LoadSave(string serverPath, string mapName, string dataDirectory, string saveName);

    /// <summary>
    /// Reads the configuration snapshot of a stored save. Missing when the save
    /// has no <c>meta.json</c> (older-format save).
    /// </summary>
    ConfigLoadResult<SaveMetaData> GetMeta(string dataDirectory, string mapName, string saveName);

    /// <summary>Deletes the live storage folder so the map starts fresh.</summary>
    SaveGameResult NewGame(string serverPath, string mapName);

    /// <summary>Deletes a stored save from the library.</summary>
    SaveGameResult DeleteSave(string dataDirectory, string mapName, string saveName);
}

public sealed partial class SaveGameService : ISaveGameService
{
    /// <summary>Folder under the data directory that hosts the save library.</summary>
    internal const string SavesRootName = "Progress_Saves";

    /// <summary>File name of the configuration snapshot stored inside each save folder.</summary>
    internal const string MetaFileName = "meta.json";

    /// <summary>
    /// Folder inside each save folder that holds a snapshot of the mission's
    /// <c>db\ModTypes</c> contents taken at save time.
    /// </summary>
    internal const string ModTypesSnapshotFolderName = "ModTypes";

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

    public int ReadInstanceId(string serverPath)
    {
        string configPath = Path.Combine(serverPath, "serverDZ.cfg");
        if (string.IsNullOrWhiteSpace(serverPath) || !_fileSystem.FileExists(configPath))
        {
            return 1;
        }

        Match match = InstanceIdRegex().Match(_fileSystem.ReadAllText(configPath));
        return match.Success && int.TryParse(match.Groups[1].Value, out int id) ? id : 1;
    }

    public string GetStorageFolderPath(string serverPath, string mapName)
    {
        int instanceId = ReadInstanceId(serverPath);
        return Path.Combine(serverPath, "mpmissions", mapName, $"storage_{instanceId}");
    }

    public IReadOnlyList<string> ListSaves(string dataDirectory, string mapName)
    {
        string library = SavesLibrary(dataDirectory, mapName);
        if (!_fileSystem.DirectoryExists(library))
        {
            return Array.Empty<string>();
        }

        return _fileSystem
            .GetDirectories(library)
            // Promotion backups (e.g. "Alpha.old" left by an interrupted AddSave)
            // are internal bookkeeping, never loadable saves.
            .Where(name => !name.EndsWith(OldBackupSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(name => (Name: name, SavedAtUtc: TryGetSavedAtUtc(dataDirectory, mapName, name)))
            .OrderByDescending(save => save.SavedAtUtc.HasValue)
            .ThenBy(save => save.SavedAtUtc.GetValueOrDefault())
            .ThenBy(save => save.Name, StringComparer.OrdinalIgnoreCase)
            .Select(save => save.Name)
            .ToList();
    }

    /// <summary>
    /// Returns the <c>savedAtUtc</c> recorded in a save's <c>meta.json</c>, or
    /// null when the save has no meta.json (older-format save), its snapshot is
    /// unreadable/corrupt, or it records no usable timestamp. Never throws so a
    /// single unreadable save cannot break listing the others.
    /// </summary>
    private DateTime? TryGetSavedAtUtc(string dataDirectory, string mapName, string saveName)
    {
        try
        {
            ConfigLoadResult<SaveMetaData> meta = GetMeta(dataDirectory, mapName, saveName);
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

    public string GetSaveFolderPath(string dataDirectory, string mapName, string saveName) =>
        Path.Combine(SavesLibrary(dataDirectory, mapName), saveName);

    public string GetModTypesSnapshotPath(string dataDirectory, string mapName, string saveName) =>
        Path.Combine(GetSaveFolderPath(dataDirectory, mapName, saveName), ModTypesSnapshotFolderName);

    public SaveGameResult AddSave(
        string serverPath, string mapName, string dataDirectory, string saveName, bool overwrite,
        SaveMetaData? meta = null)
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

        string liveStorage = GetStorageFolderPath(serverPath, mapName);
        if (!_fileSystem.DirectoryExists(liveStorage))
        {
            return Failure($"No storage folder found at {liveStorage}. Start the server once before saving progress.");
        }

        // The live ModTypes folder sits beside the storage folder (under the
        // mission's db subfolder). It holds the generated type files the world's
        // economy loads, so a snapshot is taken alongside the world data.
        string liveModTypes = Path.Combine(Path.GetDirectoryName(liveStorage)!, "db", "ModTypes");

        string target = Path.Combine(SavesLibrary(dataDirectory, mapName), name);
        if (_fileSystem.DirectoryExists(target) && !overwrite)
        {
            return Failure($"A save named \"{name}\" already exists.");
        }

        // Build the complete save in a hidden staging folder (outside the per-map
        // library so it can never appear in the save list). The previous save is
        // only replaced once the new one is fully written, so a failure never
        // deletes an existing save or leaves a partial one behind.
        string staging = Path.Combine(SavesRootPath(dataDirectory), StagingPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            _fileSystem.CreateDirectory(Path.GetDirectoryName(target)!);
            _fileSystem.CreateDirectory(staging);
            // Store the storage folder nested so the snapshot meta.json can sit
            // beside it without mixing into the world data.
            _fileSystem.CopyDirectory(liveStorage, Path.Combine(staging, Path.GetFileName(liveStorage)));

            if (meta is not null && _fileSystem.DirectoryExists(liveModTypes))
            {
                _fileSystem.CopyDirectory(liveModTypes, Path.Combine(staging, ModTypesSnapshotFolderName));
            }
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(staging);
            return Failure($"Failed to save progress: {ex.Message}");
        }

        if (meta is not null)
        {
            meta.Map = string.IsNullOrWhiteSpace(meta.Map) ? mapName : meta.Map;
            meta.StorageFolder = Path.GetFileName(liveStorage);

            try
            {
                ConfigJson.Write(_fileSystem, Path.Combine(staging, MetaFileName), meta);
            }
            catch (Exception ex)
            {
                // Do not leave a save that cannot be verified against its config.
                TryDeleteDirectory(staging);
                return Failure($"Failed to save configuration snapshot: {ex.Message}");
            }
        }

        // Promote the staged save into place.
        string backup = target + OldBackupSuffix;
        try
        {
            if (_fileSystem.DirectoryExists(target))
            {
                // A stale backup from an earlier interrupted run is only removed
                // when the current save is about to take its place. When the target
                // is missing but a backup exists (interrupted overwrite), the backup
                // is kept until the new save has been promoted so a failure below
                // can still fall back to it.
                TryDeleteDirectory(backup);
                _fileSystem.MoveDirectory(target, backup);
            }

            _fileSystem.MoveDirectory(staging, target);
        }
        catch (Exception ex)
        {
            // Roll the previous save back if promotion fails.
            if (!_fileSystem.DirectoryExists(target) && _fileSystem.DirectoryExists(backup))
            {
                TryMoveDirectory(backup, target);
            }

            TryDeleteDirectory(staging);
            return Failure($"Failed to finalize the save: {ex.Message}");
        }

        TryDeleteDirectory(backup);

        if (meta is not null)
        {
            return Success($"Saved current progress as \"{name}\" ({meta.ModList.Count} mod(s), {meta.TypesFiles.Count} type file(s)).");
        }

        return Success($"Saved current progress as \"{name}\".");
    }

    public SaveGameResult LoadSave(string serverPath, string mapName, string dataDirectory, string saveName)
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

        string saveFolder = Path.Combine(SavesLibrary(dataDirectory, mapName), name);
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

        string liveStorage = GetStorageFolderPath(serverPath, mapName);
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
                // The live folder is intact here, so a stale backup from a
                // previous run may safely be removed before moving live aside.
                TryDeleteDirectory(backup);
                _fileSystem.MoveDirectory(liveStorage, backup);
            }

            try
            {
                _fileSystem.MoveDirectory(temp, liveStorage);
            }
            catch
            {
                // Roll the previous progress back if promotion fails.
                if (!_fileSystem.DirectoryExists(liveStorage) && _fileSystem.DirectoryExists(backup))
                {
                    _fileSystem.MoveDirectory(backup, liveStorage);
                }

                throw;
            }
        }
        catch (Exception ex)
        {
            // The load failed; remove the staging folder so it cannot linger.
            TryDeleteDirectory(temp);
            return Failure($"Failed to load save \"{name}\": {ex.Message}");
        }

        // Success: leftover folders are only removed best-effort (a locked file
        // must not turn a successful load into a reported failure).
        TryDeleteDirectory(backup);
        TryDeleteDirectory(liveStorage + TemporarySuffix);

        return Success($"Loaded save \"{name}\" into {Path.GetFileName(liveStorage)}.");
    }

    public ConfigLoadResult<SaveMetaData> GetMeta(string dataDirectory, string mapName, string saveName)
    {
        string metaPath = Path.Combine(SavesLibrary(dataDirectory, mapName), saveName, MetaFileName);
        return ConfigJson.Read<SaveMetaData>(_fileSystem, metaPath);
    }

    /// <summary>
    /// Resolves the folder whose contents represent a stored save's world data.
    /// Prefers the nested layout (the storage folder recorded in meta.json, or a
    /// lone sub-folder of a meta-less save). The legacy flat layout is only used
    /// for saves without meta.json; when a meta.json exists but no usable storage
    /// folder can be resolved, null is returned so the save is reported as corrupt
    /// rather than copying meta.json or stray files into the live world.
    /// </summary>
    private string? ResolveSavedContentRoot(string saveFolder)
    {
        string metaPath = Path.Combine(saveFolder, MetaFileName);
        bool hasMeta = _fileSystem.FileExists(metaPath);
        bool metaValid = false;
        if (hasMeta)
        {
            ConfigLoadResult<SaveMetaData> meta = ConfigJson.Read<SaveMetaData>(_fileSystem, metaPath);
            metaValid = meta.Status == ConfigLoadStatus.Success && !string.IsNullOrWhiteSpace(meta.Value!.StorageFolder);
            if (metaValid)
            {
                string nested = Path.Combine(saveFolder, meta.Value!.StorageFolder);
                if (_fileSystem.DirectoryExists(nested)
                    && !string.Equals(Path.GetFileName(nested), ModTypesSnapshotFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    return nested;
                }
            }
        }

        // A lone sub-folder with no direct world files is treated as nested
        // (covers meta-less and interrupted new-format saves). meta.json itself
        // does not count as a direct file. The ModTypes snapshot folder is never
        // a storage candidate: with the real storage folder missing it must not be
        // mistaken for the world data.
        IReadOnlyList<string> directFiles = _fileSystem.GetFiles(saveFolder, "*", recursive: false);
        bool hasNonMetaFiles = directFiles.Any(file =>
            !string.Equals(Path.GetFileName(file), MetaFileName, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<string> children = _fileSystem.GetDirectories(saveFolder)
            .Where(name => !string.Equals(name, ModTypesSnapshotFolderName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (children.Count == 1
            && !hasNonMetaFiles
            && _fileSystem.DirectoryExists(Path.Combine(saveFolder, children[0])))
        {
            return Path.Combine(saveFolder, children[0]);
        }

        // Legacy layout: the storage contents were copied directly into the save
        // folder. Only applies to saves without a snapshot.
        if (!hasMeta && hasNonMetaFiles)
        {
            return saveFolder;
        }

        // No clean storage data could be resolved.
        return null;
    }

    public SaveGameResult NewGame(string serverPath, string mapName)
    {
        if (_processState.IsDayZServerRunning())
        {
            return Failure("The DayZ server is running. Stop it before starting a new game so the storage folder is not corrupted.");
        }

        string liveStorage = GetStorageFolderPath(serverPath, mapName);
        if (!_fileSystem.DirectoryExists(liveStorage))
        {
            return Success("No existing storage folder found; nothing to delete.");
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

    public SaveGameResult DeleteSave(string dataDirectory, string mapName, string saveName)
    {
        string? name = NormalizeSaveName(saveName);
        if (name is null)
        {
            return Failure("Invalid save name.");
        }

        string target = Path.Combine(SavesLibrary(dataDirectory, mapName), name);
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

    private string SavesLibrary(string dataDirectory, string mapName) =>
        Path.Combine(SavesRootPath(dataDirectory), mapName);

    private string SavesRootPath(string dataDirectory) =>
        Path.Combine(dataDirectory, SavesRootName);

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

    private static SaveGameResult Failure(string message) => new() { Success = false, Message = message };

    [GeneratedRegex(@"^\s*instanceId\s*=\s*(\d+)\s*;?", RegexOptions.Multiline)]
    private static partial Regex InstanceIdRegex();
}
