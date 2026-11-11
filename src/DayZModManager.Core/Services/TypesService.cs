using DayZModManager.Core;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Services;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of a types operation.</summary>
public sealed record TypesOperationResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when the operation found an unmarked V2-related <c>&lt;ce&gt;</c> block.
    /// Used to log the warning on every detection and to raise a session-scoped alert.
    /// </summary>
    public bool UnmanagedV2CeBlockDetected { get; init; }
}

/// <summary>
/// Filesystem-driven types management. The preset's <c>type_files</c> folder
/// determines which files exist; the supplied assignment dictionary determines
/// each file's economy role.
/// </summary>
public interface ITypesService
{
    /// <summary>Returns the full paths of every XML file in a mod (import discovery).</summary>
    IReadOnlyList<string> DiscoverXmlFiles(string workshopPath, string modName);

    /// <summary>
    /// Copies the selected XML files into the preset's type_files folder. When
    /// <paramref name="overwrite"/> is false, existing files are skipped rather
    /// than overwritten. No roles are assigned.
    /// </summary>
    TypesOperationResult ImportFiles(
        IReadOnlyList<string> sourceFiles,
        string typeFilesFolder,
        bool overwrite);

    /// <summary>
    /// Returns the managed type-file entries in cfgeconomycore.xml order: assigned
    /// files scanned freshly from the directory, with all "types" entries before
    /// "spawnabletypes" entries, each group natural-sorted.
    /// </summary>
    IReadOnlyList<ManagedTypeFileEntry> GetActiveTypeFiles(
        string typeFilesFolder,
        IReadOnlyDictionary<string, string> assignments);

    /// <summary>
    /// Rewrites the manager-owned CE block from a fresh scan of the type_files
    /// folder and the supplied role assignments.
    /// </summary>
    TypesOperationResult SyncEconomyCore(
        string missionPath,
        string folder,
        string typeFilesFolder,
        IReadOnlyDictionary<string, string> assignments);

    /// <summary>Returns the CE block's folder attribute, or null when none exists.</summary>
    string? GetActiveTypesFolder(string missionPath);

    /// <summary>Returns the managed CE block entries in document order.</summary>
    IReadOnlyList<ManagedTypeFileEntry> GetManagedTypeFiles(string missionPath);
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

    public TypesOperationResult ImportFiles(
        IReadOnlyList<string> sourceFiles,
        string typeFilesFolder,
        bool overwrite)
    {
        var messages = new List<string>();
        int copied = 0;
        int skipped = 0;

        try
        {
            _fileSystem.CreateDirectory(typeFilesFolder);
        }
        catch (Exception ex)
        {
            return new TypesOperationResult
            {
                Success = false,
                Messages = new[] { $"Failed to create type_files folder: {ex.Message}" },
            };
        }

        foreach (string source in sourceFiles)
        {
            string destinationName = Path.GetFileName(source);
            if (string.IsNullOrWhiteSpace(destinationName))
            {
                continue;
            }

            string destination = Path.Combine(typeFilesFolder, destinationName);
            if (!overwrite && _fileSystem.FileExists(destination))
            {
                skipped++;
                messages.Add($"Skipped {destinationName} (already exists)");
                continue;
            }

            try
            {
                _fileSystem.CopyFile(source, destination);
                copied++;
                messages.Add($"Copied {destinationName}");
            }
            catch (Exception ex)
            {
                messages.Add($"Failed to copy {destinationName}: {ex.Message}");
                return new TypesOperationResult { Success = false, Messages = messages };
            }
        }

        messages.Add($"Imported {copied} file(s); skipped {skipped}.");
        return new TypesOperationResult { Success = true, Messages = messages };
    }

    public IReadOnlyList<ManagedTypeFileEntry> GetActiveTypeFiles(
        string typeFilesFolder,
        IReadOnlyDictionary<string, string> assignments) =>
        GetOrderedFiles(typeFilesFolder, assignments)
            .Select(file => new ManagedTypeFileEntry(file.Name, file.Type))
            .ToList();

    public TypesOperationResult SyncEconomyCore(
        string missionPath,
        string folder,
        string typeFilesFolder,
        IReadOnlyDictionary<string, string> assignments)
    {
        IReadOnlyList<(string Name, string Type)> files;
        try
        {
            files = GetOrderedFiles(typeFilesFolder, assignments);
        }
        catch (Exception ex)
        {
            return new TypesOperationResult
            {
                Success = false,
                Messages = new[] { $"Failed to read type_files: {ex.Message}" },
            };
        }

        try
        {
            EconomyCoreUpdateResult result = _economyCore.UpdateTypeFiles(
                missionPath,
                folder,
                files.Select(file => file.Name).ToList(),
                files.ToDictionary(file => file.Name, file => file.Type, StringComparer.OrdinalIgnoreCase));

            return new TypesOperationResult
            {
                Success = result.Success,
                Messages = result.Warnings,
                UnmanagedV2CeBlockDetected = result.UnmanagedV2CeBlockDetected,
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

    public IReadOnlyList<ManagedTypeFileEntry> GetManagedTypeFiles(string missionPath) =>
        _economyCore.GetTypeFileEntries(missionPath);

    /// <summary>
    /// Scans type_files and returns assigned entries grouped by role: all "types"
    /// first, then all "spawnabletypes", each natural-sorted by filename.
    /// Unassigned files are excluded.
    /// </summary>
    private IReadOnlyList<(string Name, string Type)> GetOrderedFiles(
        string typeFilesFolder,
        IReadOnlyDictionary<string, string> assignments)
    {
        if (!_fileSystem.DirectoryExists(typeFilesFolder))
        {
            return Array.Empty<(string Name, string Type)>();
        }

        var files = new List<(string Name, string Type)>();
        foreach (string fullPath in _fileSystem.GetFiles(typeFilesFolder, "*.xml", recursive: false))
        {
            string name = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!assignments.TryGetValue(name, out string? stored)
                || !TypesFileRoles.TryNormalize(stored, out string role))
            {
                continue;
            }

            files.Add((name, role));
        }

        return files
            .OrderBy(file => file.Type == TypesFileRoles.Spawnabletypes)
            .ThenBy(file => file.Name, NaturalStringComparer.Instance)
            .ToList();
    }
}
