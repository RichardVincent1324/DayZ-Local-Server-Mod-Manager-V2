using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core;

/// <summary>
/// Resolves the real Steam Workshop mod folder from a user-selected path.
/// A common mistake is selecting the DayZ game folder
/// (<c>...\steamapps\common\DayZ</c>) instead of the Workshop folder nested
/// inside it (<c>...\DayZ\!Workshop</c>), which yields an empty mod list. This
/// helper transparently corrects that case.
/// </summary>
public static class WorkshopPathResolver
{
    /// <summary>Name of the DayZ Workshop subfolder that hosts the <c>@mod</c> folders.</summary>
    public const string WorkshopFolderName = "!Workshop";

    /// <summary>Name of the DayZ game folder installed by Steam.</summary>
    public const string GameFolderName = "DayZ";

    /// <summary>
    /// True when <paramref name="path"/> is a plausible DayZ Workshop or mods
    /// folder. A Steam folder is recognised by its name (<c>!Workshop</c> or the
    /// <c>DayZ</c> game folder); any other folder must directly contain at least
    /// one <c>@mod</c> directory. The content check is deliberately non-recursive:
    /// a parent folder such as <c>...\steamapps\common</c> that only holds the real
    /// folder one level deeper is rejected.
    /// </summary>
    public static bool IsValidWorkshopPath(IFileSystem fileSystem, string? path)
    {
        if (fileSystem is null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalized = Path.TrimEndingDirectorySeparator(path.Trim());
        if (!fileSystem.DirectoryExists(normalized))
        {
            return false;
        }

        string name = Path.GetFileName(normalized);
        if (string.Equals(name, WorkshopFolderName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, GameFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileSystem
            .GetDirectories(normalized)
            .Any(child => child.StartsWith("@", StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns the effective workshop folder for <paramref name="selectedPath"/>:
    /// the path itself when it is already the <c>!Workshop</c> folder (by name) or
    /// contains no <c>!Workshop</c> child, otherwise that child. Trailing
    /// separators are trimmed.
    /// </summary>
    public static string Resolve(IFileSystem fileSystem, string? selectedPath)
    {
        if (fileSystem is null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return selectedPath ?? string.Empty;
        }

        string path = Path.TrimEndingDirectorySeparator(selectedPath.Trim());
        if (string.Equals(Path.GetFileName(path), WorkshopFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string nested = Path.Combine(path, WorkshopFolderName);
        return fileSystem.DirectoryExists(nested) ? nested : path;
    }
}
