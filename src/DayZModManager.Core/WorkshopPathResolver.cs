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
