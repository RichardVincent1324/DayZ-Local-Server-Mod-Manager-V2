using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a types-configuration operation.</summary>
public sealed record TypesOperationResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Manages "types" XML configuration for maps: discovering candidate files in a
/// mod, copying them into the mission's <c>db\ModTypes</c> folder (tracking
/// source vs generated files), and keeping <c>cfgeconomycore.xml</c> in sync.
/// </summary>
public interface ITypesService
{
    /// <summary>Returns the full paths of candidate types XML files in a mod (name contains "type").</summary>
    IReadOnlyList<string> DiscoverTypeFiles(string workshopPath, string modName);

    /// <summary>
    /// Returns the mission file leaf name the manager would generate for a source
    /// file (e.g. <c>InediaInfectedAI_Hardcore_types.xml</c>), or null when the
    /// source file does not live under the mod. Used to preview configuration
    /// changes before they are applied.
    /// </summary>
    string? GetGeneratedFileName(string workshopPath, string modName, string sourceFile);

    /// <summary>
    /// Copies the selected source files for a mod into the mission, replacing any
    /// previous configuration for that mod, and updates cfgeconomycore.xml to
    /// reference only the types of the mods in <paramref name="loadedModNames"/>.
    /// </summary>
    TypesOperationResult ConfigureMod(
        TypesConfig config,
        string mapName,
        string missionPath,
        string workshopPath,
        string modName,
        IReadOnlyList<string> sourceFiles,
        IReadOnlySet<string> loadedModNames);

    /// <summary>Removes specific generated files (by leaf name) for a mod.</summary>
    TypesOperationResult RemoveFiles(
        TypesConfig config,
        string mapName,
        string missionPath,
        string modName,
        IReadOnlySet<string> fileLeaves,
        IReadOnlySet<string> loadedModNames);

    /// <summary>Removes entries for mods not present in <paramref name="validModNames"/>.</summary>
    TypesOperationResult CleanInvalid(
        TypesConfig config,
        string mapName,
        string missionPath,
        IReadOnlySet<string> validModNames,
        IReadOnlySet<string> loadedModNames);

    /// <summary>
    /// Deletes files (by leaf name) that physically exist in the mission's
    /// <c>db\ModTypes</c> but are not tracked by the config, and removes their
    /// references from <c>cfgeconomycore.xml</c>. Files the manager still tracks
    /// are never touched. Used to clean up orphaned type files.
    /// </summary>
    TypesOperationResult RemoveUntrackedFiles(
        TypesConfig config,
        string mapName,
        string missionPath,
        IReadOnlySet<string> fileLeaves);

    /// <summary>
    /// Regenerates cfgeconomycore.xml referencing only the types of the mods in
    /// <paramref name="loadedModNames"/>. Returns false if the file is missing or malformed.
    /// </summary>
    bool SyncEconomyCore(
        TypesConfig config,
        string mapName,
        string missionPath,
        IReadOnlySet<string> loadedModNames);

    /// <summary>
    /// Returns the active generated type-file leaf names for a map (in the same
    /// order cfgeconomycore.xml references them), considering only the mods in
    /// <paramref name="loadedModNames"/>. Empty when the map has no configuration.
    /// </summary>
    IReadOnlyList<string> GetActiveTypeFileNames(
        TypesConfig config,
        string mapName,
        IReadOnlySet<string> loadedModNames);
}

public sealed class TypesService : ITypesService
{
    private readonly IFileSystem _fileSystem;
    private readonly IEconomyCoreService _economyCore;

    public TypesService(IFileSystem fileSystem, IEconomyCoreService economyCore)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _economyCore = economyCore ?? throw new ArgumentNullException(nameof(economyCore));
    }

    public IReadOnlyList<string> DiscoverTypeFiles(string workshopPath, string modName)
    {
        string modPath = Path.Combine(workshopPath, modName);
        if (!_fileSystem.DirectoryExists(modPath))
        {
            return Array.Empty<string>();
        }

        return _fileSystem
            .GetFiles(modPath, "*.xml", recursive: true)
            .Where(file => Path.GetFileName(file).Contains("type", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? GetGeneratedFileName(string workshopPath, string modName, string sourceFile)
    {
        string modPath = Path.Combine(workshopPath, modName);
        string? relative = GetRelativeWithin(modPath, sourceFile);
        return relative is null ? null : BuildDestinationName(CleanModName(modName), relative);
    }

    public TypesOperationResult ConfigureMod(
        TypesConfig config,
        string mapName,
        string missionPath,
        string workshopPath,
        string modName,
        IReadOnlyList<string> sourceFiles,
        IReadOnlySet<string> loadedModNames)
    {
        var messages = new List<string>();
        string modPath = Path.Combine(workshopPath, modName);

        if (!_fileSystem.DirectoryExists(modPath))
        {
            return new TypesOperationResult { Success = false, Messages = new[] { $"Mod not found in workshop: {modName}" } };
        }

        MapTypesConfig map = GetOrCreateMap(config, mapName);

        // Snapshot the files this manager previously generated for the map before
        // any mutation, so entries that are replaced below can still be removed
        // from cfgeconomycore.xml.
        IReadOnlySet<string> ownedBefore = GetAllOwnedFileNames(map);

        // Copy the selected files first so a mid-copy failure cannot leave the
        // previous configuration deleted or the config pointing at files that
        // were never written.
        string cleanModName = CleanModName(modName);
        var generated = new List<string>();
        var sourceRelative = new List<string>();
        var copied = new List<string>();

        foreach (string source in sourceFiles)
        {
            string relative = Path.GetRelativePath(modPath, source);
            string destinationName = BuildDestinationName(cleanModName, relative);
            string destinationFull = Path.Combine(missionPath, "db", "ModTypes", destinationName);

            try
            {
                _fileSystem.CopyFile(source, destinationFull);
                copied.Add(destinationFull);
            }
            catch (Exception ex)
            {
                foreach (string alreadyCopied in copied)
                {
                    _fileSystem.DeleteFile(alreadyCopied);
                }

                return new TypesOperationResult { Success = false, Messages = new[] { $"Failed to copy {source}: {ex.Message}" } };
            }

            generated.Add(Path.Combine("db", "ModTypes", destinationName));
            sourceRelative.Add(relative);
            messages.Add($"Copied {destinationName}");
        }

        // Commit the configuration in memory first so the economy rewrite below
        // reflects the final state. If cfgeconomycore.xml cannot be updated, the
        // in-memory change is rolled back and the newly copied files are removed
        // again - no partial configuration is ever left behind.
        var newGenerated = new HashSet<string>(generated.Select(g => Path.Combine(missionPath, g)), StringComparer.OrdinalIgnoreCase);
        ModTypesEntry? previous = map.Mods.FirstOrDefault(entry => string.Equals(entry.ModName, modName, StringComparison.OrdinalIgnoreCase));
        int previousIndex = previous is null ? -1 : map.Mods.IndexOf(previous);

        var newEntry = new ModTypesEntry
        {
            ModName = modName,
            SourceFiles = sourceRelative,
            GeneratedFiles = generated,
        };

        if (previous is not null)
        {
            map.Mods.Remove(previous);
        }

        map.Mods.Add(newEntry);

        if (!TryRegenerateEconomy(missionPath, map, messages, loadedModNames, ownedBefore).Success)
        {
            map.Mods.Remove(newEntry);
            if (previous is not null)
            {
                map.Mods.Insert(Math.Min(previousIndex, map.Mods.Count), previous);
            }

            foreach (string fullPath in copied)
            {
                TryDeleteQuiet(fullPath);
            }

            return new TypesOperationResult { Success = false, Messages = messages };
        }

        // Economy now references the new files; superseded files are removed only
        // after the update succeeded (best-effort so a locked file cannot abort an
        // otherwise-committed configuration).
        if (previous is not null)
        {
            foreach (string generatedFile in previous.GeneratedFiles)
            {
                if (!newGenerated.Contains(Path.Combine(missionPath, generatedFile)))
                {
                    DeleteGenerated(missionPath, generatedFile, messages);
                }
            }
        }

        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public TypesOperationResult RemoveFiles(
        TypesConfig config,
        string mapName,
        string missionPath,
        string modName,
        IReadOnlySet<string> fileLeaves,
        IReadOnlySet<string> loadedModNames)
    {
        var messages = new List<string>();
        MapTypesConfig? map = GetMap(config, mapName);
        ModTypesEntry? entry = map?.Mods.FirstOrDefault(m => string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase));
        if (map is null || entry is null)
        {
            return new TypesOperationResult { Success = true, Messages = messages };
        }

        // Snapshot before removal so the removed files are still recognized as
        // owned when cfgeconomycore.xml is regenerated.
        IReadOnlySet<string> ownedBefore = GetAllOwnedFileNames(map);
        int entryIndex = map.Mods.IndexOf(entry);
        List<string> originalGenerated = new(entry.GeneratedFiles);
        var removedGenerated = new List<string>();

        foreach (string leaf in fileLeaves)
        {
            string? generated = entry.GeneratedFiles.FirstOrDefault(g => Path.GetFileName(g) == leaf);
            if (generated is null)
            {
                continue;
            }

            entry.GeneratedFiles.Remove(generated);
            removedGenerated.Add(generated);
        }

        if (removedGenerated.Count == 0)
        {
            return new TypesOperationResult { Success = true, Messages = messages };
        }

        // Commit in memory first (an entry whose last file is removed disappears),
        // then rewrite cfgeconomycore.xml. On failure the config is rolled back and
        // no physical file has been deleted yet.
        bool entryRemoved = false;
        if (entry.GeneratedFiles.Count == 0)
        {
            map.Mods.Remove(entry);
            entryRemoved = true;
        }

        if (!TryRegenerateEconomy(missionPath, map, messages, loadedModNames, ownedBefore).Success)
        {
            if (entryRemoved)
            {
                map.Mods.Insert(Math.Min(entryIndex, map.Mods.Count), entry);
            }

            entry.GeneratedFiles = originalGenerated;
            return new TypesOperationResult { Success = false, Messages = messages };
        }

        if (entryRemoved)
        {
            messages.Add($"Removed {modName} types config (no files remaining)");
        }

        foreach (string generated in removedGenerated)
        {
            DeleteGenerated(missionPath, generated, messages);
        }

        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public TypesOperationResult CleanInvalid(
        TypesConfig config,
        string mapName,
        string missionPath,
        IReadOnlySet<string> validModNames,
        IReadOnlySet<string> loadedModNames)
    {
        var messages = new List<string>();
        MapTypesConfig? map = GetMap(config, mapName);

        if (map is null)
        {
            messages.Add("No types configuration present.");
            return TryRegenerateEconomy(missionPath, null, messages, loadedModNames,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        // Snapshot before removal so the cleaned-up files are still recognized as
        // owned when cfgeconomycore.xml is regenerated.
        IReadOnlySet<string> ownedBefore = GetAllOwnedFileNames(map);

        List<ModTypesEntry> invalid = map.Mods
            .Where(entry => !validModNames.Contains(entry.ModName))
            .ToList();

        if (invalid.Count == 0)
        {
            messages.Add("No invalid types configurations found.");
            return new TypesOperationResult { Success = true, Messages = messages };
        }

        // Remove the invalid entries in memory first so the economy rewrite below
        // reflects the final state. The entries are restored if that rewrite
        // fails; the physical files are only deleted after it succeeded.
        var removedIndexes = new Dictionary<ModTypesEntry, int>();
        foreach (ModTypesEntry entry in invalid)
        {
            removedIndexes[entry] = map.Mods.IndexOf(entry);
            map.Mods.Remove(entry);
        }

        if (!TryRegenerateEconomy(missionPath, map, messages, loadedModNames, ownedBefore).Success)
        {
            foreach (ModTypesEntry entry in invalid)
            {
                map.Mods.Insert(Math.Min(removedIndexes[entry], map.Mods.Count), entry);
            }

            return new TypesOperationResult { Success = false, Messages = messages };
        }

        foreach (ModTypesEntry entry in invalid)
        {
            foreach (string generated in entry.GeneratedFiles)
            {
                DeleteGenerated(missionPath, generated, messages);
            }

            messages.Add($"Cleaned up types config for {entry.ModName} (mod is no longer active)");
        }

        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public bool SyncEconomyCore(
        TypesConfig config,
        string mapName,
        string missionPath,
        IReadOnlySet<string> loadedModNames)
    {
        MapTypesConfig? map = GetMap(config, mapName);
        IReadOnlyList<string> fileNames = map is null
            ? Array.Empty<string>()
            : GetAllGeneratedFileNames(map, loadedModNames);
        IReadOnlySet<string> owned = map is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : GetAllOwnedFileNames(map);

        try
        {
            return _economyCore.UpdateModTypes(missionPath, fileNames, owned);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public TypesOperationResult RemoveUntrackedFiles(
        TypesConfig config,
        string mapName,
        string missionPath,
        IReadOnlySet<string> fileLeaves)
    {
        var messages = new List<string>();

        // Never touch files the manager still tracks for this map.
        MapTypesConfig? map = GetMap(config, mapName);
        IReadOnlySet<string> owned = map is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : GetAllOwnedFileNames(map);

        var removed = new List<string>();
        foreach (string leaf in fileLeaves)
        {
            if (string.IsNullOrWhiteSpace(leaf) || owned.Contains(leaf))
            {
                continue;
            }

            removed.Add(leaf);
        }

        if (removed.Count == 0)
        {
            return new TypesOperationResult { Success = true, Messages = messages };
        }

        // Drop the economy references first; only delete the physical files once
        // that succeeded so an unreadable/missing cfgeconomycore.xml never leaves
        // the on-disk references gone but the files still gone (or vice versa).
        if (!TryRemoveEconomyEntries(missionPath, removed, messages))
        {
            return new TypesOperationResult { Success = false, Messages = messages };
        }

        foreach (string leaf in removed)
        {
            DeleteGenerated(missionPath, Path.Combine("db", "ModTypes", leaf), messages);
        }

        messages.Add($"Removed {removed.Count} untracked type file(s) from db\\ModTypes");
        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public IReadOnlyList<string> GetActiveTypeFileNames(
        TypesConfig config,
        string mapName,
        IReadOnlySet<string> loadedModNames)
    {
        MapTypesConfig? map = GetMap(config, mapName);
        return map is null ? Array.Empty<string>() : GetAllGeneratedFileNames(map, loadedModNames);
    }

    private TypesOperationResult TryRegenerateEconomy(
        string missionPath,
        MapTypesConfig? map,
        List<string> messages,
        IReadOnlySet<string> loadedModNames,
        IReadOnlySet<string> owned)
    {
        IReadOnlyList<string> fileNames = map is null
            ? Array.Empty<string>()
            : GetAllGeneratedFileNames(map, loadedModNames);

        try
        {
            if (_economyCore.UpdateModTypes(missionPath, fileNames, owned))
            {
                return new TypesOperationResult { Success = true, Messages = messages };
            }
        }
        catch (Exception ex)
        {
            messages.Add($"Failed to update cfgeconomycore.xml: {ex.Message}");
            return new TypesOperationResult { Success = false, Messages = messages };
        }

        messages.Add("Failed to update cfgeconomycore.xml.");
        return new TypesOperationResult { Success = false, Messages = messages };
    }

    private bool TryRemoveEconomyEntries(string missionPath, IReadOnlyList<string> names, List<string> messages)
    {
        var targets = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_economyCore.RemoveModTypesFiles(missionPath, targets))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            messages.Add($"Failed to update cfgeconomycore.xml: {ex.Message}");
            return false;
        }

        messages.Add("Failed to update cfgeconomycore.xml.");
        return false;
    }

    private static IReadOnlyList<string> GetAllGeneratedFileNames(
        MapTypesConfig map,
        IReadOnlySet<string> loadedModNames) =>
        map.Mods
            .Where(entry => loadedModNames.Contains(entry.ModName))
            .SelectMany(entry => entry.GeneratedFiles
                // Within a mod, regular types must precede spawnabletypes; OrderBy
                // is stable so ties keep their existing (copy) order.
                .OrderBy(generated => Path.GetFileName(generated)!.Contains("spawnable", StringComparison.OrdinalIgnoreCase)))
            .Select(generated => Path.GetFileName(generated)!)
            .ToList();

    private static IReadOnlySet<string> GetAllOwnedFileNames(MapTypesConfig map) =>
        map.Mods
            .SelectMany(entry => entry.GeneratedFiles)
            .Select(generated => Path.GetFileName(generated)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Deletes a generated file, adding a message. Failures are reported
    /// but never thrown: deletion runs only after the economy file was updated, so
    /// a locked file degrades to a (re-cleanable) leftover instead of aborting the
    /// already-committed configuration.</summary>
    private void DeleteGenerated(string missionPath, string relativeFile, List<string> messages)
    {
        string fullPath = Path.Combine(missionPath, relativeFile);
        try
        {
            if (_fileSystem.FileExists(fullPath))
            {
                _fileSystem.DeleteFile(fullPath);
                messages.Add($"Deleted {Path.GetFileName(fullPath)}");
            }
        }
        catch (Exception ex)
        {
            messages.Add($"Failed to delete {Path.GetFileName(fullPath)}: {ex.Message}");
        }
    }

    /// <summary>Deletes a file, ignoring failures (used for rollback cleanup).</summary>
    private void TryDeleteQuiet(string fullPath)
    {
        try
        {
            if (_fileSystem.FileExists(fullPath))
            {
                _fileSystem.DeleteFile(fullPath);
            }
        }
        catch (Exception)
        {
            // Best effort: leftover copies are shown as untracked and cleaned later.
        }
    }

    private static MapTypesConfig? GetMap(TypesConfig config, string mapName) =>
        config.Maps.TryGetValue(mapName, out MapTypesConfig? map) ? map : null;

    private static string CleanModName(string modName) =>
        modName.StartsWith('@') ? modName[1..] : modName;

    /// <summary>Builds the destination file name for a mod-relative source path.</summary>
    private static string BuildDestinationName(string cleanModName, string relative)
    {
        string safeRelative = relative.Replace('\\', '_').Replace('/', '_');
        return $"{cleanModName}_{safeRelative}";
    }

    /// <summary>
    /// Returns the path of <paramref name="fullPath"/> relative to
    /// <paramref name="basePath"/>, or null when it is not a descendant.
    /// </summary>
    private static string? GetRelativeWithin(string basePath, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        string relative;
        try
        {
            relative = Path.GetRelativePath(basePath, fullPath);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return relative.Equals(".", StringComparison.Ordinal) || relative.StartsWith("..", StringComparison.Ordinal)
            ? null
            : relative;
    }

    private static MapTypesConfig GetOrCreateMap(TypesConfig config, string mapName)
    {
        if (!config.Maps.TryGetValue(mapName, out MapTypesConfig? map))
        {
            map = new MapTypesConfig();
            config.Maps[mapName] = map;
        }

        return map;
    }
}
