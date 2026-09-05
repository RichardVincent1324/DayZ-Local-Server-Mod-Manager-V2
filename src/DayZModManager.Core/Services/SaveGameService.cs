using System.Text.RegularExpressions;
using DayZModManager.Core.Abstractions;

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
/// serverDZ.cfg); copies are stored under
/// <c>&lt;dataDirectory&gt;\Saves\&lt;mapName&gt;\&lt;saveName&gt;</c>.
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

    /// <summary>Copies the live storage folder into the save library.</summary>
    SaveGameResult AddSave(string serverPath, string mapName, string dataDirectory, string saveName, bool overwrite);

    /// <summary>
    /// Replaces the live storage folder with a stored save. The replacement is
    /// staged to a temporary folder first so the current progress is not lost if
    /// the copy fails.
    /// </summary>
    SaveGameResult LoadSave(string serverPath, string mapName, string dataDirectory, string saveName);

    /// <summary>Deletes the live storage folder so the map starts fresh.</summary>
    SaveGameResult NewGame(string serverPath, string mapName);

    /// <summary>Deletes a stored save from the library.</summary>
    SaveGameResult DeleteSave(string dataDirectory, string mapName, string saveName);
}

public sealed partial class SaveGameService : ISaveGameService
{
    /// <summary>Folder under the data directory that hosts the save library.</summary>
    internal const string SavesRootName = "Saves";

    private const string TemporarySuffix = ".restore";

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly IFileSystem _fileSystem;

    public SaveGameService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
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
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public SaveGameResult AddSave(string serverPath, string mapName, string dataDirectory, string saveName, bool overwrite)
    {
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

        string target = Path.Combine(SavesLibrary(dataDirectory, mapName), name);
        if (_fileSystem.DirectoryExists(target) && !overwrite)
        {
            return Failure($"A save named \"{name}\" already exists.");
        }

        try
        {
            _fileSystem.CreateDirectory(Path.GetDirectoryName(target)!);
            if (_fileSystem.DirectoryExists(target))
            {
                _fileSystem.DeleteDirectory(target, recursive: true);
            }

            _fileSystem.CopyDirectory(liveStorage, target);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to save progress: {ex.Message}");
        }

        return Success($"Saved current progress as \"{name}\".");
    }

    public SaveGameResult LoadSave(string serverPath, string mapName, string dataDirectory, string saveName)
    {
        string? name = NormalizeSaveName(saveName);
        if (name is null)
        {
            return Failure("Invalid save name.");
        }

        string source = Path.Combine(SavesLibrary(dataDirectory, mapName), name);
        if (!_fileSystem.DirectoryExists(source))
        {
            return Failure($"Save \"{name}\" was not found.");
        }

        string missionPath = Path.Combine(serverPath, "mpmissions", mapName);
        if (!_fileSystem.DirectoryExists(missionPath))
        {
            return Failure($"Mission folder not found: {missionPath}");
        }

        string liveStorage = GetStorageFolderPath(serverPath, mapName);
        string temp = liveStorage + TemporarySuffix + "_" + Guid.NewGuid().ToString("N");
        string backup = liveStorage + ".old";

        try
        {
            // Stage a fresh copy first. Until it succeeds, the live folder (and
            // any .old backup left by an earlier interrupted load) stays intact,
            // so a failed copy can never destroy the current progress.
            _fileSystem.CopyDirectory(source, temp);

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

    public SaveGameResult NewGame(string serverPath, string mapName)
    {
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
        Path.Combine(dataDirectory, SavesRootName, mapName);

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

    private static SaveGameResult Success(string message) => new() { Success = true, Message = message };

    private static SaveGameResult Failure(string message) => new() { Success = false, Message = message };

    [GeneratedRegex(@"^\s*instanceId\s*=\s*(\d+)\s*;?", RegexOptions.Multiline)]
    private static partial Regex InstanceIdRegex();
}
