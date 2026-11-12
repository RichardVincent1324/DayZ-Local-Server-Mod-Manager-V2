using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// The single definition of which Workshop mods are present. Discovery, validation
/// and the launch preflight all go through it, so they can never disagree about
/// whether a loaded mod is missing.
/// </summary>
public static class WorkshopMods
{
    /// <summary>Prefix every DayZ Workshop mod folder carries.</summary>
    public const string NamePrefix = "@";

    /// <summary>
    /// Returns the names (not paths) of the mod folders directly inside
    /// <paramref name="workshopPath"/>, in directory order. Returns an empty list
    /// when the path is blank or does not exist; callers that must distinguish that
    /// case from "the folder has no mods" should check the path first.
    /// </summary>
    public static IReadOnlyList<string> FindPresent(IFileSystem fileSystem, string workshopPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (string.IsNullOrWhiteSpace(workshopPath) || !fileSystem.DirectoryExists(workshopPath))
        {
            return Array.Empty<string>();
        }

        return fileSystem
            .GetDirectories(workshopPath)
            .Where(name => name.StartsWith(NamePrefix, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Returns the loaded mods that are not present in the Workshop folder, in their
    /// original load order. Returns an empty list only when the Workshop path is
    /// unset or missing: an unusable path must never be reported as "every mod is
    /// missing". A readable but empty Workshop folder does report every loaded mod
    /// as missing, because that is what it means.
    /// </summary>
    public static IReadOnlyList<string> FindMissing(
        IFileSystem fileSystem,
        string workshopPath,
        IReadOnlyList<string> loadedMods)
    {
        ArgumentNullException.ThrowIfNull(loadedMods);

        if (string.IsNullOrWhiteSpace(workshopPath) || !fileSystem.DirectoryExists(workshopPath))
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string> present = FindPresent(fileSystem, workshopPath);
        var presentSet = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
        return loadedMods
            .Where(mod => !presentSet.Contains(mod))
            .ToList();
    }
}
