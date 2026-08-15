using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>Discovers maps in the server's <c>mpmissions</c> directory.</summary>
public interface IMapService
{
    /// <summary>
    /// Returns the mission folders under <c>mpmissions</c> that contain a
    /// <c>cfgeconomycore.xml</c>, sorted by name.
    /// </summary>
    IReadOnlyList<MapInfo> DiscoverMaps(string serverPath);

    /// <summary>Resolves a map folder name to its full path, or null if not found.</summary>
    string? ResolveMapPath(string serverPath, string mapName);
}

public sealed class MapService : IMapService
{
    private const string EconomyCoreFile = "cfgeconomycore.xml";

    private readonly IFileSystem _fileSystem;

    public MapService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public IReadOnlyList<MapInfo> DiscoverMaps(string serverPath)
    {
        string missionsPath = Path.Combine(serverPath, "mpmissions");
        if (!_fileSystem.DirectoryExists(missionsPath))
        {
            return Array.Empty<MapInfo>();
        }

        return _fileSystem
            .GetDirectories(missionsPath)
            .Select(name => new MapInfo(name, Path.Combine(missionsPath, name)))
            .Where(map => _fileSystem.FileExists(Path.Combine(map.Path, EconomyCoreFile)))
            .OrderBy(map => map.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? ResolveMapPath(string serverPath, string mapName)
    {
        return DiscoverMaps(serverPath)
            .FirstOrDefault(map => map.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase))
            ?.Path;
    }
}
