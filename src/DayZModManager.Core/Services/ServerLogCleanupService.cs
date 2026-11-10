using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a log-cleanup pass.</summary>
public sealed record ServerLogCleanupResult(
    bool FolderExists,
    int Removed,
    bool SkippedWhileServerRunning = false);

/// <summary>
/// Clears DayZ server log files inside a preset's profile folder
/// (<c>Presets\&lt;map&gt;\&lt;preset&gt;\profiles</c>). All <c>.rpt</c> and <c>.log</c>
/// files are counted together and, when that combined total exceeds
/// <see cref="ServerLogCleanupService.CleanupThreshold"/>, every one of them is
/// deleted. Other files are never touched. The pass is skipped entirely while
/// the DayZ server is running because it may still be writing those files.
/// </summary>
public interface IServerLogCleanupService
{
    /// <summary>
    /// Deletes all <c>.rpt</c>/<c>.log</c> files in <paramref name="profileFolderPath"/>
    /// when there are more than <see cref="ServerLogCleanupService.CleanupThreshold"/>
    /// of them. When the DayZ server is running, nothing is deleted and
    /// <see cref="ServerLogCleanupResult.SkippedWhileServerRunning"/> is set. Any
    /// remaining file that cannot be deleted is skipped; the method never throws.
    /// </summary>
    ServerLogCleanupResult Cleanup(string profileFolderPath);
}

public sealed class ServerLogCleanupService : IServerLogCleanupService
{
    /// <summary>
    /// The combined number of <c>.rpt</c>/<c>.log</c> files is only cleared when it
    /// exceeds this; at or below the threshold no files are deleted.
    /// </summary>
    public const int CleanupThreshold = 20;

    private readonly IFileSystem _fileSystem;
    private readonly IDayZServerProcessState _processState;

    public ServerLogCleanupService(IFileSystem fileSystem, IDayZServerProcessState processState)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _processState = processState ?? throw new ArgumentNullException(nameof(processState));
    }

    public ServerLogCleanupResult Cleanup(string profileFolderPath)
    {
        if (string.IsNullOrWhiteSpace(profileFolderPath) || !_fileSystem.DirectoryExists(profileFolderPath))
        {
            return new ServerLogCleanupResult(FolderExists: false, Removed: 0);
        }

        if (_processState.IsDayZServerRunning())
        {
            return new ServerLogCleanupResult(
                FolderExists: true,
                Removed: 0,
                SkippedWhileServerRunning: true);
        }

        List<string> logs = _fileSystem
            .GetFiles(profileFolderPath, "*", recursive: false)
            .Where(IsLogFile)
            .ToList();

        if (logs.Count <= CleanupThreshold)
        {
            return new ServerLogCleanupResult(FolderExists: true, Removed: 0);
        }

        int removed = 0;
        foreach (string file in logs)
        {
            try
            {
                _fileSystem.DeleteFile(file);
                removed++;
            }
            catch (Exception)
            {
                // The file may be locked by another process; skip it.
            }
        }

        return new ServerLogCleanupResult(FolderExists: true, Removed: removed);
    }

    private static bool IsLogFile(string fullPath)
    {
        string extension = Path.GetExtension(fullPath);
        return extension.Equals(".rpt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".log", StringComparison.OrdinalIgnoreCase);
    }
}
