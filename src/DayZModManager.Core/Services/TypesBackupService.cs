using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a configured-types backup operation.</summary>
public sealed record TypesBackupResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    /// <summary>The mapping recovered by <see cref="ITypesBackupService.Restore"/>.</summary>
    public MapTypesConfig? Config { get; init; }
}

/// <summary>
/// Preserves the user's configured types (the files in the mission's
/// <c>db\ModTypes</c> folder plus their <c>types_config.json</c> mapping) in a
/// per-map folder under the data directory. While a world is active the live
/// folder holds the loaded save's types instead, so this backup is what a later
/// New Game restores.
/// </summary>
public interface ITypesBackupService
{
    /// <summary>Returns the per-map backup folder, e.g. <c>&lt;dataDir&gt;\ModTypes_Backup\&lt;map&gt;</c>.</summary>
    string GetBackupFolder(string dataDirectory, string mapName);

    /// <summary>
    /// Captures the live types folder and mapping only when no backup exists yet,
    /// so an existing configured backup is never overwritten by a loaded world.
    /// </summary>
    TypesBackupResult EnsureCaptured(string serverPath, string mapName, string dataDirectory, MapTypesConfig? configured);

    /// <summary>Overwrites the backup with the live types folder and mapping.</summary>
    TypesBackupResult Capture(string serverPath, string mapName, string dataDirectory, MapTypesConfig configured);

    /// <summary>
    /// Replaces the live types folder with the backup's files and returns its
    /// mapping. Fails without touching the live folder when the backup is missing
    /// or its mapping cannot be read.
    /// </summary>
    TypesBackupResult Restore(string serverPath, string mapName, string dataDirectory);
}

public sealed class TypesBackupService : ITypesBackupService
{
    private const string BackupRootName = "ModTypes_Backup";
    private const string FilesFolderName = "ModTypes";
    private const string ConfigFileName = "config.json";

    private readonly IFileSystem _fileSystem;

    public TypesBackupService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public string GetBackupFolder(string dataDirectory, string mapName) =>
        Path.Combine(dataDirectory, BackupRootName, mapName);

    public TypesBackupResult EnsureCaptured(
        string serverPath, string mapName, string dataDirectory, MapTypesConfig? configured)
    {
        string backupFolder = GetBackupFolder(dataDirectory, mapName);
        string filesFolder = Path.Combine(backupFolder, FilesFolderName);
        string configFile = Path.Combine(backupFolder, ConfigFileName);

        // Only a complete backup (files + readable mapping) may be kept. An
        // interrupted Capture can leave the folder behind without its files or
        // mapping; treating that as valid would skip capturing the configured
        // types and let a later Load Save destroy them.
        bool complete = _fileSystem.DirectoryExists(backupFolder)
            && _fileSystem.DirectoryExists(filesFolder)
            && _fileSystem.FileExists(configFile);

        if (complete)
        {
            return new TypesBackupResult { Success = true, Messages = new[] { "Existing configured types backup kept." } };
        }

        return Capture(serverPath, mapName, dataDirectory, configured ?? new MapTypesConfig());
    }

    public TypesBackupResult Capture(
        string serverPath, string mapName, string dataDirectory, MapTypesConfig configured)
    {
        string backupFolder = GetBackupFolder(dataDirectory, mapName);
        string filesFolder = Path.Combine(backupFolder, FilesFolderName);
        string configFile = Path.Combine(backupFolder, ConfigFileName);
        string liveTypes = LiveTypesFolder(serverPath, mapName);

        _fileSystem.CreateDirectory(backupFolder);
        DirectorySwap.CleanStaleStaging(_fileSystem, filesFolder);
        if (!DirectorySwap.TryReplace(_fileSystem, filesFolder, liveTypes, out string error))
        {
            return new TypesBackupResult
            {
                Success = false,
                Messages = new[] { $"Failed to back up the configured types files: {error}" },
            };
        }

        try
        {
            ConfigJson.Write(_fileSystem, configFile, configured ?? new MapTypesConfig());
        }
        catch (Exception ex)
        {
            return new TypesBackupResult
            {
                Success = false,
                Messages = new[] { $"Failed to back up the types mapping: {ex.Message}" },
            };
        }

        return new TypesBackupResult
        {
            Success = true,
            Messages = new[] { "Backed up the configured types files and mapping." },
            Config = configured,
        };
    }

    public TypesBackupResult Restore(string serverPath, string mapName, string dataDirectory)
    {
        string backupFolder = GetBackupFolder(dataDirectory, mapName);
        if (!_fileSystem.DirectoryExists(backupFolder))
        {
            return new TypesBackupResult
            {
                Success = false,
                Messages = new[] { "No configured types backup was found; the types files were left unchanged." },
            };
        }

        ConfigLoadResult<MapTypesConfig> stored =
            ConfigJson.Read<MapTypesConfig>(_fileSystem, Path.Combine(backupFolder, ConfigFileName));
        if (stored.Status != ConfigLoadStatus.Success || stored.Value is null)
        {
            return new TypesBackupResult
            {
                Success = false,
                Messages = new[] { "The configured types backup is unreadable; the types files were left unchanged." },
            };
        }

        string filesFolder = Path.Combine(backupFolder, FilesFolderName);
        if (!_fileSystem.DirectoryExists(filesFolder))
        {
            return new TypesBackupResult
            {
                Success = false,
                Messages = new[] { "The configured types backup is incomplete (its files are missing); the types files were left unchanged." },
            };
        }

        string liveTypes = LiveTypesFolder(serverPath, mapName);
        DirectorySwap.CleanStaleStaging(_fileSystem, liveTypes);
        if (!DirectorySwap.TryReplace(_fileSystem, liveTypes, filesFolder, out string error))
        {
            return new TypesBackupResult
            {
                Success = false,
                Messages = new[] { $"Failed to restore the configured types files: {error}" },
            };
        }

        return new TypesBackupResult
        {
            Success = true,
            Messages = new[] { "Restored the configured types files and mapping." },
            Config = stored.Value,
        };
    }

    private static string LiveTypesFolder(string serverPath, string mapName) =>
        Path.Combine(serverPath, "mpmissions", mapName, "db", "ModTypes");
}
