using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a log-cleanup pass.</summary>
public sealed record ServerLogCleanupResult(bool FolderExists, int FilesRemoved);

/// <summary>
/// Wipes DayZ server log files inside a map profile folder
/// (<c>map_profiles\&lt;map&gt;</c>). Once both the <c>DayZServer_x64_*.RPT</c> and
/// <c>script_*.log</c> groups each hold at least
/// <see cref="ServerLogCleanupService.WipeThresholdPerType"/> files, all four log
/// groups — <c>DayZServer_x64_*.RPT</c>, <c>script_*.log</c>, <c>crash_*.log</c>
/// and <c>warning_*.log</c> — are deleted outright. Below that nothing is touched.
/// Everything else in the folder is untouched.
/// </summary>
public interface IServerLogCleanupService
{
    /// <summary>
    /// Deletes all DayZ log files when both the <c>DayZServer_x64_*.RPT</c> and
    /// <c>script_*.log</c> groups contain at least
    /// <see cref="ServerLogCleanupService.WipeThresholdPerType"/> files each; otherwise
    /// no files are deleted. Never throws when a file cannot be deleted (for example
    /// because the server is still writing it); such files are skipped and retried on
    /// the next run. Returns the number of files actually removed.
    /// </summary>
    ServerLogCleanupResult Cleanup(string profileFolderPath);
}

public sealed class ServerLogCleanupService : IServerLogCleanupService
{
    /// <summary>
    /// The wipe only runs when both the <c>DayZServer_x64_*.RPT</c> and the
    /// <c>script_*.log</c> groups each contain at least this many files. This keeps
    /// cleanup infrequent so an app open does not constantly churn the log folder.
    /// </summary>
    public const int WipeThresholdPerType = 10;

    private readonly IFileSystem _fileSystem;

    public ServerLogCleanupService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ServerLogCleanupResult Cleanup(string profileFolderPath)
    {
        if (string.IsNullOrWhiteSpace(profileFolderPath) || !_fileSystem.DirectoryExists(profileFolderPath))
        {
            return new ServerLogCleanupResult(FolderExists: false, FilesRemoved: 0);
        }

        IReadOnlyList<string> files = _fileSystem.GetFiles(profileFolderPath, "*", recursive: false);

        bool rptAtThreshold = CountGroup(files, "DayZServer_x64_", ".rpt") >= WipeThresholdPerType;
        bool scriptAtThreshold = CountGroup(files, "script_", ".log") >= WipeThresholdPerType;
        if (!rptAtThreshold || !scriptAtThreshold)
        {
            return new ServerLogCleanupResult(FolderExists: true, FilesRemoved: 0);
        }

        int removed = 0;
        foreach (string file in files)
        {
            if (!IsServerLogFile(file))
            {
                continue;
            }

            try
            {
                _fileSystem.DeleteFile(file);
                removed++;
            }
            catch (Exception)
            {
                // The file may be locked by a running server; skip it and retry later.
            }
        }

        return new ServerLogCleanupResult(FolderExists: true, removed);
    }

    private static int CountGroup(IReadOnlyList<string> files, string namePrefix, string extension) =>
        files.Count(f => MatchesGroup(f, namePrefix, extension));

    /// <summary>
    /// True for the four DayZ log groups the cleanup owns: the <c>DayZServer_x64_*.RPT</c>
    /// crash dumps, the <c>script_*.log</c>, <c>crash_*.log</c> and <c>warning_*.log</c>
    /// files. Everything else (e.g. <c>settings.cfg</c>) is left alone.
    /// </summary>
    private static bool IsServerLogFile(string fullPath) =>
        MatchesGroup(fullPath, "DayZServer_x64_", ".rpt")
        || MatchesGroup(fullPath, "script_", ".log")
        || MatchesGroup(fullPath, "crash_", ".log")
        || MatchesGroup(fullPath, "warning_", ".log");

    private static bool MatchesGroup(string fullPath, string namePrefix, string extension)
    {
        string name = Path.GetFileName(fullPath);
        return name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}
