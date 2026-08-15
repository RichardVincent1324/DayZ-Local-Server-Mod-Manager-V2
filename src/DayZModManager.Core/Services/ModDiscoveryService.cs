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

    public IReadOnlyList<string> DiscoverWorkshopMods(string workshopPath)
    {
        if (string.IsNullOrWhiteSpace(workshopPath) || !_fileSystem.DirectoryExists(workshopPath))
        {
            return Array.Empty<string>();
        }

        return _fileSystem
            .GetDirectories(workshopPath)
            .Where(name => name.StartsWith("@", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
