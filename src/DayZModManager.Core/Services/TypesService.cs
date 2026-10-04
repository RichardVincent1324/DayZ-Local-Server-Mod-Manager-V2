using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a types-configuration operation.</summary>
public sealed record TypesOperationResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>A configured type file: its mod-relative source, generated leaf and role.</summary>
public sealed record ConfiguredTypeFile(string SourceRelative, string GeneratedLeaf, TypesFileRole Role);

/// <summary>
/// Manages "types" XML configuration for maps: discovering candidate files in a
/// mod, copying them into the mission's <c>db\ModTypes</c> folder (tracking
/// source vs generated files), and keeping <c>cfgeconomycore.xml</c> in sync.
/// </summary>
public interface ITypesService
{
    /// <summary>
    /// Returns the full paths of every XML file in a mod. Discovery makes no
    /// attempt to classify them; the user assigns each file a role.
    /// </summary>
    IReadOnlyList<string> DiscoverXmlFiles(string workshopPath, string modName);

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
        IReadOnlyList<TypeFileSelection> selections,
        IReadOnlySet<string> loadedModNames);

    /// <summary>
    /// Returns the files a mod currently has configured, pairing each source file
    /// with the generated leaf name and role it is copied under. Empty when the
    /// mod is not configured. Used by the picker to pre-select a file by its
    /// source path, so a numbered generated name is handled transparently.
    /// </summary>
    IReadOnlyList<ConfiguredTypeFile> GetConfiguredFiles(
        TypesConfig config,
        string mapName,
        string modName);

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
    /// Regenerates the manager-owned ModTypes block in cfgeconomycore.xml
    /// referencing only the types of the mods in <paramref name="loadedModNames"/>
    /// from <paramref name="map"/>. <paramref name="folder"/> is written to the
    /// block's <c>folder</c> attribute (configured db\ModTypes, or a loaded save's
    /// types folder). Returns false if the file is missing or malformed.
    /// </summary>
    /// <param name="previouslyOwned">
    /// Type-file leaf names the manager owned before the current configuration was
    /// installed (e.g. the config replaced when a save was loaded). They are added
    /// to the owned set so their now-removed files are dropped from
    /// cfgeconomycore.xml instead of being preserved as third-party entries.
    /// </param>
    bool SyncEconomyCore(
        MapTypesConfig? map,
        string missionPath,
        string folder,
        IReadOnlySet<string> loadedModNames,
        IReadOnlySet<string>? previouslyOwned = null);

    /// <summary>
    /// Returns the <c>folder</c> value of the manager-owned ModTypes block in a
    /// map's cfgeconomycore.xml, or null when none exists. Used to rediscover
    /// whether the configured types or a loaded save is active.
    /// </summary>
    string? GetActiveTypesFolder(string missionPath);

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

    public IReadOnlyList<string> DiscoverXmlFiles(string workshopPath, string modName)
    {
        string modPath = Path.Combine(workshopPath, modName);
        if (!_fileSystem.DirectoryExists(modPath))
        {
            return Array.Empty<string>();
        }

        return _fileSystem
            .GetFiles(modPath, "*.xml", recursive: true)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public TypesOperationResult ConfigureMod(
        TypesConfig config,
        string mapName,
        string missionPath,
        string workshopPath,
        string modName,
        IReadOnlyList<TypeFileSelection> selections,
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
        ModTypesEntry? previous = map.Mods.FirstOrDefault(entry => string.Equals(entry.ModName, modName, StringComparison.OrdinalIgnoreCase));
        int previousIndex = previous is null ? -1 : map.Mods.IndexOf(previous);

        // Copy the selected files first so a mid-copy failure cannot leave the
        // previous configuration deleted or the config pointing at files that
        // were never written.
        string cleanModName = CleanModName(modName);
        var generated = new List<string>();
        var sourceRelative = new List<string>();
        var fileRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newlyCreated = new List<string>();

        // Plan every destination before copying. The generated name is a pure
        // function of the source path, so the only collision possible is two
        // selected files that flatten to the same name; reject that rather than
        // let one copy silently overwrite the other.
        var planned = new List<(string Source, string Relative, TypesFileRole Role, string DestinationName)>(selections.Count);
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (TypeFileSelection selection in selections)
        {
            string source = selection.SourceFile;
            string relative = Path.GetRelativePath(modPath, source);
            TypesFileRole role = TypesFileRoles.EffectiveRole(Path.GetFileName(source), selection.Role);
            string destinationName = BuildDestinationName(cleanModName, relative);

            if (destinations.TryGetValue(destinationName, out string? existing))
            {
                return new TypesOperationResult
                {
                    Success = false,
                    Messages = new[]
                    {
                        $"Two selected files map to the same generated file \"{destinationName}\": {existing} and {source}. Deselect one of them.",
                    },
                };
            }

            destinations[destinationName] = source;
            planned.Add((source, relative, role, destinationName));
        }

        foreach ((string source, string relative, TypesFileRole role, string destinationName) in planned)
        {
            string destinationFull = Path.Combine(missionPath, "db", "ModTypes", destinationName);

            // A destination that already exists may belong to the previous
            // configuration (or another owner). It must never be deleted by the
            // rollback below, or a restored entry would reference a missing file.
            bool existedBefore = _fileSystem.FileExists(destinationFull);

            try
            {
                _fileSystem.CopyFile(source, destinationFull);
                if (!existedBefore)
                {
                    newlyCreated.Add(destinationFull);
                }
            }
            catch (Exception ex)
            {
                foreach (string created in newlyCreated)
                {
                    TryDeleteQuiet(created);
                }

                return new TypesOperationResult { Success = false, Messages = new[] { $"Failed to copy {source}: {ex.Message}" } };
            }

            generated.Add(Path.Combine("db", "ModTypes", destinationName));
            sourceRelative.Add(relative);
            fileRoles[destinationName] = TypesFileRoles.ToEconomyType(role);
            messages.Add($"Copied {destinationName}");
        }

        // Commit the configuration in memory first so the economy rewrite below
        // reflects the final state. If cfgeconomycore.xml cannot be updated, the
        // in-memory change is rolled back and any files this attempt newly created
        // are removed - files that pre-existed (still referenced by the restored
        // entry) are left in place, so no config points at a missing file.
        var newGenerated = new HashSet<string>(generated.Select(g => Path.Combine(missionPath, g)), StringComparer.OrdinalIgnoreCase);

        var newEntry = new ModTypesEntry
        {
            ModName = modName,
            SourceFiles = sourceRelative,
            GeneratedFiles = generated,
            FileRoles = fileRoles,
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

            foreach (string fullPath in newlyCreated)
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
        List<string> originalSources = new(entry.SourceFiles);
        var originalRoles = new Dictionary<string, string>(entry.FileRoles, StringComparer.OrdinalIgnoreCase);
        var removedGenerated = new List<string>();

        foreach (string leaf in fileLeaves)
        {
            int index = entry.GeneratedFiles.FindIndex(g =>
                string.Equals(Path.GetFileName(g), leaf, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            string generated = entry.GeneratedFiles[index];
            entry.GeneratedFiles.RemoveAt(index);
            entry.FileRoles.Remove(Path.GetFileName(generated));

            // SourceFiles is index-parallel to GeneratedFiles; keep it aligned so
            // a later role reconstruction does not read a stale source.
            if (index < entry.SourceFiles.Count)
            {
                entry.SourceFiles.RemoveAt(index);
            }

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
            entry.SourceFiles = originalSources;
            entry.FileRoles = originalRoles;
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
        MapTypesConfig? map,
        string missionPath,
        string folder,
        IReadOnlySet<string> loadedModNames,
        IReadOnlySet<string>? previouslyOwned = null)
    {
        IReadOnlyList<(string Leaf, string Type)> files = map is null
            ? Array.Empty<(string Leaf, string Type)>()
            : GetOrderedEconomyFiles(map, loadedModNames);

        // Ownership is the union of the current configuration and any ownership
        // carried over from the replaced configuration, so entries for files that
        // were owned before but are not desired now are recognized as stale and
        // removed (their physical files were already removed).
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (map is not null)
        {
            owned.UnionWith(GetAllOwnedFileNames(map));
        }

        if (previouslyOwned is not null)
        {
            owned.UnionWith(previouslyOwned);
        }

        try
        {
            return _economyCore.UpdateModTypes(
                missionPath,
                folder,
                files.Select(file => file.Leaf).ToList(),
                owned,
                GetEconomyFileTypes(files));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? GetActiveTypesFolder(string missionPath) => _economyCore.GetModTypesFolder(missionPath);

    /// <summary>Convenience overload that targets the configured <c>./db/ModTypes</c> folder.</summary>
    public bool SyncEconomyCore(
        TypesConfig config,
        string mapName,
        string missionPath,
        IReadOnlySet<string> loadedModNames,
        IReadOnlySet<string>? previouslyOwned = null) =>
        SyncEconomyCore(GetMap(config, mapName), missionPath, EconomyCoreService.ConfiguredFolder, loadedModNames, previouslyOwned);

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

    public IReadOnlyList<ConfiguredTypeFile> GetConfiguredFiles(
        TypesConfig config,
        string mapName,
        string modName)
    {
        var files = new List<ConfiguredTypeFile>();
        ModTypesEntry? entry = GetMap(config, mapName)?.Mods
            .FirstOrDefault(m => string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return files;
        }

        for (int i = 0; i < entry.GeneratedFiles.Count; i++)
        {
            string generated = entry.GeneratedFiles[i];
            string leaf = Path.GetFileName(generated);
            if (string.IsNullOrWhiteSpace(leaf))
            {
                continue;
            }

            string source = i < entry.SourceFiles.Count ? entry.SourceFiles[i] : string.Empty;
            TypesFileRole role = GetRole(entry, generated);
            files.Add(new ConfiguredTypeFile(source, leaf, role));
        }

        return files;
    }

    private TypesOperationResult TryRegenerateEconomy(
        string missionPath,
        MapTypesConfig? map,
        List<string> messages,
        IReadOnlySet<string> loadedModNames,
        IReadOnlySet<string> owned)
    {
        IReadOnlyList<(string Leaf, string Type)> files = map is null
            ? Array.Empty<(string Leaf, string Type)>()
            : GetOrderedEconomyFiles(map, loadedModNames);

        try
        {
            if (_economyCore.UpdateModTypes(
                missionPath,
                EconomyCoreService.ConfiguredFolder,
                files.Select(file => file.Leaf).ToList(),
                owned,
                GetEconomyFileTypes(files)))
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
        GetOrderedEconomyFiles(map, loadedModNames)
            .Select(file => file.Leaf)
            .ToList();

    /// <summary>
    /// Returns the generated files of the loaded mods in cfgeconomycore.xml order:
    /// mods in configuration order, and within a mod regular types before
    /// spawnabletypes using each file's explicit role. OrderBy is stable so ties
    /// keep their existing (copy) order.
    /// </summary>
    private static IReadOnlyList<(string Leaf, string Type)> GetOrderedEconomyFiles(
        MapTypesConfig map,
        IReadOnlySet<string> loadedModNames) =>
        map.Mods
            .Where(entry => loadedModNames.Contains(entry.ModName))
            .SelectMany(entry => entry.GeneratedFiles
                .Select(generated => (Leaf: Path.GetFileName(generated)!, Type: TypesFileRoles.ToEconomyType(GetRole(entry, generated))))
                .OrderBy(file => file.Type == TypesFileRoles.Spawnabletypes))
            .ToList();

    private static IReadOnlyDictionary<string, string> GetEconomyFileTypes(
        IReadOnlyList<(string Leaf, string Type)> files)
    {
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string leaf, string type) in files)
        {
            types[leaf] = type;
        }

        return types;
    }

    /// <summary>
    /// Returns the role a generated file is configured under from the explicit
    /// role map, defaulting to a types file when the map has no entry.
    /// </summary>
    private static TypesFileRole GetRole(ModTypesEntry entry, string generated)
    {
        string leaf = Path.GetFileName(generated);
        return entry.FileRoles.TryGetValue(leaf, out string? stored)
            ? TypesFileRoles.ToRole(stored)
            : TypesFileRole.Types;
    }

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
