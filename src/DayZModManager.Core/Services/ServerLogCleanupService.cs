using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a log-cleanup pass.</summary>
public sealed record ServerLogCleanupResult(bool FolderExists, int Removed);

/// <summary>
/// Clears DayZ server log files inside a preset's profile folder
/// (<c>Presets\&lt;map&gt;\&lt;preset&gt;\profiles</c>). All <c>.rpt</c> and <c>.log</c>
/// files are counted together and, when that combined total exceeds
/// <see cref="ServerLogCleanupService.CleanupThreshold"/>, every one of them is
/// deleted. Other files are never touched.
/// </summary>
public interface IServerLogCleanupService
{
    /// <summary>
    /// Deletes all <c>.rpt</c>/<c>.log</c> files in <paramref name="profileFolderPath"/>
    /// when there are more than <see cref="ServerLogCleanupService.CleanupThreshold"/>
    /// of them. Files that cannot be deleted (for example because the server is
    /// still writing them) are skipped; the method never throws.
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

    public ServerLogCleanupService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ServerLogCleanupResult Cleanup(string profileFolderPath)
    {
        if (string.IsNullOrWhiteSpace(profileFolderPath) || !_fileSystem.DirectoryExists(profileFolderPath))
        {
            return new ServerLogCleanupResult(FolderExists: false, Removed: 0);
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
                // The file may be locked by a running server; skip it.
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
