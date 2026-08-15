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
    /// Regenerates cfgeconomycore.xml referencing only the types of the mods in
    /// <paramref name="loadedModNames"/>. Returns false if the file is missing or malformed.
    /// </summary>
    bool SyncEconomyCore(
        TypesConfig config,
        string mapName,
        string missionPath,
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

        // Copy the selected files first so a mid-copy failure cannot leave the
        // previous configuration deleted or the config pointing at files that
        // were never written.
        string cleanModName = modName.StartsWith('@') ? modName[1..] : modName;
        var generated = new List<string>();
        var sourceRelative = new List<string>();
        var copied = new List<string>();

        foreach (string source in sourceFiles)
        {
            string relative = Path.GetRelativePath(modPath, source);
            string safeRelative = relative.Replace('\\', '_').Replace('/', '_');
            string destinationName = $"{cleanModName}_{safeRelative}";
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

        // Commit: replace the previous configuration only after every copy succeeded.
        var newGenerated = new HashSet<string>(generated.Select(g => Path.Combine(missionPath, g)), StringComparer.OrdinalIgnoreCase);
        ModTypesEntry? previous = map.Mods.FirstOrDefault(entry => string.Equals(entry.ModName, modName, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
        {
            foreach (string generatedFile in previous.GeneratedFiles)
            {
                if (newGenerated.Contains(Path.Combine(missionPath, generatedFile)))
                {
                    continue;
                }

                DeleteGenerated(missionPath, generatedFile, messages);
            }

            map.Mods.Remove(previous);
        }

        map.Mods.Add(new ModTypesEntry
        {
            ModName = modName,
            SourceFiles = sourceRelative,
            GeneratedFiles = generated,
        });

        return RegenerateEconomyCore(missionPath, map, messages, loadedModNames);
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

        foreach (string leaf in fileLeaves)
        {
            string? generated = entry.GeneratedFiles.FirstOrDefault(g => Path.GetFileName(g) == leaf);
            if (generated is null)
            {
                continue;
            }

            DeleteGenerated(missionPath, generated, messages);
            entry.GeneratedFiles.Remove(generated);
        }

        if (entry.GeneratedFiles.Count == 0)
        {
            map.Mods.Remove(entry);
            messages.Add($"Removed {modName} types config (no files remaining)");
        }

        return RegenerateEconomyCore(missionPath, map, messages, loadedModNames);
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
            return RegenerateEconomyCore(missionPath, null, messages, loadedModNames);
        }

        List<ModTypesEntry> invalid = map.Mods
            .Where(entry => !validModNames.Contains(entry.ModName))
            .ToList();

        foreach (ModTypesEntry entry in invalid)
        {
            foreach (string generated in entry.GeneratedFiles)
            {
                DeleteGenerated(missionPath, generated, messages);
            }

            map.Mods.Remove(entry);
            messages.Add($"Cleaned up types config for {entry.ModName} (mod is no longer active)");
        }

        if (invalid.Count == 0)
        {
            messages.Add("No invalid types configurations found.");
        }

        return RegenerateEconomyCore(missionPath, map, messages, loadedModNames);
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

        return _economyCore.UpdateModTypes(missionPath, fileNames);
    }

    private TypesOperationResult RegenerateEconomyCore(
        string missionPath,
        MapTypesConfig? map,
        List<string> messages,
        IReadOnlySet<string> loadedModNames)
    {
        IReadOnlyList<string> fileNames = map is null
            ? Array.Empty<string>()
            : GetAllGeneratedFileNames(map, loadedModNames);

        if (!_economyCore.UpdateModTypes(missionPath, fileNames))
        {
            messages.Add("Failed to update cfgeconomycore.xml.");
            return new TypesOperationResult { Success = false, Messages = messages };
        }

        return new TypesOperationResult { Success = true, Messages = messages };
    }

    private static IReadOnlyList<string> GetAllGeneratedFileNames(
        MapTypesConfig map,
        IReadOnlySet<string> loadedModNames) =>
        map.Mods
            .Where(entry => loadedModNames.Contains(entry.ModName))
            .SelectMany(entry => entry.GeneratedFiles)
            .Select(generated => Path.GetFileName(generated)!)
            .ToList();

    private void DeleteGenerated(string missionPath, string relativeFile, List<string> messages)
    {
        string fullPath = Path.Combine(missionPath, relativeFile);
        if (_fileSystem.FileExists(fullPath))
        {
            _fileSystem.DeleteFile(fullPath);
            messages.Add($"Deleted {Path.GetFileName(fullPath)}");
        }
    }

    private static MapTypesConfig? GetMap(TypesConfig config, string mapName) =>
        config.Maps.TryGetValue(mapName, out MapTypesConfig? map) ? map : null;

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
