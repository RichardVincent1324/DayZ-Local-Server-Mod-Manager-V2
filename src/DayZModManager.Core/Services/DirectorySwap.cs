using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Replaces a live folder's contents with a source folder using a staged,
/// rollback-safe swap: stage into <c>&lt;live&gt;.restore_&lt;guid&gt;</c>, move
/// the live folder aside to <c>&lt;live&gt;.old</c>, promote the staging folder,
/// and roll back on failure. A null/absent source produces an empty live folder.
/// Used for the mission's <c>db\ModTypes</c> folder.
/// </summary>
internal static class DirectorySwap
{
    internal const string TemporarySuffix = ".restore";

    internal const string OldBackupSuffix = ".old";

    public static bool TryReplace(IFileSystem fileSystem, string live, string? source, out string error)
    {
        error = string.Empty;
        string temp = live + TemporarySuffix + "_" + Guid.NewGuid().ToString("N");
        string backup = live + OldBackupSuffix;

        try
        {
            if (source is not null && fileSystem.DirectoryExists(source))
            {
                fileSystem.CopyDirectory(source, temp);
            }
            else
            {
                fileSystem.CreateDirectory(temp);
            }

            if (fileSystem.DirectoryExists(live))
            {
                // The live folder is intact here, so a stale backup from a
                // previous interrupted run may safely be removed before the move.
                TryDeleteDirectory(fileSystem, backup);
                fileSystem.MoveDirectory(live, backup);
            }

            try
            {
                fileSystem.MoveDirectory(temp, live);
            }
            catch
            {
                // Roll the previous contents back if promotion fails.
                if (!fileSystem.DirectoryExists(live) && fileSystem.DirectoryExists(backup))
                {
                    fileSystem.MoveDirectory(backup, live);
                }

                throw;
            }
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(fileSystem, temp);
            error = ex.Message;
            return false;
        }

        // Success: the backup is only removed best-effort.
        TryDeleteDirectory(fileSystem, backup);
        return true;
    }

    /// <summary>
    /// Removes stale <c>&lt;leaf&gt;.restore*</c> staging folders left beside a
    /// live folder by an interrupted swap. Never throws.
    /// </summary>
    public static void CleanStaleStaging(IFileSystem fileSystem, string live)
    {
        string? parent = Path.GetDirectoryName(live);
        string leaf = Path.GetFileName(live);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(leaf))
        {
            return;
        }

        try
        {
            if (!fileSystem.DirectoryExists(parent))
            {
                return;
            }

            string prefix = leaf + TemporarySuffix;
            foreach (string name in fileSystem.GetDirectories(parent))
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(fileSystem, Path.Combine(parent, name));
                }
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup must never fail the surrounding operation.
        }
    }

    private static void TryDeleteDirectory(IFileSystem fileSystem, string path)
    {
        try
        {
            if (fileSystem.DirectoryExists(path))
            {
                fileSystem.DeleteDirectory(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort: a locked file must not break the surrounding operation.
        }
    }
}
