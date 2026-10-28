using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a types-configuration operation.</summary>
public sealed record TypesOperationResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when the operation found an unmanaged legacy <c>&lt;ce&gt;</c> block and
    /// created the V2-managed one alongside it. Used to raise a one-time alert.
    /// </summary>
    public bool UnmanagedLegacyBlockDetected { get; init; }
}

/// <summary>A configured type file: its mod-relative source, generated leaf and role.</summary>
public sealed record ConfiguredTypeFile(string SourceRelative, string GeneratedLeaf, TypesFileRole Role);

/// <summary>
/// Where a types operation writes its files and economy references. In the
/// preset-driven model the generated type files live in the preset's own
/// <see cref="TypeFilesFolder"/> (outside the mission). The managed block in
/// <c>cfgeconomycore.xml</c> uses the stable mission-local
/// <see cref="EconomyFolder"/> (<c>db/DZMMV2_Types</c>), which resolves through
/// an app-managed junction to that preset folder.
/// </summary>
public sealed record TypesTarget(string MissionPath, string TypeFilesFolder, string EconomyFolder);

/// <summary>
/// Launch-time problems with a preset's types configuration.
/// <see cref="MissingMods"/> are entries whose mod is no longer present in the
/// Workshop. <see cref="MissingFiles"/> are generated files of loaded mods that
/// <c>cfgeconomycore.xml</c> will reference but that are absent from the preset's
/// <c>type_files</c> folder. Untracked files and entries for merely-unloaded mods
/// are ignored: neither is referenced by <c>cfgeconomycore.xml</c>.
/// </summary>
public sealed record TypesLaunchIssues(
    IReadOnlyList<string> MissingMods,
    IReadOnlyList<string> MissingFiles)
{
    /// <summary>True when at least one issue should block a server launch.</summary>
    public bool HasBlockers => MissingMods.Count > 0 || MissingFiles.Count > 0;
}

/// <summary>
/// Manages "types" XML configuration for a preset: discovering candidate files in
/// a mod, copying them into the preset's <c>type_files</c> folder (tracking source
/// vs generated files), and keeping <c>cfgeconomycore.xml</c> in sync.
/// </summary>
public interface ITypesService
{
    /// <summary>
    /// Returns the full paths of every XML file in a mod. Discovery makes no
    /// attempt to classify them; the user assigns each file a role.
    /// </summary>
    IReadOnlyList<string> DiscoverXmlFiles(string workshopPath, string modName);

    /// <summary>
    /// Copies the selected source files for a mod into the preset's type_files
    /// folder, replacing any previous configuration for that mod, and updates
    /// cfgeconomycore.xml to reference only the types of the mods in
    /// <paramref name="loadedModNames"/>.
    /// </summary>
    TypesOperationResult ConfigureMod(
        TypesConfig config,
        TypesTarget target,
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
        string modName);

    /// <summary>Removes specific generated files (by leaf name) for a mod.</summary>
    TypesOperationResult RemoveFiles(
        TypesConfig config,
        TypesTarget target,
        string modName,
        IReadOnlySet<string> fileLeaves,
        IReadOnlySet<string> loadedModNames);

    /// <summary>Removes entries for mods not present in <paramref name="validModNames"/>.</summary>
    TypesOperationResult CleanInvalid(
        TypesConfig config,
        TypesTarget target,
        IReadOnlySet<string> validModNames,
        IReadOnlySet<string> loadedModNames);

    /// <summary>
    /// Deletes files (by leaf name) that physically exist in the preset's type_files
    /// folder but are not tracked by the config, and removes their references from
    /// <c>cfgeconomycore.xml</c>. Files the manager still tracks are never touched.
    /// </summary>
    TypesOperationResult RemoveUntrackedFiles(
        TypesConfig config,
        TypesTarget target,
        IReadOnlySet<string> fileLeaves);

    /// <summary>
    /// Rewrites the manager-owned (marked) type_files block in cfgeconomycore.xml
    /// to reference exactly the types of the mods in <paramref name="loadedModNames"/>
    /// from <paramref name="config"/>. <paramref name="folder"/> is written to the
    /// block's <c>folder</c> attribute (the active preset's type_files) and is
    /// rewritten on every call. Any unmanaged <c>ce</c> block is left untouched.
    /// Returns a result whose <see cref="TypesOperationResult.Messages"/> carry any
    /// warnings (for example, an unmanaged legacy block was found).
    /// </summary>
    TypesOperationResult SyncEconomyCore(
        TypesConfig? config,
        string missionPath,
        string folder,
        IReadOnlySet<string> loadedModNames);

    /// <summary>
    /// Returns the <c>folder</c> value of the manager-owned type_files block in a
    /// map's cfgeconomycore.xml, or null when none exists.
    /// </summary>
    string? GetActiveTypesFolder(string missionPath);

    /// <summary>
    /// Returns the file names currently listed by the manager-owned block in a
    /// map's cfgeconomycore.xml, in document order. Empty when none exist.
    /// </summary>
    IReadOnlyList<string> GetManagedTypeFileNames(string missionPath);

    /// <summary>
    /// Returns the managed block's file entries (name and economy type) in
    /// document order. Empty when none exist.
    /// </summary>
    IReadOnlyList<ManagedTypeFileEntry> GetManagedTypeFiles(string missionPath);

    /// <summary>
    /// Returns the active generated type-file leaf names for a map (in the same
    /// order cfgeconomycore.xml references them), considering only the mods in
    /// <paramref name="loadedModNames"/>. Empty when the map has no configuration.
    /// </summary>
    IReadOnlyList<string> GetActiveTypeFileNames(
        TypesConfig config,
        IReadOnlySet<string> loadedModNames);

    /// <summary>
    /// Returns the active generated type-file entries (leaf name and economy type)
    /// in the order cfgeconomycore.xml references them, considering only the mods
    /// in <paramref name="loadedModNames"/>.
    /// </summary>
    IReadOnlyList<ManagedTypeFileEntry> GetActiveTypeFiles(
        TypesConfig config,
        IReadOnlySet<string> loadedModNames);

    /// <summary>
    /// Returns the launch-blocking problems in a map's types configuration: entries
    /// whose mod is missing from the Workshop, and generated files of loaded mods
    /// that are absent from <paramref name="typeFilesFolder"/> (the files
    /// cfgeconomycore.xml will reference). Untracked files and entries for
    /// merely-unloaded mods are ignored.
    /// </summary>
    TypesLaunchIssues FindLaunchIssues(
        TypesConfig config,
        string typeFilesFolder,
        IReadOnlySet<string> workshopModNames,
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
        TypesTarget target,
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

        ModTypesEntry? previous = config.Mods.FirstOrDefault(entry => string.Equals(entry.ModName, modName, StringComparison.OrdinalIgnoreCase));
        int previousIndex = previous is null ? -1 : config.Mods.IndexOf(previous);

        // Files owned by other mods may not be overwritten: generated names are
        // built from the mod name + relative path, so two different mods can
        // flatten to the same leaf. Also collect the leaves this mod already owns
        // so a reconfiguration can replace its own files.
        var ownedByOtherMods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModTypesEntry entry in config.Mods)
        {
            if (string.Equals(entry.ModName, modName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string generatedFile in entry.GeneratedFiles)
            {
                string leaf = Path.GetFileName(generatedFile);
                if (!string.IsNullOrWhiteSpace(leaf))
                {
                    ownedByOtherMods.TryAdd(leaf, entry.ModName);
                }
            }
        }

        var ownLeaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (previous is not null)
        {
            foreach (string generatedFile in previous.GeneratedFiles)
            {
                string leaf = Path.GetFileName(generatedFile);
                if (!string.IsNullOrWhiteSpace(leaf))
                {
                    ownLeaves.Add(leaf);
                }
            }
        }

        // Copy the selected files first so a mid-copy failure cannot leave the
        // previous configuration deleted or the config pointing at files that
        // were never written.
        string cleanModName = CleanModName(modName);
        var generated = new List<string>();
        var sourceRelative = new List<string>();
        var fileRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newlyCreated = new List<string>();

        // Plan every destination before copying. The generated name is a pure
        // function of the source path; reject collisions with another selected
        // file, another mod's file, or an untracked file already in type_files
        // rather than silently overwriting any of them.
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

            if (ownedByOtherMods.TryGetValue(destinationName, out string? owner))
            {
                return new TypesOperationResult
                {
                    Success = false,
                    Messages = new[]
                    {
                        $"The generated file name \"{destinationName}\" is already used by mod \"{owner}\". " +
                        "Rename the source file or remove that mod's types configuration first.",
                    },
                };
            }

            if (!ownLeaves.Contains(destinationName)
                && _fileSystem.FileExists(Path.Combine(target.TypeFilesFolder, destinationName)))
            {
                return new TypesOperationResult
                {
                    Success = false,
                    Messages = new[]
                    {
                        $"A file named \"{destinationName}\" already exists in type_files but is not managed for {modName}. " +
                        "Remove it with Remove Selected before configuring this file, or rename the source file.",
                    },
                };
            }

            destinations[destinationName] = source;
            planned.Add((source, relative, role, destinationName));
        }

        foreach ((string source, string relative, TypesFileRole role, string destinationName) in planned)
        {
            string destinationFull = Path.Combine(target.TypeFilesFolder, destinationName);

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

            generated.Add(destinationName);
            sourceRelative.Add(relative);
            fileRoles[destinationName] = TypesFileRoles.ToEconomyType(role);
            messages.Add($"Copied {destinationName}");
        }

        // Commit the configuration in memory first so the economy rewrite below
        // reflects the final state. If cfgeconomycore.xml cannot be updated, the
        // in-memory change is rolled back and any files this attempt newly created
        // are removed - files that pre-existed (still referenced by the restored
        // entry) are left in place, so no config points at a missing file.
        var newGenerated = new HashSet<string>(generated.Select(g => Path.Combine(target.TypeFilesFolder, g)), StringComparer.OrdinalIgnoreCase);

        var newEntry = new ModTypesEntry
        {
            ModName = modName,
            SourceFiles = sourceRelative,
            GeneratedFiles = generated,
            FileRoles = fileRoles,
        };

        if (previous is not null)
        {
            config.Mods.Remove(previous);
        }

        config.Mods.Add(newEntry);

        if (!TryRegenerateEconomy(target, config, messages, loadedModNames).Success)
        {
            config.Mods.Remove(newEntry);
            if (previous is not null)
            {
                config.Mods.Insert(Math.Min(previousIndex, config.Mods.Count), previous);
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
                if (!newGenerated.Contains(Path.Combine(target.TypeFilesFolder, generatedFile)))
                {
                    DeleteGenerated(target.TypeFilesFolder, generatedFile, messages);
                }
            }
        }

        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public TypesOperationResult RemoveFiles(
        TypesConfig config,
        TypesTarget target,
        string modName,
        IReadOnlySet<string> fileLeaves,
        IReadOnlySet<string> loadedModNames)
    {
        var messages = new List<string>();
        ModTypesEntry? entry = config.Mods.FirstOrDefault(m => string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return new TypesOperationResult { Success = true, Messages = messages };
        }

        // Snapshot before removal so the current state can be restored if the
        // economy rewrite fails.
        int entryIndex = config.Mods.IndexOf(entry);
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
            config.Mods.Remove(entry);
            entryRemoved = true;
        }

        if (!TryRegenerateEconomy(target, config, messages, loadedModNames).Success)
        {
            if (entryRemoved)
            {
                config.Mods.Insert(Math.Min(entryIndex, config.Mods.Count), entry);
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
            DeleteGenerated(target.TypeFilesFolder, generated, messages);
        }

        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public TypesOperationResult CleanInvalid(
        TypesConfig config,
        TypesTarget target,
        IReadOnlySet<string> validModNames,
        IReadOnlySet<string> loadedModNames)
    {
        var messages = new List<string>();
        List<ModTypesEntry> invalid = config.Mods
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
            removedIndexes[entry] = config.Mods.IndexOf(entry);
            config.Mods.Remove(entry);
        }

        if (!TryRegenerateEconomy(target, config, messages, loadedModNames).Success)
        {
            foreach (ModTypesEntry entry in invalid)
            {
                config.Mods.Insert(Math.Min(removedIndexes[entry], config.Mods.Count), entry);
            }

            return new TypesOperationResult { Success = false, Messages = messages };
        }

        foreach (ModTypesEntry entry in invalid)
        {
            foreach (string generated in entry.GeneratedFiles)
            {
                DeleteGenerated(target.TypeFilesFolder, generated, messages);
            }

            messages.Add($"Cleaned up types config for {entry.ModName} (mod is no longer active)");
        }

        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public TypesOperationResult SyncEconomyCore(
        TypesConfig? config,
        string missionPath,
        string folder,
        IReadOnlySet<string> loadedModNames)
    {
        IReadOnlyList<(string Leaf, string Type)> files = config is null
            ? Array.Empty<(string Leaf, string Type)>()
            : GetOrderedEconomyFiles(config, loadedModNames);

        try
        {
            EconomyCoreUpdateResult result = _economyCore.UpdateTypeFiles(
                missionPath,
                folder,
                files.Select(file => file.Leaf).ToList(),
                GetEconomyFileTypes(files));

            return new TypesOperationResult
            {
                Success = result.Success,
                Messages = result.Warnings,
                UnmanagedLegacyBlockDetected = result.UnmanagedLegacyBlockDetected,
            };
        }
        catch (Exception ex)
        {
            return new TypesOperationResult
            {
                Success = false,
                Messages = new[] { $"Failed to update cfgeconomycore.xml: {ex.Message}" },
            };
        }
    }

    public string? GetActiveTypesFolder(string missionPath) => _economyCore.GetTypeFilesFolder(missionPath);

    public IReadOnlyList<string> GetManagedTypeFileNames(string missionPath) =>
        _economyCore.GetTypeFileNames(missionPath);

    public IReadOnlyList<ManagedTypeFileEntry> GetManagedTypeFiles(string missionPath) =>
        _economyCore.GetTypeFileEntries(missionPath);

    /// <summary>Convenience overload targeting the stable mission-local managed folder.</summary>
    public TypesOperationResult SyncEconomyCore(
        TypesConfig config,
        string missionPath,
        IReadOnlySet<string> loadedModNames) =>
        SyncEconomyCore(config, missionPath, EconomyCoreService.ManagedFolder, loadedModNames);

    public TypesOperationResult RemoveUntrackedFiles(
        TypesConfig config,
        TypesTarget target,
        IReadOnlySet<string> fileLeaves)
    {
        var messages = new List<string>();

        // Never touch files the manager still tracks.
        IReadOnlySet<string> owned = GetAllOwnedFileNames(config);

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
        if (!TryRemoveEconomyEntries(target.MissionPath, removed, messages))
        {
            return new TypesOperationResult { Success = false, Messages = messages };
        }

        foreach (string leaf in removed)
        {
            DeleteGenerated(target.TypeFilesFolder, leaf, messages);
        }

        messages.Add($"Removed {removed.Count} untracked type file(s) from type_files");
        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public IReadOnlyList<string> GetActiveTypeFileNames(
        TypesConfig config,
        IReadOnlySet<string> loadedModNames)
    {
        return GetAllGeneratedFileNames(config, loadedModNames);
    }

    public IReadOnlyList<ManagedTypeFileEntry> GetActiveTypeFiles(
        TypesConfig config,
        IReadOnlySet<string> loadedModNames)
    {
        return GetOrderedEconomyFiles(config, loadedModNames)
            .Select(file => new ManagedTypeFileEntry(file.Leaf, file.Type))
            .ToList();
    }

    public TypesLaunchIssues FindLaunchIssues(
        TypesConfig config,
        string typeFilesFolder,
        IReadOnlySet<string> workshopModNames,
        IReadOnlySet<string> loadedModNames)
    {
        List<string> missingMods = config.Mods
            .Where(entry => !workshopModNames.Contains(entry.ModName))
            .Select(entry => entry.ModName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingFiles = new List<string>();
        foreach (ModTypesEntry entry in config.Mods.Where(entry => loadedModNames.Contains(entry.ModName)))
        {
            foreach (string generated in entry.GeneratedFiles)
            {
                string leaf = Path.GetFileName(generated);
                if (string.IsNullOrWhiteSpace(leaf))
                {
                    continue;
                }

                if (!_fileSystem.FileExists(Path.Combine(typeFilesFolder, leaf)))
                {
                    missingFiles.Add(leaf);
                }
            }
        }

        return new TypesLaunchIssues(missingMods, missingFiles);
    }

    public IReadOnlyList<ConfiguredTypeFile> GetConfiguredFiles(
        TypesConfig config,
        string modName)
    {
        var files = new List<ConfiguredTypeFile>();
        ModTypesEntry? entry = config.Mods
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
        TypesTarget target,
        TypesConfig? config,
        List<string> messages,
        IReadOnlySet<string> loadedModNames)
    {
        IReadOnlyList<(string Leaf, string Type)> files = config is null
            ? Array.Empty<(string Leaf, string Type)>()
            : GetOrderedEconomyFiles(config, loadedModNames);

        bool unmanagedLegacy = false;
        try
        {
            EconomyCoreUpdateResult result = _economyCore.UpdateTypeFiles(
                target.MissionPath,
                target.EconomyFolder,
                files.Select(file => file.Leaf).ToList(),
                GetEconomyFileTypes(files));

            unmanagedLegacy = result.UnmanagedLegacyBlockDetected;
            messages.AddRange(result.Warnings);
            if (result.Success)
            {
                return new TypesOperationResult
                {
                    Success = true,
                    Messages = messages,
                    UnmanagedLegacyBlockDetected = unmanagedLegacy,
                };
            }
        }
        catch (Exception ex)
        {
            messages.Add($"Failed to update cfgeconomycore.xml: {ex.Message}");
            return new TypesOperationResult { Success = false, Messages = messages };
        }

        messages.Add("Failed to update cfgeconomycore.xml.");
        return new TypesOperationResult
        {
            Success = false,
            Messages = messages,
            UnmanagedLegacyBlockDetected = unmanagedLegacy,
        };
    }

    private bool TryRemoveEconomyEntries(string missionPath, IReadOnlyList<string> names, List<string> messages)
    {
        var targets = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_economyCore.RemoveTypeFiles(missionPath, targets))
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
        TypesConfig config,
        IReadOnlySet<string> loadedModNames) =>
        GetOrderedEconomyFiles(config, loadedModNames)
            .Select(file => file.Leaf)
            .ToList();

    /// <summary>
    /// Returns the generated files of the loaded mods in cfgeconomycore.xml order:
    /// mods in configuration order, and within a mod regular types before
    /// spawnabletypes using each file's explicit role. OrderBy is stable so ties
    /// keep their existing (copy) order.
    /// </summary>
    private static IReadOnlyList<(string Leaf, string Type)> GetOrderedEconomyFiles(
        TypesConfig config,
        IReadOnlySet<string> loadedModNames) =>
        config.Mods
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

    private static IReadOnlySet<string> GetAllOwnedFileNames(TypesConfig config) =>
        config.Mods
            .SelectMany(entry => entry.GeneratedFiles)
            .Select(generated => Path.GetFileName(generated)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Deletes a generated file, adding a message. Failures are reported
    /// but never thrown: deletion runs only after the economy file was updated, so
    /// a locked file degrades to a (re-cleanable) leftover instead of aborting the
    /// already-committed configuration.</summary>
    private void DeleteGenerated(string typeFilesFolder, string leaf, List<string> messages)
    {
        string fullPath = Path.Combine(typeFilesFolder, leaf);
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

    private static string CleanModName(string modName) =>
        modName.StartsWith('@') ? modName[1..] : modName;

    /// <summary>Builds the destination file name for a mod-relative source path.</summary>
    private static string BuildDestinationName(string cleanModName, string relative)
    {
        string safeRelative = relative.Replace('\\', '_').Replace('/', '_');
        return $"{cleanModName}_{safeRelative}";
    }

}
