using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Scans the Steam Workshop directory for mod folders. Its only responsibility
/// is discovery — it does not touch the UI, create junctions, or persist anything.
/// </summary>
public sealed class ModDiscoveryService : IModDiscoveryService
{
    private readonly IFileSystem _fileSystem;

    public ModDiscoveryService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ModDiscoveryResult DiscoverWorkshopMods(string workshopPath)
    {
        // A path that cannot be read is reported as unavailable rather than empty:
        // callers must not infer that every loaded mod is missing.
        if (string.IsNullOrWhiteSpace(workshopPath) || !_fileSystem.DirectoryExists(workshopPath))
        {
            return ModDiscoveryResult.Unavailable;
        }

        IReadOnlyList<string> mods = WorkshopMods
            .FindPresent(_fileSystem, workshopPath)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModDiscoveryResult(PathAvailable: true, mods);
    }
}
