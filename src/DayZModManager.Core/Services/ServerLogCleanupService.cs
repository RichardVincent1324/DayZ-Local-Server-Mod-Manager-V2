using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a log-cleanup pass.</summary>
public sealed record ServerLogCleanupResult(bool FolderExists, int RptRemoved, int ScriptRemoved);

/// <summary>
/// Prunes old DayZ server log files inside a map profile folder
/// (<c>map_profiles\&lt;map&gt;</c>). Each file type (the <c>DayZServer_x64_*.RPT</c>
/// files and the <c>script_*.log</c> files) is trimmed independently and only when
/// more than <see cref="ServerLogCleanupService.PruneThreshold"/> files of that type
/// exist, keeping the three most recent. Everything else in the folder is untouched.
/// </summary>
public interface IServerLogCleanupService
{
    /// <summary>
    /// Trims each log type down to the newest <see cref="ServerLogCleanupService.RetainedPerGroup"/>
    /// files, but only when that type has more than <see cref="ServerLogCleanupService.PruneThreshold"/>
    /// files. Never throws when a file cannot be deleted (for example because the
    /// server is still writing it); such files are skipped and retried on the next run.
    /// </summary>
    ServerLogCleanupResult Cleanup(string profileFolderPath);
}

public sealed class ServerLogCleanupService : IServerLogCleanupService
{
    /// <summary>Number of most recent files retained per log type when a prune runs.</summary>
    public const int RetainedPerGroup = 3;

    /// <summary>
    /// A log type is only pruned when it has more than this many files; at or below
    /// the threshold no files are deleted. This keeps cleanup infrequent so an app
    /// open does not constantly churn the log folder.
    /// </summary>
    public const int PruneThreshold = 10;

    private readonly IFileSystem _fileSystem;

    public ServerLogCleanupService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ServerLogCleanupResult Cleanup(string profileFolderPath)
    {
        if (string.IsNullOrWhiteSpace(profileFolderPath) || !_fileSystem.DirectoryExists(profileFolderPath))
        {
            return new ServerLogCleanupResult(FolderExists: false, RptRemoved: 0, ScriptRemoved: 0);
        }

        IReadOnlyList<string> files = _fileSystem.GetFiles(profileFolderPath, "*", recursive: false);

        int rptRemoved = PruneGroup(files, "DayZServer_x64_", ".rpt");
        int scriptRemoved = PruneGroup(files, "script_", ".log");

        return new ServerLogCleanupResult(FolderExists: true, rptRemoved, scriptRemoved);
    }

    /// <summary>
    /// For files whose name starts with <paramref name="namePrefix"/> and ends with
    /// <paramref name="extension"/>, keeps the newest <see cref="RetainedPerGroup"/> and
    /// deletes the rest — but only when more than <see cref="PruneThreshold"/> such files
    /// exist. Returns the number deleted. Newest is decided by file name: DayZ names
    /// these files with a fixed-width <c>yyyy-MM-dd_HH-mm-ss</c> timestamp, so descending
    /// ordinal order equals newest-first.
    /// </summary>
    private int PruneGroup(IReadOnlyList<string> files, string namePrefix, string extension)
    {
        var matches = files
            .Where(f => MatchesGroup(f, namePrefix, extension))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count <= PruneThreshold)
        {
            return 0;
        }

        int removed = 0;
        for (int i = RetainedPerGroup; i < matches.Count; i++)
        {
            try
            {
                _fileSystem.DeleteFile(matches[i]);
                removed++;
            }
            catch (Exception)
            {
                // The file may be locked by a running server; skip it and retry later.
            }
        }

        return removed;
    }

    private static bool MatchesGroup(string fullPath, string namePrefix, string extension)
    {
        string name = Path.GetFileName(fullPath);
        return name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}
