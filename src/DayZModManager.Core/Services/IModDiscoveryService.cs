namespace DayZModManager.Core.Services;

/// <summary>Discovers valid Steam Workshop mods (directories starting with '@').</summary>
public interface IModDiscoveryService
{
    /// <summary>
    /// Returns the sorted list of '@' prefixed directory names inside
    /// <paramref name="workshopPath"/>. Returns an empty list when the path is
    /// blank or does not exist.
    /// </summary>
    IReadOnlyList<string> DiscoverWorkshopMods(string workshopPath);
}
